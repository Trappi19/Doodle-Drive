using System.IO;
using DoodleDrive.Models;

namespace DoodleDrive.Services;

public enum SyncAction { UploadNew, UploadModified, DownloadNew, DownloadModified }

/// <summary>Une action prévue pour un fichier (résultat de « Vérifier »).</summary>
public sealed record SyncItemPlan(string RelPath, SyncAction Action);

/// <summary>Bilan d'une synchronisation.</summary>
public sealed record SyncResult(int Uploaded, int Downloaded, int Skipped, int Failed, IReadOnlyList<string> Errors)
{
    public int Changed => Uploaded + Downloaded;
}

/// <summary>
/// Moteur de synchronisation bidirectionnelle par différence de dates de modification.
/// Politique v1 : jamais de suppression (on ajoute/met à jour seulement), et en cas de
/// modification des deux côtés, le fichier le plus récent l'emporte. Les dates étant
/// préservées à l'upload/download, un fichier inchangé n'est jamais retransféré.
/// </summary>
public sealed class SyncService
{
    private readonly FtpService _ftp;
    public SyncService(FtpService ftp) => _ftp = ftp;

    // Tolérance sur les dates (granularité des systèmes de fichiers / arrondis réseau).
    private const double MtimeToleranceSeconds = 2;

    /// <summary>Calcule les actions sans rien transférer (« Vérifier »).</summary>
    public async Task<IReadOnlyList<SyncItemPlan>> PreviewAsync(string localRoot, string remoteRoot, CancellationToken ct = default)
    {
        var plan = await ComputePlanAsync(localRoot, FtpPathUtil.Normalize(remoteRoot), ct);
        return plan.Select(p => new SyncItemPlan(p.Rel, p.Action)).ToList();
    }

    /// <summary>Synchronise dans les deux sens et renvoie le bilan.</summary>
    public async Task<SyncResult> SyncAsync(string localRoot, string remoteRoot,
        IProgress<string>? status = null, CancellationToken ct = default)
    {
        remoteRoot = FtpPathUtil.Normalize(remoteRoot);
        Directory.CreateDirectory(localRoot);
        var knownRemoteDirs = new HashSet<string>(StringComparer.Ordinal);
        await EnsureRemoteDirAsync(remoteRoot, remoteRoot, knownRemoteDirs, ct);

        var plan = await ComputePlanAsync(localRoot, remoteRoot, ct);
        int up = 0, down = 0; var errors = new List<string>();

        foreach (var p in plan)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (p.Action is SyncAction.UploadNew or SyncAction.UploadModified)
                {
                    status?.Report($"Envoi : {p.Rel}");
                    await EnsureRemoteDirAsync(remoteRoot, FtpPathUtil.GetParent(p.RemotePath), knownRemoteDirs, ct);
                    await _ftp.UploadAsync(p.LocalPath, p.RemotePath, null, p.LocalMtimeUnix, ct);
                    up++;
                }
                else
                {
                    status?.Report($"Réception : {p.Rel}");
                    Directory.CreateDirectory(Path.GetDirectoryName(p.LocalPath)!);
                    await _ftp.DownloadAsync(p.RemotePath, p.LocalPath, null, ct);
                    if (p.RemoteModifiedUtc is { } rm)
                        try { File.SetLastWriteTimeUtc(p.LocalPath, rm); } catch { /* best-effort */ }
                    down++;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                errors.Add($"{p.Rel} : {ex.Message}");
            }
        }

        return new SyncResult(up, down, 0, errors.Count, errors);
    }

    // ------------------------------------------------------------------

    private sealed record PlanEntry(
        string Rel, SyncAction Action, string LocalPath, string RemotePath,
        long LocalMtimeUnix, DateTime? RemoteModifiedUtc);

    private async Task<List<PlanEntry>> ComputePlanAsync(string localRoot, string remoteRoot, CancellationToken ct)
    {
        // 1) Fichiers locaux (chemins relatifs en slashs avant).
        var local = new Dictionary<string, (string Full, long Mtime, long Size)>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(localRoot))
        {
            foreach (var f in Directory.EnumerateFiles(localRoot, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(localRoot, f).Replace('\\', '/');
                var info = new FileInfo(f);
                local[rel] = (f, ToUnix(info.LastWriteTimeUtc), info.Length);
            }
        }

        // 2) Fichiers distants (parcours récursif).
        var remote = new Dictionary<string, RemoteEntry>(StringComparer.OrdinalIgnoreCase);
        await WalkRemoteAsync(remoteRoot, "", remote, ct);

        // 3) Décision par fichier (union des deux ensembles).
        var plan = new List<PlanEntry>();
        foreach (var (rel, l) in local)
        {
            var remotePath = FtpPathUtil.Combine(remoteRoot, rel);
            if (!remote.TryGetValue(rel, out var r))
            {
                plan.Add(new PlanEntry(rel, SyncAction.UploadNew, l.Full, remotePath, l.Mtime, null));
                continue;
            }
            var rMtime = ToUnix(r.Modified.ToUniversalTime());
            if (l.Size == r.Size && Math.Abs(l.Mtime - rMtime) <= MtimeToleranceSeconds)
                continue; // identique -> rien à faire
            if (l.Mtime > rMtime + MtimeToleranceSeconds)
                plan.Add(new PlanEntry(rel, SyncAction.UploadModified, l.Full, remotePath, l.Mtime, null));
            else
                plan.Add(new PlanEntry(rel, SyncAction.DownloadModified, l.Full, remotePath, l.Mtime, r.Modified.ToUniversalTime()));
        }
        foreach (var (rel, r) in remote)
        {
            if (local.ContainsKey(rel)) continue; // déjà traité au-dessus
            var localPath = Path.Combine(localRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            plan.Add(new PlanEntry(rel, SyncAction.DownloadNew, localPath, r.FullPath, 0, r.Modified.ToUniversalTime()));
        }
        return plan;
    }

    private async Task WalkRemoteAsync(string dir, string relBase, Dictionary<string, RemoteEntry> map, CancellationToken ct)
    {
        IReadOnlyList<RemoteEntry> entries;
        try { entries = await _ftp.ListAsync(dir, ct); }
        catch (ApiException) { return; } // dossier distant absent : rien à parcourir
        foreach (var e in entries)
        {
            var rel = relBase.Length == 0 ? e.Name : relBase + "/" + e.Name;
            if (e.IsDirectory) await WalkRemoteAsync(e.FullPath, rel, map, ct);
            else map[rel] = e;
        }
    }

    /// <summary>Crée un dossier distant et tous ses ancêtres (jusqu'à la racine de synchro incluse).</summary>
    private async Task EnsureRemoteDirAsync(string remoteRoot, string remoteDir, HashSet<string> known, CancellationToken ct)
    {
        remoteDir = FtpPathUtil.Normalize(remoteDir);
        if (known.Contains(remoteDir)) return;

        // Crée d'abord le parent (tant qu'on reste sous la racine de synchro), du haut vers le bas.
        if (remoteDir != remoteRoot && FtpPathUtil.IsWithin(remoteDir, remoteRoot))
        {
            var parent = FtpPathUtil.GetParent(remoteDir);
            if (parent != remoteDir) await EnsureRemoteDirAsync(remoteRoot, parent, known, ct);
        }

        try { await _ftp.CreateDirectoryAsync(remoteDir, ct); }
        catch (ApiException) { /* existe déjà : OK */ }
        known.Add(remoteDir);
    }

    private static long ToUnix(DateTime utc) => ((DateTimeOffset)DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
}

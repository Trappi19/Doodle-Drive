using System.IO;
using DoodleDrive.Models;

namespace DoodleDrive.Services;

/// <summary>Sens d'une synchronisation.</summary>
public enum SyncDirection { Both, Upload, Download }

/// <summary>Aperçu (« Vérifier ») : nombre d'actions par catégorie.</summary>
public sealed record SyncPreview(int Upload, int Download, int DeleteLocal, int DeleteRemote, int Rename)
{
    public int Total => Upload + Download + DeleteLocal + DeleteRemote + Rename;
}

/// <summary>Bilan d'une synchronisation.</summary>
public sealed record SyncResult(int Uploaded, int Downloaded, int Deleted, int Renamed, int Failed, IReadOnlyList<string> Errors)
{
    public int Changed => Uploaded + Downloaded + Deleted + Renamed;
}

/// <summary>Progression en cours (actions traitées / total + fichier courant).</summary>
public sealed record SyncProgress(int Done, int Total, string CurrentFile)
{
    public double Percent => Total > 0 ? Done * 100.0 / Total : 0;
}

/// <summary>
/// Moteur de synchronisation.
/// <para><b>Sync (deux sens)</b> : vraie synchro three-way via un manifeste (état du dernier sync).
/// Propage ajouts, modifications, <b>suppressions</b> et <b>renommages</b>. Conflit modifié/modifié :
/// le plus récent gagne ; modifié d'un côté / supprimé de l'autre : on garde le fichier (aucune perte).
/// Au tout premier sync (manifeste vide), aucune suppression n'est propagée.</para>
/// <para><b>Envoyer / Récupérer</b> : copies simples et sûres (ajout + mise à jour, jamais de suppression).</para>
/// Les dates sont préservées de bout en bout, donc rien n'est retransféré inutilement.
/// </summary>
public sealed class SyncService
{
    private readonly FtpService _ftp;
    private readonly SyncStateStore _store;
    public SyncService(FtpService ftp, SyncStateStore store) { _ftp = ftp; _store = store; }

    private const double Tol = 2; // tolérance sur les dates (secondes)

    private enum Kind { Upload, Download, DeleteLocal, DeleteRemote, RenameRemote, RenameLocal }
    private sealed record Act(Kind Kind, string Rel, string? NewRel = null);
    private sealed record LocalFile(string Full, long Size, long Mtime);

    // =====================================================================
    //  Aperçu (« Vérifier »)
    // =====================================================================
    public async Task<SyncPreview> PreviewAsync(int pairId, string localRoot, string remoteRoot,
        SyncDirection direction, CancellationToken ct = default)
    {
        remoteRoot = FtpPathUtil.Normalize(remoteRoot);
        var local = ScanLocal(localRoot);
        var remote = await ScanRemoteAsync(remoteRoot, ct);
        var acts = direction == SyncDirection.Both
            ? BuildTwoWayPlan(local, remote, _store.Load(pairId))
            : BuildOneWayPlan(local, remote, direction);

        return new SyncPreview(
            acts.Count(a => a.Kind == Kind.Upload),
            acts.Count(a => a.Kind == Kind.Download),
            acts.Count(a => a.Kind == Kind.DeleteLocal),
            acts.Count(a => a.Kind == Kind.DeleteRemote),
            acts.Count(a => a.Kind is Kind.RenameLocal or Kind.RenameRemote));
    }

    // =====================================================================
    //  Synchronisation
    // =====================================================================
    public async Task<SyncResult> SyncAsync(int pairId, string localRoot, string remoteRoot,
        SyncDirection direction = SyncDirection.Both, IProgress<SyncProgress>? progress = null, CancellationToken ct = default)
    {
        remoteRoot = FtpPathUtil.Normalize(remoteRoot);
        Directory.CreateDirectory(localRoot);
        var knownRemoteDirs = new HashSet<string>(StringComparer.Ordinal);
        await EnsureRemoteDirAsync(remoteRoot, remoteRoot, knownRemoteDirs, ct);

        var local = ScanLocal(localRoot);
        var remote = await ScanRemoteAsync(remoteRoot, ct);
        var twoWay = direction == SyncDirection.Both;
        var acts = twoWay
            ? BuildTwoWayPlan(local, remote, _store.Load(pairId))
            : BuildOneWayPlan(local, remote, direction);

        int up = 0, down = 0, del = 0, ren = 0, done = 0, total = acts.Count;
        var errors = new List<string>();

        foreach (var a in acts)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new SyncProgress(done, total, a.NewRel ?? a.Rel));
            try
            {
                switch (a.Kind)
                {
                    case Kind.Upload:
                        await EnsureRemoteDirAsync(remoteRoot, FtpPathUtil.GetParent(RemoteFull(remoteRoot, a.Rel)), knownRemoteDirs, ct);
                        await _ftp.UploadAsync(local[a.Rel].Full, RemoteFull(remoteRoot, a.Rel), null, local[a.Rel].Mtime, ct: ct);
                        up++;
                        break;
                    case Kind.Download:
                    {
                        var lp = LocalFull(localRoot, a.Rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(lp)!);
                        await _ftp.DownloadAsync(RemoteFull(remoteRoot, a.Rel), lp, null, ct);
                        try { File.SetLastWriteTimeUtc(lp, remote[a.Rel].Modified.ToUniversalTime()); } catch { }
                        down++;
                        break;
                    }
                    case Kind.DeleteRemote:
                        await _ftp.DeleteFileAsync(RemoteFull(remoteRoot, a.Rel), ct);
                        del++;
                        break;
                    case Kind.DeleteLocal:
                    {
                        var lp = LocalFull(localRoot, a.Rel);
                        if (File.Exists(lp)) File.Delete(lp);
                        del++;
                        break;
                    }
                    case Kind.RenameRemote: // renommé en local -> on renomme le distant
                        await _ftp.RenameAsync(RemoteFull(remoteRoot, a.Rel), RemoteFull(remoteRoot, a.NewRel!), ct);
                        ren++;
                        break;
                    case Kind.RenameLocal:  // renommé en ligne -> on renomme le local
                    {
                        var from = LocalFull(localRoot, a.Rel);
                        var to = LocalFull(localRoot, a.NewRel!);
                        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                        if (File.Exists(from)) File.Move(from, to, overwrite: true);
                        ren++;
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { errors.Add($"{a.NewRel ?? a.Rel} : {ex.Message}"); }
            done++;
            progress?.Report(new SyncProgress(done, total, a.NewRel ?? a.Rel));
        }

        // Manifeste : on ne le met à jour QUE si tout a réussi (sinon on garde l'ancien pour
        // réessayer proprement, sans risquer de prendre un envoi échoué pour une suppression).
        if (twoWay && errors.Count == 0)
        {
            var after = ScanLocal(localRoot); // local == en ligne après un sync complet
            var manifest = after.ToDictionary(kv => kv.Key, kv => new FileState(kv.Value.Size, kv.Value.Mtime), StringComparer.Ordinal);
            _store.Save(pairId, manifest);
        }

        return new SyncResult(up, down, del, ren, errors.Count, errors);
    }

    // =====================================================================
    //  Construction des plans
    // =====================================================================

    /// <summary>Copie unidirectionnelle simple (ajout + mise à jour, sans suppression).</summary>
    private List<Act> BuildOneWayPlan(Dictionary<string, LocalFile> local, Dictionary<string, RemoteEntry> remote, SyncDirection dir)
    {
        var acts = new List<Act>();
        if (dir == SyncDirection.Upload)
        {
            foreach (var (rel, lf) in local)
                if (!remote.TryGetValue(rel, out var re) || Differs(lf.Size, lf.Mtime, re.Size, RMtime(re)))
                    acts.Add(new Act(Kind.Upload, rel));
        }
        else
        {
            foreach (var (rel, re) in remote)
                if (!local.TryGetValue(rel, out var lf) || Differs(lf.Size, lf.Mtime, re.Size, RMtime(re)))
                    acts.Add(new Act(Kind.Download, rel));
        }
        return acts;
    }

    /// <summary>Synchro bidirectionnelle three-way (avec suppressions et renommages).</summary>
    private List<Act> BuildTwoWayPlan(Dictionary<string, LocalFile> local, Dictionary<string, RemoteEntry> remote,
        Dictionary<string, FileState> baseline)
    {
        var acts = new List<Act>();
        var handled = new HashSet<string>(StringComparer.Ordinal);

        var localAdds = local.Keys.Where(k => !baseline.ContainsKey(k)).ToHashSet(StringComparer.Ordinal);
        var localDels = baseline.Keys.Where(k => !local.ContainsKey(k)).ToHashSet(StringComparer.Ordinal);
        var remoteAdds = remote.Keys.Where(k => !baseline.ContainsKey(k)).ToHashSet(StringComparer.Ordinal);
        var remoteDels = baseline.Keys.Where(k => !remote.ContainsKey(k)).ToHashSet(StringComparer.Ordinal);

        // Renommage LOCAL (même taille+date, même dossier) -> renommer le distant.
        foreach (var add in localAdds.ToList())
        {
            var lf = local[add];
            var match = localDels.FirstOrDefault(d =>
                baseline[d].Size == lf.Size && Math.Abs(baseline[d].MtimeUnix - lf.Mtime) <= Tol && Parent(d) == Parent(add));
            if (match is null) continue;
            if (remote.ContainsKey(match) && !remote.ContainsKey(add)) acts.Add(new Act(Kind.RenameRemote, match, add));
            localDels.Remove(match); localAdds.Remove(add); remoteDels.Remove(match);
            handled.Add(match); handled.Add(add);
        }

        // Renommage DISTANT -> renommer le local.
        foreach (var add in remoteAdds.ToList())
        {
            var re = remote[add]; var rm = RMtime(re);
            var match = remoteDels.FirstOrDefault(d =>
                baseline[d].Size == re.Size && Math.Abs(baseline[d].MtimeUnix - rm) <= Tol && Parent(d) == Parent(add));
            if (match is null) continue;
            if (local.ContainsKey(match) && !local.ContainsKey(add)) acts.Add(new Act(Kind.RenameLocal, match, add));
            remoteDels.Remove(match); remoteAdds.Remove(add); localDels.Remove(match);
            handled.Add(match); handled.Add(add);
        }

        var all = new HashSet<string>(local.Keys, StringComparer.Ordinal);
        all.UnionWith(remote.Keys);
        all.UnionWith(baseline.Keys);

        foreach (var rel in all)
        {
            if (handled.Contains(rel)) continue;
            var hasB = baseline.TryGetValue(rel, out var b);
            var hasL = local.TryGetValue(rel, out var lf);
            var hasR = remote.TryGetValue(rel, out var re);
            long lm = hasL ? lf!.Mtime : 0, rm = hasR ? RMtime(re!) : 0;

            if (!hasL && !hasR) continue;

            if (!hasB) // nouveaux (jamais synchronisés)
            {
                if (hasL && !hasR) acts.Add(new Act(Kind.Upload, rel));
                else if (hasR && !hasL) acts.Add(new Act(Kind.Download, rel));
                else if (Differs(lf!.Size, lm, re!.Size, rm)) acts.Add(new Act(lm >= rm ? Kind.Upload : Kind.Download, rel));
                continue;
            }

            var lDel = !hasL; var rDel = !hasR;
            var lMod = hasL && Differs(lf!.Size, lm, b!.Size, b.MtimeUnix);
            var rMod = hasR && Differs(re!.Size, rm, b!.Size, b.MtimeUnix);

            if (lDel && rDel) continue;                                  // supprimé des deux côtés
            else if (lDel && !rMod) acts.Add(new Act(Kind.DeleteRemote, rel)); // supprimé local -> supprime distant
            else if (rDel && !lMod) acts.Add(new Act(Kind.DeleteLocal, rel));  // supprimé distant -> supprime local
            else if (lDel && rMod) acts.Add(new Act(Kind.Download, rel));      // supprimé local mais modifié distant -> on garde
            else if (rDel && lMod) acts.Add(new Act(Kind.Upload, rel));        // symétrique
            else if (lMod && !rMod) acts.Add(new Act(Kind.Upload, rel));
            else if (rMod && !lMod) acts.Add(new Act(Kind.Download, rel));
            else if (lMod && rMod && Differs(lf!.Size, lm, re!.Size, rm)) acts.Add(new Act(lm >= rm ? Kind.Upload : Kind.Download, rel));
            // sinon : inchangé des deux côtés -> rien
        }
        return acts;
    }

    // =====================================================================
    //  Scan / utilitaires
    // =====================================================================

    private static Dictionary<string, LocalFile> ScanLocal(string localRoot)
    {
        var map = new Dictionary<string, LocalFile>(StringComparer.Ordinal);
        if (!Directory.Exists(localRoot)) return map;
        foreach (var f in Directory.EnumerateFiles(localRoot, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(f).StartsWith('.')) continue; // ignore les fichiers cachés / .part
            var rel = Path.GetRelativePath(localRoot, f).Replace('\\', '/');
            var info = new FileInfo(f);
            map[rel] = new LocalFile(f, info.Length, ToUnix(info.LastWriteTimeUtc));
        }
        return map;
    }

    private async Task<Dictionary<string, RemoteEntry>> ScanRemoteAsync(string remoteRoot, CancellationToken ct)
    {
        var map = new Dictionary<string, RemoteEntry>(StringComparer.Ordinal);
        await WalkRemoteAsync(remoteRoot, "", map, ct);
        return map;
    }

    private async Task WalkRemoteAsync(string dir, string relBase, Dictionary<string, RemoteEntry> map, CancellationToken ct)
    {
        IReadOnlyList<RemoteEntry> entries;
        try { entries = await _ftp.ListAsync(dir, ct); }
        catch (ApiException) { return; }
        foreach (var e in entries)
        {
            var rel = relBase.Length == 0 ? e.Name : relBase + "/" + e.Name;
            if (e.IsDirectory) await WalkRemoteAsync(e.FullPath, rel, map, ct);
            else map[rel] = e;
        }
    }

    private async Task EnsureRemoteDirAsync(string remoteRoot, string remoteDir, HashSet<string> known, CancellationToken ct)
    {
        remoteDir = FtpPathUtil.Normalize(remoteDir);
        if (known.Contains(remoteDir)) return;
        if (remoteDir != remoteRoot && FtpPathUtil.IsWithin(remoteDir, remoteRoot))
        {
            var parent = FtpPathUtil.GetParent(remoteDir);
            if (parent != remoteDir) await EnsureRemoteDirAsync(remoteRoot, parent, known, ct);
        }
        try { await _ftp.CreateDirectoryAsync(remoteDir, ct); }
        catch (ApiException) { }
        known.Add(remoteDir);
    }

    private static string RemoteFull(string remoteRoot, string rel) => FtpPathUtil.Combine(remoteRoot, rel);
    private static string LocalFull(string localRoot, string rel) => Path.Combine(localRoot, rel.Replace('/', Path.DirectorySeparatorChar));
    private static string Parent(string rel) { var i = rel.LastIndexOf('/'); return i < 0 ? "" : rel[..i]; }

    private static bool Differs(long s1, long m1, long s2, long m2) => s1 != s2 || Math.Abs(m1 - m2) > Tol;
    private static long RMtime(RemoteEntry e) => ToUnix(e.Modified.ToUniversalTime());
    private static long ToUnix(DateTime utc) => ((DateTimeOffset)DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
}

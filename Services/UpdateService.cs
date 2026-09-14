using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace DoodleDrive.Services;

/// <summary>Une mise à jour disponible (version plus récente que celle installée).</summary>
public sealed record UpdateAvailable(string VersionText, string? Notes, string? Sha256);

/// <summary>
/// Système de mise à jour : interroge le serveur (<c>/api/update</c>), compare à la version
/// installée, télécharge l'installeur (vérif SHA-256) puis le lance en silencieux.
/// </summary>
public sealed class UpdateService
{
    private readonly ApiClient _api;

    public UpdateService(ApiClient api) => _api = api;

    /// <summary>Version de l'assembly courant (issue de &lt;Version&gt; dans le .csproj).</summary>
    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    public static string CurrentVersionText
    {
        get { var v = CurrentVersion; return $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}"; }
    }

    /// <summary>Renvoie les infos de mise à jour si une version plus récente existe, sinon null.</summary>
    public async Task<UpdateAvailable?> CheckAsync(CancellationToken ct = default)
    {
        var info = await _api.GetUpdateInfoAsync(ct);
        if (info is null || string.IsNullOrWhiteSpace(info.Version)) return null;
        if (!Version.TryParse(Pad(info.Version), out var remote)) return null;
        return IsNewer(remote, CurrentVersion)
            ? new UpdateAvailable(info.Version.Trim(), info.Notes, info.Sha256)
            : null;
    }

    /// <summary>Télécharge l'installeur dans le dossier temporaire (et vérifie son empreinte si fournie).</summary>
    public async Task<string> DownloadAsync(UpdateAvailable upd, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "DoodleDrive", "update");
        Directory.CreateDirectory(dir);
        var setup = Path.Combine(dir, "DoodleDrive-Setup.exe");

        await _api.DownloadAppInstallerAsync(setup, progress, ct);

        if (!string.IsNullOrWhiteSpace(upd.Sha256))
        {
            var actual = await ComputeSha256Async(setup, ct);
            if (!string.Equals(actual, upd.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(setup); } catch { /* nettoyage best-effort */ }
                throw new InvalidOperationException(
                    "L'intégrité de la mise à jour n'a pas pu être vérifiée (empreinte SHA-256 différente).");
            }
        }
        return setup;
    }

    /// <summary>Lance l'installeur en silencieux (il ferme puis relance l'app) et quitte l'application.</summary>
    public void InstallAndRestart(string setupPath)
    {
        Process.Start(new ProcessStartInfo(setupPath)
        {
            UseShellExecute = true,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART"
        });
        System.Windows.Application.Current.Shutdown();
    }

    // Compare uniquement Majeur.Mineur.Build (on ignore Revision, source de pièges).
    private static bool IsNewer(Version remote, Version current) =>
        (remote.Major, remote.Minor, Math.Max(remote.Build, 0))
            .CompareTo((current.Major, current.Minor, Math.Max(current.Build, 0))) > 0;

    // Complète en au moins 3 segments pour que Version.TryParse accepte "2.0" ou "2".
    private static string Pad(string v)
    {
        v = v.Trim();
        return v.Split('.').Length switch { 1 => v + ".0.0", 2 => v + ".0", _ => v };
    }

    private static async Task<string> ComputeSha256Async(string file, CancellationToken ct)
    {
        await using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(fs, ct);
        return Convert.ToHexString(hash);
    }
}

using System.Globalization;
using DoodleDrive.Models;

namespace DoodleDrive.Services;

/// <summary>
/// Accès aux fichiers du drive. Depuis la migration API, cette classe est un adaptateur
/// mince au-dessus de <see cref="ApiClient"/> (mêmes signatures qu'avant pour ne rien
/// changer côté ViewModels) — plus aucune connexion FTP directe.
/// </summary>
public sealed class FtpService
{
    private readonly ApiClient _api;

    public FtpService(ApiClient api) => _api = api;

    /// <summary>Vérifie que le serveur/API répond et que la session est valide.</summary>
    public async Task TestConnectionAsync(CancellationToken ct = default)
    {
        await _api.MeAsync(ct);
    }

    public async Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken ct = default)
    {
        path = FtpPathUtil.Normalize(path);
        var listing = await _api.ListAsync(path, ct);

        var result = new List<RemoteEntry>(listing.Entries.Count);
        foreach (var e in listing.Entries)
        {
            result.Add(new RemoteEntry
            {
                Name = e.Name,
                FullPath = FtpPathUtil.Combine(path, e.Name),
                IsDirectory = e.IsDir,
                Size = e.Size,
                Modified = ParseModified(e.Modified)
            });
        }
        return result;
    }

    public async Task<bool> DirectoryExistsAsync(string path, CancellationToken ct = default)
    {
        try
        {
            await _api.ListAsync(FtpPathUtil.Normalize(path), ct);
            return true;
        }
        catch (ApiException)
        {
            return false;
        }
    }

    public Task CreateDirectoryAsync(string path, CancellationToken ct = default)
    {
        path = FtpPathUtil.Normalize(path);
        return _api.MkdirAsync(FtpPathUtil.GetParent(path), FtpPathUtil.GetName(path), ct);
    }

    public Task DeleteFileAsync(string path, CancellationToken ct = default) =>
        _api.DeleteAsync(FtpPathUtil.Normalize(path), ct);

    public Task DeleteDirectoryAsync(string path, CancellationToken ct = default) =>
        _api.DeleteAsync(FtpPathUtil.Normalize(path), ct);

    public Task RenameAsync(string fromPath, string toPath, CancellationToken ct = default) =>
        _api.RenameAsync(FtpPathUtil.Normalize(fromPath), FtpPathUtil.GetName(toPath), ct);

    public async Task<bool> UploadAsync(
        string localPath, string remotePath,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        remotePath = FtpPathUtil.Normalize(remotePath);
        await _api.UploadFileAsync(FtpPathUtil.GetParent(remotePath), FtpPathUtil.GetName(remotePath), localPath, progress, ct);
        return true;
    }

    public async Task<bool> DownloadAsync(
        string remotePath, string localPath,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        await _api.DownloadToFileAsync(FtpPathUtil.Normalize(remotePath), localPath, progress, ct);
        return true;
    }

    private static DateTime ParseModified(string? iso)
    {
        if (string.IsNullOrEmpty(iso)) return default;
        return DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            ? dt.ToLocalTime()
            : default;
    }
}

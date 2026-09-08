using FluentFTP;
using DoodleDrive.Models;

namespace DoodleDrive.Services;

/// <summary>
/// Accès au disque dur externe exposé en FTP sur la Freebox, via FluentFTP.
/// Un client est créé par opération logique (l'app est mono-utilisateur côté poste,
/// on privilégie la robustesse à la réutilisation de connexion).
/// IMPORTANT : chaque opération est exécutée sur le pool de threads (<see cref="Task.Run(Action)"/>).
/// FluentFTP attend certaines réponses réseau par boucles synchrones (Thread.Sleep) : lancées
/// depuis le thread UI, elles le gèlent (voire livelock complet, fenêtre « ne répond pas »).
/// </summary>
public sealed class FtpService
{
    private readonly AppConfigService _configService;

    public FtpService(AppConfigService configService) => _configService = configService;

    private async Task<AsyncFtpClient> ConnectAsync(CancellationToken ct)
    {
        var c = _configService.Current;
        var client = new AsyncFtpClient(c.FtpHost, c.FtpUser, c.FtpPassword, c.FtpPort);
        // IPv4 uniquement : la Freebox publie A + AAAA mais ses redirections de ports
        // sont IPv4-only (pare-feu IPv6 fermé) -> en IPv6 la connexion time out.
        client.Config.InternetProtocolVersions = FtpIpVersion.IPv4;
        // UTF-8 forcé : certains serveurs envoient de l'UTF-8 sans l'annoncer dans FEAT,
        // et les accents deviennent « ?? » si on laisse le repli ASCII.
        client.Encoding = System.Text.Encoding.UTF8;
        client.Config.ConnectTimeout = 15000;
        client.Config.ReadTimeout = 30000;
        client.Config.DataConnectionConnectTimeout = 15000;
        client.Config.DataConnectionReadTimeout = 30000;
        client.Config.RetryAttempts = 3;
        client.Config.DataConnectionType = FtpDataConnectionType.AutoPassive;
        client.Config.EncryptionMode = c.FtpUseTls ? FtpEncryptionMode.Explicit : FtpEncryptionMode.None;
        client.Config.ValidateAnyCertificate = true; // usage perso : on ne bloque pas sur un certif auto-signé
        await client.Connect(ct);
        return client;
    }

    public Task TestConnectionAsync(CancellationToken ct = default) => Task.Run(async () =>
    {
        await using var client = await ConnectAsync(ct);
    }, ct);

    /// <summary>Liste le contenu (dossiers + fichiers) d'un chemin FTP.</summary>
    public Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken ct = default) => Task.Run<IReadOnlyList<RemoteEntry>>(async () =>
    {
        path = FtpPathUtil.Normalize(path);
        await using var client = await ConnectAsync(ct);
        var items = await client.GetListing(path, ct);

        var result = new List<RemoteEntry>();
        foreach (var item in items)
        {
            if (item.Type is not (FtpObjectType.File or FtpObjectType.Directory))
                continue;
            if (item.Name is "." or "..")
                continue;

            result.Add(new RemoteEntry
            {
                Name = item.Name,
                FullPath = FtpPathUtil.Normalize(item.FullName),
                IsDirectory = item.Type == FtpObjectType.Directory,
                Size = item.Size < 0 ? 0 : item.Size,
                Modified = item.Modified
            });
        }
        return result;
    }, ct);

    public Task<bool> DirectoryExistsAsync(string path, CancellationToken ct = default) => Task.Run(async () =>
    {
        await using var client = await ConnectAsync(ct);
        return await client.DirectoryExists(FtpPathUtil.Normalize(path), ct);
    }, ct);

    public Task CreateDirectoryAsync(string path, CancellationToken ct = default) => Task.Run(async () =>
    {
        await using var client = await ConnectAsync(ct);
        await client.CreateDirectory(FtpPathUtil.Normalize(path), ct);
    }, ct);

    public Task DeleteFileAsync(string path, CancellationToken ct = default) => Task.Run(async () =>
    {
        await using var client = await ConnectAsync(ct);
        await client.DeleteFile(FtpPathUtil.Normalize(path), ct);
    }, ct);

    public Task DeleteDirectoryAsync(string path, CancellationToken ct = default) => Task.Run(async () =>
    {
        await using var client = await ConnectAsync(ct);
        await client.DeleteDirectory(FtpPathUtil.Normalize(path), ct);
    }, ct);

    public Task RenameAsync(string fromPath, string toPath, CancellationToken ct = default) => Task.Run(async () =>
    {
        await using var client = await ConnectAsync(ct);
        await client.Rename(FtpPathUtil.Normalize(fromPath), FtpPathUtil.Normalize(toPath), ct);
    }, ct);

    /// <summary>Upload d'un fichier local vers le FTP, avec reprise si le fichier existe partiellement.</summary>
    public Task<bool> UploadAsync(
        string localPath, string remotePath,
        IProgress<double>? progress = null, CancellationToken ct = default) => Task.Run(async () =>
    {
        await using var client = await ConnectAsync(ct);
        var ftpProgress = progress is null ? null : new Progress<FtpProgress>(p =>
        {
            if (p.Progress >= 0) progress.Report(p.Progress);
        });

        var status = await client.UploadFile(
            localPath, FtpPathUtil.Normalize(remotePath),
            FtpRemoteExists.Resume, createRemoteDir: true, FtpVerify.None, ftpProgress, ct);
        return status == FtpStatus.Success;
    }, ct);

    /// <summary>Download d'un fichier FTP vers un chemin local, avec reprise.</summary>
    public Task<bool> DownloadAsync(
        string remotePath, string localPath,
        IProgress<double>? progress = null, CancellationToken ct = default) => Task.Run(async () =>
    {
        await using var client = await ConnectAsync(ct);
        var ftpProgress = progress is null ? null : new Progress<FtpProgress>(p =>
        {
            if (p.Progress >= 0) progress.Report(p.Progress);
        });

        var status = await client.DownloadFile(
            localPath, FtpPathUtil.Normalize(remotePath),
            FtpLocalExists.Resume, FtpVerify.None, ftpProgress, ct);
        return status == FtpStatus.Success;
    }, ct);
}

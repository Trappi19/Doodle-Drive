using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace DoodleDrive.Services;

/// <summary>Erreur renvoyée par l'API (message + code HTTP).</summary>
public sealed class ApiException(string message, HttpStatusCode status) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
    public bool IsUnauthorized => Status == HttpStatusCode.Unauthorized;
}

// ---- DTO de réponse (camelCase, désérialisés depuis l'API) ----
public sealed record ApiUser(int Id, string Username, string Role, string? DefaultPath, string? LastPath)
{
    public bool IsAdmin => string.Equals(Role, "admin", StringComparison.OrdinalIgnoreCase);
}
public sealed record ApiEntry(string Name, bool IsDir, long Size, string? Modified);
public sealed record ApiListing(string Path, string Access, bool CanWrite, List<ApiEntry> Entries);
public sealed record ApiAccessibleFolder(int Id, string Name, string FtpPath, int? ParentId, string Access);
public sealed record ApiAccessible(bool IsAdmin, List<ApiAccessibleFolder> Folders);
public sealed record ApiAdminUser(int Id, string Username, string Role, DateTime CreatedAt, string? DefaultPath);
public sealed record ApiFolder(int Id, string Name, string FtpPath, int? ParentId, int OwnerId, string OwnerName);
public sealed record ApiPermission(int UserId, string Username, string Permission);
public sealed record ApiPermissions(bool Registered, int? FolderId, List<ApiPermission> Permissions);
public sealed record ApiShare(string Token, string FtpPath, string FileName, string Mode, bool IsDir,
    DateTime CreatedAt, DateTime? ExpiresAt, bool Revoked, int ViewCount);
public sealed record ApiShareCreated(string Token, string Path);
public sealed record ApiUpdateInfo(string Version, string? Notes, string? Sha256);
public sealed record ApiIdResult(int Id);
public sealed record ApiSyncFolder(int Id, string MachineId, string MachineName, string LocalPath,
    string RemotePath, bool AutoSync, DateTime CreatedAt, DateTime? LastSyncAt);

/// <summary>
/// Client HTTP unique de l'API Doodle Drive. Remplace les accès directs MariaDB/FTP :
/// login -> jeton conservé et envoyé en Bearer sur tous les appels.
/// </summary>
public sealed class ApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = new(new SocketsHttpHandler
    {
        // Funnel / Wi-Fi du serveur coupent parfois les connexions inactives sans prévenir :
        // on ne réutilise pas une connexion restée longtemps au repos (sinon la requête suivante
        // « pend » sur un socket mort), et on abandonne vite une connexion qui ne s'établit pas.
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(10)
    }) { Timeout = TimeSpan.FromMinutes(30) };

    /// <summary>Délai max d'une tentative de lecture (GET JSON) avant de réessayer.</summary>
    private static readonly TimeSpan GetAttemptTimeout = TimeSpan.FromSeconds(15);
    private const int GetMaxAttempts = 3;
    private readonly Func<string> _baseUrl;

    public ApiClient(Func<string> baseUrlProvider)
    {
        _baseUrl = baseUrlProvider;
        // Ne jamais envoyer « Expect: 100-continue » : Tailscale Funnel le gère mal
        // et casse la connexion sur les envois de fichiers.
        _http.DefaultRequestHeaders.ExpectContinue = false;
    }

    /// <summary>Jeton d'authentification courant (envoyé en Bearer). Défini au login.</summary>
    public string? Token { get; set; }

    public bool HasToken => !string.IsNullOrEmpty(Token);

    private string Url(string path) => $"{_baseUrl().TrimEnd('/')}{path}";

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, Url(path));
        if (HasToken) req.Headers.Add("Authorization", $"Bearer {Token}");
        return req;
    }

    private static async Task EnsureOkAsync(HttpResponseMessage res)
    {
        if (res.IsSuccessStatusCode) return;
        string? message = null;
        try
        {
            var doc = await res.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
            if (doc is not null && doc.TryGetValue("error", out var e)) message = e.GetString();
        }
        catch { /* corps non-JSON */ }
        throw new ApiException(message ?? $"Erreur serveur ({(int)res.StatusCode}).", res.StatusCode);
    }

    /// <summary>
    /// Appel JSON. Les lectures (GET, idempotentes) ont un délai court par tentative et sont
    /// réessayées : une requête bloquée sur une connexion morte repart au lieu d'attendre indéfiniment.
    /// </summary>
    private async Task<T> SendJsonAsync<T>(HttpMethod method, string path, object? body = null, CancellationToken ct = default)
    {
        if (method != HttpMethod.Get) return await SendJsonOnceAsync<T>(method, path, body, ct);

        for (var attempt = 1; ; attempt++)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(GetAttemptTimeout);
            try
            {
                return await SendJsonOnceAsync<T>(method, path, body, cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                if (attempt >= GetMaxAttempts)
                    throw new ApiException("Le serveur ne répond pas (délai dépassé). Réessayez.", HttpStatusCode.RequestTimeout);
            }
            catch (HttpRequestException) when (attempt < GetMaxAttempts) { }
            await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), ct);
        }
    }

    private async Task<T> SendJsonOnceAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var req = Request(method, path);
        if (body is not null) req.Content = JsonContent.Create(body, options: Json);
        using var res = await _http.SendAsync(req, ct);
        await EnsureOkAsync(res);
        return (await res.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    private async Task SendAsync(HttpMethod method, string path, object? body = null, CancellationToken ct = default)
    {
        using var req = Request(method, path);
        if (body is not null) req.Content = JsonContent.Create(body, options: Json);
        using var res = await _http.SendAsync(req, ct);
        await EnsureOkAsync(res);
    }

    private static string Enc(string s) => Uri.EscapeDataString(s);

    // ---------- Auth ----------
    public sealed record LoginResponse(string Token, ApiUser User);

    /// <summary>Connecte l'utilisateur, conserve le jeton et renvoie l'utilisateur.</summary>
    public async Task<ApiUser> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var resp = await SendJsonAsync<LoginResponse>(HttpMethod.Post, "/api/login",
            new { username, password }, ct);
        Token = resp.Token;
        return resp.User;
    }

    public Task<ApiUser> MeAsync(CancellationToken ct = default) =>
        SendJsonAsync<ApiUser>(HttpMethod.Get, "/api/me", ct: ct);

    public void SignOut() => Token = null;

    // ---------- Navigation / fichiers ----------
    public Task<ApiListing> ListAsync(string path, CancellationToken ct = default) =>
        SendJsonAsync<ApiListing>(HttpMethod.Get, $"/api/list?path={Enc(path)}", ct: ct);

    public async Task DownloadToFileAsync(string path, string localFile, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        using var req = Request(HttpMethod.Get, $"/api/download?path={Enc(path)}");
        using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureOkAsync(res);
        var total = res.Content.Headers.ContentLength ?? -1;
        await using var src = await res.Content.ReadAsStreamAsync(ct);
        await using var dst = new FileStream(localFile, FileMode.Create, FileAccess.Write, FileShare.None);
        await CopyWithProgressAsync(src, dst, total, progress, ct);
    }

    public async Task UploadFileAsync(string dirPath, string name, string localFile,
        IProgress<double>? progress = null, long? mtimeUnix = null, CancellationToken ct = default)
    {
        await using var src = new FileStream(localFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        var url = $"/api/upload?path={Enc(dirPath)}&name={Enc(name)}" + (mtimeUnix is { } m ? $"&mtime={m}" : "");
        using var req = Request(HttpMethod.Post, url);
        req.Content = new ProgressStreamContent(src, progress);
        using var res = await _http.SendAsync(req, ct);
        await EnsureOkAsync(res);
    }

    // ---------- Envoi par morceaux (résiste aux coupures Wi-Fi : réessai + reprise) ----------
    /// <summary>Taille d'une tranche (transfert court → survit aux blips réseau).</summary>
    public const int UploadChunkSize = 8 * 1024 * 1024;
    private const int MaxChunkRetries = 5;

    private sealed record ProbeResult(long Uploaded);

    /// <summary>Octets déjà reçus par le serveur pour cet envoi (lance une exception en cas d'échec).</summary>
    public async Task<long> ProbeUploadAsync(string dirPath, string uploadId, CancellationToken ct = default)
    {
        var r = await SendJsonAsync<ProbeResult>(HttpMethod.Get,
            $"/api/upload/probe?path={Enc(dirPath)}&id={uploadId}", ct: ct);
        return r.Uploaded;
    }

    public Task AbortUploadAsync(string dirPath, string uploadId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"/api/upload/abort?path={Enc(dirPath)}&id={uploadId}", ct: ct);

    /// <summary>
    /// Envoie un fichier en tranches. En cas de coupure, réessaie chaque tranche (jusqu'à
    /// <see cref="MaxChunkRetries"/>) en resynchronisant l'offset auprès du serveur, et
    /// reprend là où il en était (même <paramref name="uploadId"/>).
    /// </summary>
    public async Task UploadFileChunkedAsync(string dirPath, string name, string localFile, string uploadId,
        IProgress<double>? progress = null, long? mtimeUnix = null, CancellationToken ct = default)
    {
        await using var src = new FileStream(localFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        var total = src.Length;

        long uploaded;
        try { uploaded = Math.Min(await ProbeUploadAsync(dirPath, uploadId, ct), total); }
        catch { uploaded = 0; }
        if (total > 0) progress?.Report(uploaded * 100.0 / total);

        var buffer = new byte[UploadChunkSize];
        var attempt = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                src.Seek(uploaded, SeekOrigin.Begin);
                var read = await FillAsync(src, buffer, ct);
                var last = uploaded + read >= total;

                var url = $"/api/upload/chunk?path={Enc(dirPath)}&name={Enc(name)}&id={uploadId}&last={(last ? "true" : "false")}"
                          + (last && mtimeUnix is { } m ? $"&mtime={m}" : "");
                using var req = Request(HttpMethod.Post, url);

                // Progression fluide : on remonte l'avancement PENDANT l'envoi de la tranche.
                var before = uploaded;
                using var chunkStream = new MemoryStream(buffer, 0, read, writable: false);
                IProgress<double>? chunkProgress = total > 0
                    ? new Progress<double>(pct => progress?.Report(Math.Min(before + pct / 100.0 * read, total) * 100.0 / total))
                    : null;
                req.Content = new ProgressStreamContent(chunkStream, chunkProgress);

                using var res = await _http.SendAsync(req, ct);
                await EnsureOkAsync(res);

                uploaded += read;
                if (total > 0) progress?.Report(Math.Min(uploaded, total) * 100.0 / total);
                attempt = 0;              // tranche réussie : on repart à zéro pour la suivante
                if (last) break;
            }
            catch (OperationCanceledException) { throw; }
            catch when (attempt < MaxChunkRetries)
            {
                attempt++;
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(2 * attempt, 8)), ct);
                // Resynchronise l'offset : le serveur a pu écrire une partie de la tranche.
                try { uploaded = Math.Min(await ProbeUploadAsync(dirPath, uploadId, ct), total); }
                catch { /* échec du probe : on garde l'offset courant et on réessaie */ }
            }
        }
    }

    private static async Task<int> FillAsync(Stream s, byte[] buf, CancellationToken ct)
    {
        var total = 0;
        while (total < buf.Length)
        {
            var r = await s.ReadAsync(buf.AsMemory(total, buf.Length - total), ct);
            if (r == 0) break;
            total += r;
        }
        return total;
    }

    public Task MkdirAsync(string path, string name, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "/api/mkdir", new { path, name }, ct);

    public Task RenameAsync(string path, string newName, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "/api/rename", new { path, newName }, ct);

    /// <summary>Déplace un fichier/dossier (<paramref name="from"/>) dans le dossier <paramref name="toDir"/>.</summary>
    public Task MoveAsync(string from, string toDir, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "/api/move", new { from, toDir }, ct);

    /// <summary>Copie un fichier/dossier (<paramref name="from"/>) dans le dossier <paramref name="toDir"/>.</summary>
    public Task CopyAsync(string from, string toDir, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "/api/copy", new { from, toDir }, ct);

    // ---------- Dossiers de synchronisation ----------
    public Task<List<ApiSyncFolder>> GetSyncFoldersAsync(CancellationToken ct = default) =>
        SendJsonAsync<List<ApiSyncFolder>>(HttpMethod.Get, "/api/sync/folders", ct: ct);

    public async Task<int> CreateSyncFolderAsync(string machineId, string machineName, string localPath,
        string remotePath, bool autoSync, CancellationToken ct = default) =>
        (await SendJsonAsync<ApiIdResult>(HttpMethod.Post, "/api/sync/folders",
            new { machineId, machineName, localPath, remotePath, autoSync }, ct)).Id;

    public Task SetSyncAutoAsync(int id, bool autoSync, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "/api/sync/folders/auto", new { id, autoSync }, ct);

    public Task TouchSyncAsync(int id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "/api/sync/folders/touch", new { id }, ct);

    public Task DeleteSyncFolderAsync(int id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "/api/sync/folders/delete", new { id }, ct);

    // ---------- Mise à jour de l'app ----------
    /// <summary>Infos de la dernière version publiée, ou null si le serveur n'en publie aucune (204).</summary>
    public async Task<ApiUpdateInfo?> GetUpdateInfoAsync(CancellationToken ct = default)
    {
        using var req = Request(HttpMethod.Get, "/api/update");
        using var res = await _http.SendAsync(req, ct);
        if (res.StatusCode == HttpStatusCode.NoContent) return null;
        await EnsureOkAsync(res);
        return await res.Content.ReadFromJsonAsync<ApiUpdateInfo>(Json, ct);
    }

    /// <summary>Télécharge l'installeur Windows le plus récent vers un fichier local.</summary>
    public async Task DownloadAppInstallerAsync(string localFile, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        using var req = Request(HttpMethod.Get, "/download/app");
        using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureOkAsync(res);
        var total = res.Content.Headers.ContentLength ?? -1;
        await using var src = await res.Content.ReadAsStreamAsync(ct);
        await using var dst = new FileStream(localFile, FileMode.Create, FileAccess.Write, FileShare.None);
        await CopyWithProgressAsync(src, dst, total, progress, ct);
    }

    public Task DeleteAsync(string path, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "/api/delete", new { path }, ct);

    public Task SetLastPathAsync(string path, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "/api/lastpath", new { path }, ct);

    public Task<ApiAccessible> GetAccessibleAsync(CancellationToken ct = default) =>
        SendJsonAsync<ApiAccessible>(HttpMethod.Get, "/api/accessible", ct: ct);

    // ---------- Admin ----------
    public Task<List<ApiAdminUser>> GetUsersAsync(CancellationToken ct = default) =>
        SendJsonAsync<List<ApiAdminUser>>(HttpMethod.Get, "/api/admin/users", ct: ct);

    public sealed record CreatedUser(int Id, string Username, string Role);
    public Task<CreatedUser> CreateUserAsync(string username, string password, string role, CancellationToken ct = default) =>
        SendJsonAsync<CreatedUser>(HttpMethod.Post, "/api/admin/users", new { username, password, role }, ct);

    public Task SetUserRoleAsync(int userId, string role, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "/api/admin/users/role", new { userId, role }, ct);

    public Task SetUserPasswordAsync(int userId, string password, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "/api/admin/users/password", new { userId, password }, ct);

    public Task SetUserDefaultPathAsync(int userId, string? path, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "/api/admin/users/default-path", new { userId, path }, ct);

    public Task DeleteUserAsync(int userId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "/api/admin/users/delete", new { userId }, ct);

    public Task<List<ApiFolder>> GetAllFoldersAsync(CancellationToken ct = default) =>
        SendJsonAsync<List<ApiFolder>>(HttpMethod.Get, "/api/admin/folders", ct: ct);

    // ---------- Permissions ----------
    public Task<ApiPermissions> GetPermissionsAsync(string path, CancellationToken ct = default) =>
        SendJsonAsync<ApiPermissions>(HttpMethod.Get, $"/api/permissions?path={Enc(path)}", ct: ct);

    public Task SetPermissionAsync(string path, int userId, string permission, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "/api/permissions", new { path, userId, permission }, ct);

    public Task RemovePermissionAsync(string path, int userId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "/api/permissions/remove", new { path, userId }, ct);

    // ---------- Partages ----------
    public Task<ApiShareCreated> CreateShareAsync(string path, string mode, bool isDir, int? expiresInDays, CancellationToken ct = default) =>
        SendJsonAsync<ApiShareCreated>(HttpMethod.Post, "/api/shares", new { path, mode, isDir, expiresInDays }, ct);

    public Task<List<ApiShare>> GetSharesAsync(CancellationToken ct = default) =>
        SendJsonAsync<List<ApiShare>>(HttpMethod.Get, "/api/shares", ct: ct);

    public Task RevokeShareAsync(string token, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "/api/shares/revoke", new { token }, ct);

    // ---------- utilitaires de streaming ----------
    private static async Task CopyWithProgressAsync(Stream src, Stream dst, long total, IProgress<double>? progress, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            if (total > 0) progress?.Report(done * 100.0 / total);
        }
        if (total <= 0) progress?.Report(100);
    }
}

/// <summary>Contenu HTTP qui streame un flux en signalant la progression d'envoi.</summary>
internal sealed class ProgressStreamContent(Stream source, IProgress<double>? progress) : HttpContent
{
    protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
    {
        var total = source.CanSeek ? source.Length : -1;
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await stream.WriteAsync(buffer.AsMemory(0, read));
            done += read;
            if (total > 0) progress?.Report(done * 100.0 / total);
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        if (source.CanSeek) { length = source.Length; return true; }
        length = 0; return false;
    }
}

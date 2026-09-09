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

/// <summary>
/// Client HTTP unique de l'API Doodle Drive. Remplace les accès directs MariaDB/FTP :
/// login -> jeton conservé et envoyé en Bearer sur tous les appels.
/// </summary>
public sealed class ApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private readonly Func<string> _baseUrl;

    public ApiClient(Func<string> baseUrlProvider) => _baseUrl = baseUrlProvider;

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

    private async Task<T> SendJsonAsync<T>(HttpMethod method, string path, object? body = null, CancellationToken ct = default)
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

    public async Task UploadFileAsync(string dirPath, string name, string localFile, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        await using var src = new FileStream(localFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var req = Request(HttpMethod.Post, $"/api/upload?path={Enc(dirPath)}&name={Enc(name)}");
        req.Content = new ProgressStreamContent(src, progress);
        using var res = await _http.SendAsync(req, ct);
        await EnsureOkAsync(res);
    }

    public Task MkdirAsync(string path, string name, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "/api/mkdir", new { path, name }, ct);

    public Task RenameAsync(string path, string newName, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "/api/rename", new { path, newName }, ct);

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

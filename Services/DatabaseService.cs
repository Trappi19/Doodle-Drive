using DoodleDrive.Models;

namespace DoodleDrive.Services;

/// <summary>
/// Accès aux données (comptes, dossiers, permissions, partages). Depuis la migration API,
/// c'est un adaptateur au-dessus de <see cref="ApiClient"/> — plus aucun accès MariaDB direct.
/// Les signatures « historiques » sont conservées au maximum pour limiter les changements de VM ;
/// les permissions passent désormais par le CHEMIN (l'API est orientée chemin, pas folderId).
/// </summary>
public sealed class DatabaseService
{
    private readonly ApiClient _api;

    public DatabaseService(ApiClient api) => _api = api;

    public Task TestConnectionAsync(CancellationToken ct = default) => _api.MeAsync(ct);

    // ---------- Utilisateurs ----------
    public async Task<IReadOnlyList<User>> GetAllUsersAsync(CancellationToken ct = default) =>
        (await _api.GetUsersAsync(ct)).Select(ToUser).ToList();

    public async Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default) =>
        (await _api.GetUsersAsync(ct)).Any(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

    /// <summary>Crée un compte. Le mot de passe est transmis en clair : c'est l'API qui le hash.</summary>
    public async Task<int> CreateUserAsync(string username, string password, UserRole role, CancellationToken ct = default) =>
        (await _api.CreateUserAsync(username, password, role.ToDbValue(), ct)).Id;

    public Task UpdateUserRoleAsync(int userId, UserRole role, CancellationToken ct = default) =>
        _api.SetUserRoleAsync(userId, role.ToDbValue(), ct);

    /// <summary>Change le mot de passe (transmis en clair, hashé par l'API).</summary>
    public Task UpdateUserPasswordAsync(int userId, string password, CancellationToken ct = default) =>
        _api.SetUserPasswordAsync(userId, password, ct);

    public Task UpdateUserDefaultPathAsync(int userId, string? path, CancellationToken ct = default) =>
        _api.SetUserDefaultPathAsync(userId, path, ct);

    public Task UpdateUserLastPathAsync(int userId, string? path, CancellationToken ct = default) =>
        _api.SetLastPathAsync(path ?? "/", ct);

    public Task DeleteUserAsync(int userId, CancellationToken ct = default) =>
        _api.DeleteUserAsync(userId, ct);

    public async Task<int> GetAdminCountAsync(CancellationToken ct = default) =>
        (await _api.GetUsersAsync(ct)).Count(u => string.Equals(u.Role, "admin", StringComparison.OrdinalIgnoreCase));

    // ---------- Dossiers ----------
    public async Task<IReadOnlyList<Folder>> GetAllFoldersAsync(CancellationToken ct = default) =>
        (await _api.GetAllFoldersAsync(ct)).Select(ToFolder).ToList();

    public async Task<Folder?> GetFolderByPathAsync(string ftpPath, CancellationToken ct = default)
    {
        try
        {
            var target = FtpPathUtil.Normalize(ftpPath);
            var f = (await _api.GetAllFoldersAsync(ct)).FirstOrDefault(x => FtpPathUtil.Normalize(x.FtpPath) == target);
            return f is null ? null : ToFolder(f);
        }
        catch (ApiException)
        {
            return null; // non-admin (403) : ne gère pas les dossiers enregistrés
        }
    }

    public async Task<IReadOnlyList<(Folder Folder, FolderAccessLevel Access)>> GetAccessibleFoldersAsync(
        int userId, CancellationToken ct = default)
    {
        var acc = await _api.GetAccessibleAsync(ct);
        return acc.Folders.Select(f => (
            new Folder { Id = f.Id, Name = f.Name, FtpPath = f.FtpPath, ParentId = f.ParentId },
            ParseAccess(f.Access))).ToList();
    }

    // Ces opérations sur la table `folders` sont désormais gérées côté serveur (ou non nécessaires
    // en mode API) : l'enregistrement d'un dossier se fait à l'attribution d'une permission.
    public Task<int> CreateFolderAsync(string name, string ftpPath, int? parentId, int ownerId, CancellationToken ct = default)
        => Task.FromResult(0);
    public Task RenameFolderAsync(int folderId, string oldPath, string newPath, string newName, CancellationToken ct = default)
        => Task.CompletedTask;
    public Task DeleteFolderAsync(int folderId, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteFoldersByPathAsync(string ftpPath, CancellationToken ct = default) => Task.CompletedTask;

    // ---------- Permissions (orientées CHEMIN) ----------
    public async Task<IReadOnlyList<(User User, PermissionLevel Level)>> GetFolderPermissionsAsync(
        string ftpPath, CancellationToken ct = default)
    {
        var p = await _api.GetPermissionsAsync(ftpPath, ct);
        return p.Permissions.Select(x => (
            new User { Id = x.UserId, Username = x.Username },
            string.Equals(x.Permission, "write", StringComparison.OrdinalIgnoreCase) ? PermissionLevel.Write : PermissionLevel.Read
        )).ToList();
    }

    public Task SetPermissionAsync(string ftpPath, int userId, PermissionLevel level, int grantedBy, CancellationToken ct = default) =>
        _api.SetPermissionAsync(ftpPath, userId, level.ToDbValue(), ct);

    public Task RemovePermissionAsync(string ftpPath, int userId, CancellationToken ct = default) =>
        _api.RemovePermissionAsync(ftpPath, userId, ct);

    // ---------- Partages ----------
    public Task EnsureSharesTableAsync(CancellationToken ct = default) => Task.CompletedTask; // géré par le serveur

    /// <summary>Crée un partage via l'API (le serveur génère le jeton) et le renvoie.</summary>
    public async Task<string> CreateShareAsync(string ftpPath, string mode, bool isDir, int? expiresInDays, CancellationToken ct = default) =>
        (await _api.CreateShareAsync(ftpPath, mode, isDir, expiresInDays, ct)).Token;

    public async Task<IReadOnlyList<ShareLink>> GetSharesAsync(int? createdBy, CancellationToken ct = default) =>
        (await _api.GetSharesAsync(ct)).Select(s => new ShareLink
        {
            Token = s.Token, FtpPath = s.FtpPath, FileName = s.FileName, Mode = s.Mode, IsDir = s.IsDir,
            CreatedAt = s.CreatedAt, ExpiresAt = s.ExpiresAt, Revoked = s.Revoked, ViewCount = s.ViewCount
        }).ToList();

    public Task RevokeShareAsync(string token, CancellationToken ct = default) => _api.RevokeShareAsync(token, ct);

    // ---------- Mappings ----------
    private static User ToUser(ApiAdminUser u) => new()
    {
        Id = u.Id, Username = u.Username, CreatedAt = u.CreatedAt, DefaultPath = u.DefaultPath,
        Role = string.Equals(u.Role, "admin", StringComparison.OrdinalIgnoreCase) ? UserRole.Admin : UserRole.User
    };

    private static Folder ToFolder(ApiFolder f) => new()
    {
        Id = f.Id, Name = f.Name, FtpPath = f.FtpPath, ParentId = f.ParentId, OwnerId = f.OwnerId, OwnerName = f.OwnerName
    };

    private static FolderAccessLevel ParseAccess(string a) => a switch
    {
        "Owner" => FolderAccessLevel.Owner,
        "Write" => FolderAccessLevel.Write,
        "Read" => FolderAccessLevel.Read,
        "Traverse" => FolderAccessLevel.Traverse,
        _ => FolderAccessLevel.None
    };
}

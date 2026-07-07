using Dapper;
using DoodleDrive.Models;
using MySqlConnector;

namespace DoodleDrive.Services;

/// <summary>
/// Accès direct à la base MariaDB <c>cloud_perso</c> (comptes &amp; permissions) via MySqlConnector + Dapper.
/// Aucun backend intermédiaire : l'application interroge la base directement.
/// </summary>
public sealed class DatabaseService
{
    private readonly AppConfigService _configService;

    static DatabaseService()
    {
        // Mappe password_hash -> PasswordHash, ftp_path -> FtpPath, etc.
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public DatabaseService(AppConfigService configService) => _configService = configService;

    private string BuildConnectionString()
    {
        var c = _configService.Current;
        return new MySqlConnectionStringBuilder
        {
            Server = c.DbHost,
            Port = (uint)c.DbPort,
            Database = c.DbName,
            UserID = c.DbUser,
            Password = c.DbPassword,
            SslMode = MySqlSslMode.Preferred,
            ConnectionTimeout = 15,
            DefaultCommandTimeout = 30,
            AllowUserVariables = true
        }.ConnectionString;
    }

    private MySqlConnection CreateConnection() => new(BuildConnectionString());

    /// <summary>Teste la connexion (utilisé par l'écran de connexion et les paramètres).</summary>
    public async Task TestConnectionAsync(CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await conn.QueryFirstOrDefaultAsync<int>(new CommandDefinition("SELECT 1", cancellationToken: ct));
    }

    // =====================================================================
    //  Utilisateurs
    // =====================================================================

    public async Task<User?> GetUserByUsernameAsync(string username, CancellationToken ct = default)
    {
        const string sql = @"SELECT id, username, password_hash, role, created_at
                             FROM users WHERE username = @username LIMIT 1;";
        await using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<User>(new CommandDefinition(sql, new { username }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<User>> GetAllUsersAsync(CancellationToken ct = default)
    {
        const string sql = @"SELECT id, username, password_hash, role, created_at
                             FROM users ORDER BY username;";
        await using var conn = CreateConnection();
        var users = await conn.QueryAsync<User>(new CommandDefinition(sql, cancellationToken: ct));
        return users.ToList();
    }

    public async Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default)
    {
        const string sql = "SELECT COUNT(1) FROM users WHERE username = @username;";
        await using var conn = CreateConnection();
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, new { username }, cancellationToken: ct)) > 0;
    }

    public async Task<int> CreateUserAsync(string username, string passwordHash, UserRole role, CancellationToken ct = default)
    {
        const string sql = @"INSERT INTO users (username, password_hash, role)
                             VALUES (@username, @passwordHash, @role);
                             SELECT LAST_INSERT_ID();";
        await using var conn = CreateConnection();
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            sql, new { username, passwordHash, role = role.ToDbValue() }, cancellationToken: ct));
    }

    public async Task UpdateUserRoleAsync(int userId, UserRole role, CancellationToken ct = default)
    {
        const string sql = "UPDATE users SET role = @role WHERE id = @userId;";
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new { userId, role = role.ToDbValue() }, cancellationToken: ct));
    }

    public async Task UpdateUserPasswordAsync(int userId, string passwordHash, CancellationToken ct = default)
    {
        const string sql = "UPDATE users SET password_hash = @passwordHash WHERE id = @userId;";
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new { userId, passwordHash }, cancellationToken: ct));
    }

    public async Task DeleteUserAsync(int userId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM users WHERE id = @userId;";
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new { userId }, cancellationToken: ct));
    }

    public async Task<int> GetAdminCountAsync(CancellationToken ct = default)
    {
        const string sql = "SELECT COUNT(1) FROM users WHERE role = 'admin';";
        await using var conn = CreateConnection();
        return (int)await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, cancellationToken: ct));
    }

    // =====================================================================
    //  Dossiers
    // =====================================================================

    public async Task<IReadOnlyList<Folder>> GetAllFoldersAsync(CancellationToken ct = default)
    {
        const string sql = @"SELECT f.id, f.name, f.ftp_path, f.parent_id, f.owner_id, f.created_at, u.username AS owner_name
                             FROM folders f JOIN users u ON u.id = f.owner_id
                             ORDER BY f.ftp_path;";
        await using var conn = CreateConnection();
        var folders = await conn.QueryAsync<Folder>(new CommandDefinition(sql, cancellationToken: ct));
        return folders.ToList();
    }

    /// <summary>
    /// Renvoie tous les dossiers accessibles à l'utilisateur (propriétaire ou partagé),
    /// enfants inclus par héritage, avec le niveau d'accès effectif calculé.
    /// </summary>
    public async Task<IReadOnlyList<(Folder Folder, FolderAccessLevel Access)>> GetAccessibleFoldersAsync(
        int userId, CancellationToken ct = default)
    {
        // La table folders est de petite taille (usage perso) : on la charge en mémoire
        // et on résout l'héritage côté application, ce qui reste simple et lisible.
        const string foldersSql = @"SELECT id, name, ftp_path, parent_id, owner_id, created_at FROM folders;";
        const string permsSql = @"SELECT folder_id AS FolderId, permission AS Permission
                                  FROM folder_permissions WHERE user_id = @userId;";

        await using var conn = CreateConnection();
        var allFolders = (await conn.QueryAsync<Folder>(new CommandDefinition(foldersSql, cancellationToken: ct))).ToList();
        var perms = (await conn.QueryAsync<UserFolderPermDto>(
            new CommandDefinition(permsSql, new { userId }, cancellationToken: ct))).ToList();

        var byId = allFolders.ToDictionary(f => f.Id);
        var directPerms = perms
            .GroupBy(p => p.FolderId)
            .ToDictionary(g => g.Key, g => g.First().Permission.ToPermissionLevel());

        FolderAccessLevel Resolve(Folder folder)
        {
            var best = FolderAccessLevel.None;
            var current = (Folder?)folder;
            var guard = 0; // sécurité anti-boucle sur des parent_id incohérents
            while (current is not null && guard++ < 512)
            {
                if (current.OwnerId == userId)
                    return FolderAccessLevel.Owner; // la propriété d'un ancêtre donne le niveau maximal

                if (directPerms.TryGetValue(current.Id, out var level))
                {
                    var mapped = level == PermissionLevel.Write ? FolderAccessLevel.Write : FolderAccessLevel.Read;
                    if (mapped > best) best = mapped;
                }

                current = current.ParentId is int pid && byId.TryGetValue(pid, out var parent) ? parent : null;
            }
            return best;
        }

        var result = new List<(Folder, FolderAccessLevel)>();
        foreach (var folder in allFolders)
        {
            var access = Resolve(folder);
            if (access != FolderAccessLevel.None)
                result.Add((folder, access));
        }
        return result;
    }

    public async Task<Folder?> GetFolderByPathAsync(string ftpPath, CancellationToken ct = default)
    {
        const string sql = @"SELECT id, name, ftp_path, parent_id, owner_id, created_at
                             FROM folders WHERE ftp_path = @ftpPath LIMIT 1;";
        await using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Folder>(new CommandDefinition(sql, new { ftpPath }, cancellationToken: ct));
    }

    public async Task<int> CreateFolderAsync(string name, string ftpPath, int? parentId, int ownerId, CancellationToken ct = default)
    {
        const string sql = @"INSERT INTO folders (name, ftp_path, parent_id, owner_id)
                             VALUES (@name, @ftpPath, @parentId, @ownerId);
                             SELECT LAST_INSERT_ID();";
        await using var conn = CreateConnection();
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            sql, new { name, ftpPath, parentId, ownerId }, cancellationToken: ct));
    }

    /// <summary>Renomme un dossier et répercute le changement de chemin sur tous ses sous-dossiers.</summary>
    public async Task RenameFolderAsync(int folderId, string oldPath, string newPath, string newName, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE folders
            SET ftp_path = CONCAT(@newPath, SUBSTRING(ftp_path, @oldPathLen + 1))
            WHERE ftp_path = @oldPath OR ftp_path LIKE CONCAT(@oldPath, '/%');
            UPDATE folders SET name = @newName WHERE id = @folderId;";
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            sql, new { folderId, oldPath, newPath, newName, oldPathLen = oldPath.Length }, cancellationToken: ct));
    }

    /// <summary>Supprime l'enregistrement d'un dossier (CASCADE supprime sous-dossiers et permissions).</summary>
    public async Task DeleteFolderAsync(int folderId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM folders WHERE id = @folderId;";
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new { folderId }, cancellationToken: ct));
    }

    /// <summary>Supprime les enregistrements de dossiers dont le chemin correspond ou descend de <paramref name="ftpPath"/>.</summary>
    public async Task DeleteFoldersByPathAsync(string ftpPath, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM folders WHERE ftp_path = @ftpPath OR ftp_path LIKE CONCAT(@ftpPath, '/%');";
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new { ftpPath }, cancellationToken: ct));
    }

    // =====================================================================
    //  Permissions de partage
    // =====================================================================

    public async Task<IReadOnlyList<(User User, PermissionLevel Level)>> GetFolderPermissionsAsync(
        int folderId, CancellationToken ct = default)
    {
        const string sql = @"SELECT u.id AS Id, u.username AS Username, u.password_hash AS PasswordHash,
                                    u.role AS Role, u.created_at AS CreatedAt, fp.permission AS Permission
                             FROM folder_permissions fp
                             JOIN users u ON u.id = fp.user_id
                             WHERE fp.folder_id = @folderId
                             ORDER BY u.username;";
        await using var conn = CreateConnection();
        var rows = await conn.QueryAsync<PermissionRowDto>(
            new CommandDefinition(sql, new { folderId }, cancellationToken: ct));

        return rows.Select(r => (
            new User { Id = r.Id, Username = r.Username, PasswordHash = r.PasswordHash, Role = r.Role.ToUserRole(), CreatedAt = r.CreatedAt },
            r.Permission.ToPermissionLevel())).ToList();
    }

    private sealed class PermissionRowDto
    {
        public int Id { get; init; }
        public string Username { get; init; } = string.Empty;
        public string PasswordHash { get; init; } = string.Empty;
        public string Role { get; init; } = "user";
        public DateTime CreatedAt { get; init; }
        public string Permission { get; init; } = "read";
    }

    private sealed class UserFolderPermDto
    {
        public int FolderId { get; init; }
        public string Permission { get; init; } = "read";
    }

    /// <summary>Ajoute ou met à jour une permission (respecte la contrainte unique folder_id/user_id).</summary>
    public async Task SetPermissionAsync(int folderId, int userId, PermissionLevel level, int grantedBy, CancellationToken ct = default)
    {
        const string sql = @"INSERT INTO folder_permissions (folder_id, user_id, permission, granted_by)
                             VALUES (@folderId, @userId, @permission, @grantedBy)
                             ON DUPLICATE KEY UPDATE permission = @permission, granted_by = @grantedBy;";
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            sql, new { folderId, userId, permission = level.ToDbValue(), grantedBy }, cancellationToken: ct));
    }

    public async Task RemovePermissionAsync(int folderId, int userId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM folder_permissions WHERE folder_id = @folderId AND user_id = @userId;";
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new { folderId, userId }, cancellationToken: ct));
    }
}

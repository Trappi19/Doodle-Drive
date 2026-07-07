namespace DoodleDrive.Models;

/// <summary>Rôle d'un compte utilisateur (colonne <c>users.role</c>).</summary>
public enum UserRole
{
    User,
    Admin
}

/// <summary>Niveau de partage stocké dans <c>folder_permissions.permission</c>.</summary>
public enum PermissionLevel
{
    Read,
    Write
}

/// <summary>
/// Niveau d'accès effectif d'un utilisateur sur un dossier, une fois toutes les
/// règles appliquées (propriété, partage, héritage depuis un dossier parent).
/// <para>
/// <see cref="Traverse"/> = « couloir » : le dossier est un ancêtre d'un dossier
/// autorisé. L'utilisateur peut le traverser mais n'y voit que le sous-dossier
/// qui mène à un dossier auquel il a réellement accès (pas les autres contenus).
/// </para>
/// </summary>
public enum FolderAccessLevel
{
    None = 0,
    Traverse = 1,
    Read = 2,
    Write = 3,
    Owner = 4
}

/// <summary>Catégorie logique d'un fichier, déduite de son extension. Sert au choix de la vignette/glyphe.</summary>
public enum FileKind
{
    Folder,
    Image,
    Video,
    Audio,
    Pdf,
    Document,
    Spreadsheet,
    Presentation,
    Archive,
    Code,
    Text,
    Unknown
}

public static class EnumExtensions
{
    public static UserRole ToUserRole(this string? value) =>
        string.Equals(value, "admin", StringComparison.OrdinalIgnoreCase) ? UserRole.Admin : UserRole.User;

    public static string ToDbValue(this UserRole role) => role == UserRole.Admin ? "admin" : "user";

    public static PermissionLevel ToPermissionLevel(this string? value) =>
        string.Equals(value, "write", StringComparison.OrdinalIgnoreCase) ? PermissionLevel.Write : PermissionLevel.Read;

    public static string ToDbValue(this PermissionLevel level) => level == PermissionLevel.Write ? "write" : "read";

    public static bool CanWrite(this FolderAccessLevel level) => level >= FolderAccessLevel.Write;

    public static bool CanRead(this FolderAccessLevel level) => level >= FolderAccessLevel.Read;

    public static bool IsTraverseOnly(this FolderAccessLevel level) => level == FolderAccessLevel.Traverse;

    public static bool CanManageSharing(this FolderAccessLevel level) => level == FolderAccessLevel.Owner;
}

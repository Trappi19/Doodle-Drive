namespace DoodleDrive.Models;

/// <summary>Représente une ligne de la table <c>users</c>.</summary>
public sealed class User
{
    public int Id { get; init; }
    public string Username { get; init; } = string.Empty;

    /// <summary>Hash BCrypt. Jamais affiché dans l'UI.</summary>
    public string PasswordHash { get; init; } = string.Empty;

    public UserRole Role { get; init; }
    public DateTime CreatedAt { get; init; }

    public bool IsAdmin => Role == UserRole.Admin;
}

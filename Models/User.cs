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

    /// <summary>Chemin d'atterrissage attribué par un admin (null = comportement automatique).</summary>
    public string? DefaultPath { get; init; }

    /// <summary>Dernier chemin visité, mémorisé en base (partagé entre PC et mobile).</summary>
    public string? LastPath { get; init; }

    public bool IsAdmin => Role == UserRole.Admin;
}

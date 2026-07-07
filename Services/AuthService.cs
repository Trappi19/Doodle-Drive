using DoodleDrive.Models;

namespace DoodleDrive.Services;

public sealed class AuthResult
{
    public bool Success { get; init; }
    public User? User { get; init; }
    public string? Error { get; init; }

    public static AuthResult Ok(User user) => new() { Success = true, User = user };
    public static AuthResult Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>Authentification par identifiant/mot de passe vérifié en BCrypt contre la table <c>users</c>.</summary>
public sealed class AuthService
{
    private readonly DatabaseService _db;

    public AuthService(DatabaseService db) => _db = db;

    public async Task<AuthResult> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return AuthResult.Fail("Identifiant et mot de passe requis.");

        User? user;
        try
        {
            user = await _db.GetUserByUsernameAsync(username.Trim(), ct);
        }
        catch (Exception ex)
        {
            return AuthResult.Fail($"Impossible de joindre la base : {ex.Message}");
        }

        if (user is null)
            return AuthResult.Fail("Identifiant ou mot de passe incorrect.");

        bool valid;
        try
        {
            valid = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
        }
        catch
        {
            // Hash mal formé en base.
            valid = false;
        }

        return valid
            ? AuthResult.Ok(user)
            : AuthResult.Fail("Identifiant ou mot de passe incorrect.");
    }

    /// <summary>Génère un hash BCrypt pour un nouveau mot de passe (création/réinitialisation de compte).</summary>
    public static string HashPassword(string password) => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 11);
}

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

/// <summary>Authentification via l'API REST : identifiant/mot de passe -> jeton conservé par <see cref="ApiClient"/>.</summary>
public sealed class AuthService
{
    private readonly ApiClient _api;

    public AuthService(ApiClient api) => _api = api;

    public async Task<AuthResult> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return AuthResult.Fail("Identifiant et mot de passe requis.");
        try
        {
            var user = await _api.LoginAsync(username.Trim(), password, ct);
            return AuthResult.Ok(ToUser(user));
        }
        catch (ApiException ex)
        {
            return AuthResult.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            return AuthResult.Fail($"Serveur injoignable : {ex.Message}");
        }
    }

    /// <summary>Reconnexion silencieuse via un jeton mémorisé (« rester connecté »).</summary>
    public async Task<AuthResult> LoginWithTokenAsync(string token, CancellationToken ct = default)
    {
        _api.Token = token;
        try
        {
            var user = await _api.MeAsync(ct);
            return AuthResult.Ok(ToUser(user));
        }
        catch (Exception ex)
        {
            _api.Token = null;
            return AuthResult.Fail(ex.Message);
        }
    }

    /// <summary>Jeton courant (à mémoriser si « rester connecté »).</summary>
    public string? CurrentToken => _api.Token;

    public void SignOut() => _api.SignOut();

    /// <summary>Hash BCrypt (encore utilisé par l'admin direct tant que sa migration API n'est pas faite).</summary>
    public static string HashPassword(string password) => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 11);

    private static User ToUser(ApiUser u) => new()
    {
        Id = u.Id,
        Username = u.Username,
        Role = string.Equals(u.Role, "admin", StringComparison.OrdinalIgnoreCase) ? UserRole.Admin : UserRole.User,
        DefaultPath = u.DefaultPath,
        LastPath = u.LastPath
    };
}

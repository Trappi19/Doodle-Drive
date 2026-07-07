using DoodleDrive.Models;

namespace DoodleDrive.Services;

/// <summary>Contexte de l'utilisateur connecté, partagé entre les ViewModels.</summary>
public sealed class Session
{
    public User? CurrentUser { get; private set; }

    public bool IsAuthenticated => CurrentUser is not null;

    public bool IsAdmin => CurrentUser?.IsAdmin ?? false;

    public int UserId => CurrentUser?.Id ?? 0;

    public string UserName => CurrentUser?.Username ?? string.Empty;

    public void SignIn(User user) => CurrentUser = user;

    public void SignOut() => CurrentUser = null;
}

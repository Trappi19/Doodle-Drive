using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoodleDrive.Models;
using DoodleDrive.Services;

namespace DoodleDrive.ViewModels;

/// <summary>
/// Écran de connexion (version API) : adresse du serveur + identifiant + mot de passe.
/// L'app ne parle plus qu'à l'API — plus aucun réglage MariaDB/FTP côté client.
/// </summary>
public sealed partial class LoginViewModel : ObservableObject
{
    private readonly AppConfigService _configService;
    private readonly AuthService _auth;
    private readonly Session _session;
    private readonly NotificationService _notifications;

    public LoginViewModel(AppConfigService configService, AuthService auth, Session session, NotificationService notifications)
    {
        _configService = configService;
        _auth = auth;
        _session = session;
        _notifications = notifications;

        var c = configService.Current;
        _serverUrl = c.ShareBaseUrl;
        _username = c.RememberMe ? c.RememberedUsername : string.Empty;
        _rememberMe = c.RememberMe;

        LoginCommand = new AsyncRelayCommand(LoginAsync, () => !IsBusy);
    }

    public event Action? LoginSucceeded;

    public AsyncRelayCommand LoginCommand { get; }

    [ObservableProperty] private string _serverUrl;
    [ObservableProperty] private string _username;
    [ObservableProperty] private string _passwordInput = string.Empty;
    [ObservableProperty] private bool _rememberMe;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _statusMessage = string.Empty;

    [ObservableProperty] private bool _isError;
    [ObservableProperty] private bool _isBusy;

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>Reconnexion auto possible si « rester connecté » a mémorisé un jeton + une URL.</summary>
    public bool CanAutoLogin =>
        RememberMe && !string.IsNullOrWhiteSpace(ServerUrl) && !string.IsNullOrEmpty(_configService.Current.ApiToken);

    partial void OnIsBusyChanged(bool value) => LoginCommand.NotifyCanExecuteChanged();

    private void SaveServerUrl()
    {
        var c = _configService.Current;
        c.ShareBaseUrl = ServerUrl.Trim();
        _configService.Save(c);
    }

    private async Task LoginAsync()
    {
        IsBusy = true;
        SetStatus(string.Empty, false);
        try
        {
            if (string.IsNullOrWhiteSpace(ServerUrl))
            {
                SetStatus("Renseignez l'adresse du serveur.", true);
                return;
            }
            SaveServerUrl();

            var result = await _auth.LoginAsync(Username, PasswordInput);
            if (!result.Success || result.User is null)
            {
                SetStatus(result.Error ?? "Échec de connexion.", true);
                return;
            }

            _session.SignIn(result.User);
            Persist(result.User);
            LoginSucceeded?.Invoke();
        }
        catch (Exception ex)
        {
            SetStatus($"Erreur : {ex.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Reconnexion silencieuse via le jeton mémorisé. Renvoie true si réussie.</summary>
    public async Task<bool> TryAutoLoginAsync()
    {
        var token = _configService.Current.ApiToken;
        if (string.IsNullOrEmpty(token) || string.IsNullOrWhiteSpace(ServerUrl)) return false;
        SaveServerUrl();

        var result = await _auth.LoginWithTokenAsync(token);
        if (!result.Success || result.User is null) return false;

        _session.SignIn(result.User);
        Persist(result.User);
        LoginSucceeded?.Invoke();
        return true;
    }

    private void Persist(User user)
    {
        var c = _configService.Current;
        c.RememberMe = RememberMe;
        c.RememberedUsername = RememberMe ? user.Username : string.Empty;
        c.ApiToken = RememberMe ? (_auth.CurrentToken ?? string.Empty) : string.Empty;
        _configService.Save(c);
    }

    private void SetStatus(string message, bool error)
    {
        IsError = error;
        StatusMessage = message;
    }
}

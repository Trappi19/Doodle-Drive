using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoodleDrive.Models;
using DoodleDrive.Services;

namespace DoodleDrive.ViewModels;

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly AppConfigService _configService;
    private readonly AuthService _auth;
    private readonly DatabaseService _db;
    private readonly FtpService _ftp;
    private readonly Session _session;
    private readonly NotificationService _notifications;

    public LoginViewModel(
        AppConfigService configService, AuthService auth, DatabaseService db,
        FtpService ftp, Session session, NotificationService notifications)
    {
        _configService = configService;
        _auth = auth;
        _db = db;
        _ftp = ftp;
        _session = session;
        _notifications = notifications;

        var c = configService.Current;
        _username = c.RememberMe ? c.RememberedUsername : string.Empty;
        _passwordInput = c.RememberMe ? c.RememberedPassword : string.Empty;
        _rememberMe = c.RememberMe;

        _dbHost = c.DbHost; _dbPort = c.DbPort; _dbName = c.DbName; _dbUser = c.DbUser; _dbPassword = c.DbPassword;
        _ftpHost = c.FtpHost; _ftpPort = c.FtpPort; _ftpUser = c.FtpUser; _ftpPassword = c.FtpPassword;
        _ftpRootPath = c.FtpRootPath; _ftpUseTls = c.FtpUseTls;

        // Premier lancement (base non configurée) : accès libre aux réglages pour l'installation.
        // Ensuite, il faut prouver qu'on est admin pour déverrouiller les paramètres serveur.
        var firstRun = string.IsNullOrWhiteSpace(c.DbUser) || string.IsNullOrWhiteSpace(c.DbPassword);
        _isServerPanelOpen = firstRun;
        _isServerUnlocked = firstRun;

        LoginCommand = new AsyncRelayCommand(LoginAsync, () => !IsBusy);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => !IsBusy);
        UnlockServerCommand = new AsyncRelayCommand(UnlockServerAsync, () => !IsBusy);
    }

    public event Action? LoginSucceeded;

    public AsyncRelayCommand LoginCommand { get; }
    public AsyncRelayCommand TestConnectionCommand { get; }
    public AsyncRelayCommand UnlockServerCommand { get; }

    [ObservableProperty] private string _username;
    [ObservableProperty] private string _passwordInput = string.Empty;
    [ObservableProperty] private bool _rememberMe;
    [ObservableProperty] private bool _isServerPanelOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsServerLocked))]
    private bool _isServerUnlocked;

    public bool IsServerLocked => !IsServerUnlocked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _statusMessage = string.Empty;

    [ObservableProperty] private bool _isError;

    [ObservableProperty] private bool _isBusy;

    // --- Paramètres serveur (édités depuis l'écran de connexion) ---
    [ObservableProperty] private string _dbHost;
    [ObservableProperty] private int _dbPort;
    [ObservableProperty] private string _dbName;
    [ObservableProperty] private string _dbUser;
    [ObservableProperty] private string _dbPassword;
    [ObservableProperty] private string _ftpHost;
    [ObservableProperty] private int _ftpPort;
    [ObservableProperty] private string _ftpUser;
    [ObservableProperty] private string _ftpPassword;
    [ObservableProperty] private string _ftpRootPath;
    [ObservableProperty] private bool _ftpUseTls;

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>Vrai si un identifiant + mot de passe mémorisés permettent une connexion automatique.</summary>
    public bool CanAutoLogin =>
        RememberMe && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrEmpty(PasswordInput);

    partial void OnIsBusyChanged(bool value)
    {
        LoginCommand.NotifyCanExecuteChanged();
        TestConnectionCommand.NotifyCanExecuteChanged();
        UnlockServerCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Déverrouille les paramètres serveur en prouvant qu'on est admin : on vérifie
    /// l'identifiant/mot de passe saisis en haut contre un compte admin de la base.
    /// Au tout premier lancement (base non configurée), l'accès est libre.
    /// </summary>
    private async Task UnlockServerAsync()
    {
        var c = _configService.Current;
        if (string.IsNullOrWhiteSpace(c.DbUser) || string.IsNullOrWhiteSpace(c.DbPassword))
        {
            IsServerUnlocked = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(PasswordInput))
        {
            SetStatus("Saisissez vos identifiants administrateur ci-dessus pour déverrouiller.", true);
            return;
        }

        IsBusy = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var res = await _auth.LoginAsync(Username, PasswordInput, cts.Token);
            if (res.Success && res.User is { IsAdmin: true })
            {
                IsServerUnlocked = true;
                SetStatus("Paramètres serveur déverrouillés.", false);
            }
            else
            {
                SetStatus("Réservé aux administrateurs : identifiant ou mot de passe admin invalide.", true);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Impossible de vérifier : {ex.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyConfig()
    {
        var c = _configService.Current;
        c.DbHost = DbHost.Trim(); c.DbPort = DbPort; c.DbName = DbName.Trim();
        c.DbUser = DbUser.Trim(); c.DbPassword = DbPassword;
        c.FtpHost = FtpHost.Trim(); c.FtpPort = FtpPort; c.FtpUser = FtpUser.Trim();
        c.FtpPassword = FtpPassword; c.FtpRootPath = FtpPathUtil.Normalize(FtpRootPath);
        c.FtpUseTls = FtpUseTls;
        _configService.Save(c);
    }

    private void SetStatus(string message, bool error)
    {
        IsError = error;
        StatusMessage = message;
    }

    /// <summary>
    /// Importe les paramètres serveur depuis un fichier (<c>.ddconfig</c> chiffré ou <c>.env</c>
    /// en clair), remplit les champs et enregistre la config (chiffrée DPAPI au repos).
    /// </summary>
    public void ImportFromFile(string path)
    {
        try
        {
            var env = PortableConfig.LoadFile(path);
            if (env.Count == 0)
            {
                SetStatus("Fichier de configuration illisible ou vide.", true);
                return;
            }

            string? Get(string key) =>
                env.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

            if (Get("DOODLE_DB_HOST") is { } dbHost) DbHost = dbHost;
            if (Get("DOODLE_DB_PORT") is { } dbPort && int.TryParse(dbPort, out var dbPortValue)) DbPort = dbPortValue;
            if (Get("DOODLE_DB_NAME") is { } dbName) DbName = dbName;
            if (Get("DOODLE_DB_USER") is { } dbUser) DbUser = dbUser;
            if (Get("DOODLE_DB_PASSWORD") is { } dbPassword) DbPassword = dbPassword;
            if (Get("DOODLE_FTP_HOST") is { } ftpHost) FtpHost = ftpHost;
            if (Get("DOODLE_FTP_PORT") is { } ftpPort && int.TryParse(ftpPort, out var ftpPortValue)) FtpPort = ftpPortValue;
            if (Get("DOODLE_FTP_USER") is { } ftpUser) FtpUser = ftpUser;
            if (Get("DOODLE_FTP_PASSWORD") is { } ftpPassword) FtpPassword = ftpPassword;
            if (Get("DOODLE_FTP_ROOT") is { } ftpRoot) FtpRootPath = ftpRoot;
            if (Get("DOODLE_FTP_TLS") is { } ftpTls)
                FtpUseTls = ftpTls.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "oui" or "on";

            // Enregistre les nouveaux paramètres (persistés et chiffrés dans config.json).
            ApplyConfig();
            SetStatus("Configuration serveur importée. Vous pouvez vous connecter.", false);
        }
        catch (Exception ex)
        {
            SetStatus($"Import impossible : {ex.Message}", true);
        }
    }

    private async Task TestConnectionAsync()
    {
        IsBusy = true;
        SetStatus("Test de connexion en cours…", false);
        try
        {
            ApplyConfig();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await _db.TestConnectionAsync(cts.Token);
            await _ftp.TestConnectionAsync(cts.Token);
            SetStatus("Connexion à la base et au FTP réussie.", false);
            _notifications.Success("Connexion réussie", "Base MariaDB et FTP joignables.");
        }
        catch (Exception ex)
        {
            SetStatus($"Échec de connexion : {ex.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoginAsync()
    {
        IsBusy = true;
        SetStatus(string.Empty, false);
        try
        {
            ApplyConfig();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await _auth.LoginAsync(Username, PasswordInput, cts.Token);
            if (!result.Success || result.User is null)
            {
                SetStatus(result.Error ?? "Échec de connexion.", true);
                return;
            }

            _session.SignIn(result.User);

            var c = _configService.Current;
            c.RememberMe = RememberMe;
            c.RememberedUsername = RememberMe ? result.User.Username : string.Empty;
            c.RememberedPassword = RememberMe ? PasswordInput : string.Empty;
            _configService.Save(c);

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
}

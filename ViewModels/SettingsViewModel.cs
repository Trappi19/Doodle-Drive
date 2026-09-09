using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoodleDrive.Services;
using Wpf.Ui.Appearance;

namespace DoodleDrive.ViewModels;

/// <summary>
/// Paramètres : apparence, démarrage, adresse du serveur (API) et réinitialisation de la connexion.
/// Depuis la migration API, l'app ne se connecte plus directement à MariaDB/FTP : ces sections
/// ont été retirées (tout passe par l'URL du serveur ci-dessous).
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppConfigService _configService;
    private readonly NotificationService _notify;
    private readonly Session _session;
    private readonly DialogService _dialogs;

    public SettingsViewModel(AppConfigService configService, NotificationService notify,
        Session session, DialogService dialogs)
    {
        _configService = configService;
        _notify = notify;
        _session = session;
        _dialogs = dialogs;

        var c = configService.Current;
        _theme = c.Theme; _defaultView = c.DefaultView;
        _launchAtStartup = StartupRegistration.IsEnabled();
        _openWindowOnStartup = c.OpenWindowOnStartup;
        _shareBaseUrl = c.ShareBaseUrl;

        SaveCommand = new RelayCommand(Save);
        ResetConnectionCommand = new RelayCommand(ResetConnection);
    }

    /// <summary>Demande la déconnexion (après réinitialisation de la connexion serveur).</summary>
    public event Action? SignOutRequested;

    public RelayCommand SaveCommand { get; }
    public RelayCommand ResetConnectionCommand { get; }

    /// <summary>Seul un admin peut voir/modifier l'adresse du serveur (les users ne choisissent pas leur point d'entrée).</summary>
    public bool IsAdmin => _session.IsAdmin;

    public IReadOnlyList<string> ThemeOptions { get; } = new[] { "System", "Light", "Dark" };
    public IReadOnlyList<string> ViewOptions { get; } = new[] { "Grid", "List" };

    [ObservableProperty] private string _defaultView;
    [ObservableProperty] private bool _launchAtStartup;
    [ObservableProperty] private bool _openWindowOnStartup;
    [ObservableProperty] private string _shareBaseUrl;
    [ObservableProperty] private string _theme;

    partial void OnThemeChanged(string value) => ApplyTheme(value);

    /// <summary>Applique immédiatement le lancement au démarrage (clé Run utilisateur).</summary>
    partial void OnLaunchAtStartupChanged(bool value)
    {
        try
        {
            StartupRegistration.SetEnabled(value);
            _notify.Success(
                "Démarrage automatique",
                value ? "Doodle Drive se lancera à l'ouverture de session." : "Désactivé.");
        }
        catch (Exception ex)
        {
            _notify.Error("Réglage impossible", ex.Message);
        }
    }

    /// <summary>Persiste immédiatement l'option (elle est lue au prochain démarrage de Windows).</summary>
    partial void OnOpenWindowOnStartupChanged(bool value)
    {
        var c = _configService.Current;
        c.OpenWindowOnStartup = value;
        _configService.Save(c);
    }

    public static void ApplyTheme(string theme)
    {
        switch (theme)
        {
            case "Light":
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                break;
            case "Dark":
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                break;
            default:
                ApplicationThemeManager.ApplySystemTheme();
                break;
        }
    }

    private void Save()
    {
        var c = _configService.Current;

        var serverChanged = c.ShareBaseUrl != (ShareBaseUrl?.Trim() ?? string.Empty);

        c.Theme = Theme; c.DefaultView = DefaultView;
        c.OpenWindowOnStartup = OpenWindowOnStartup;
        c.ShareBaseUrl = ShareBaseUrl?.Trim() ?? string.Empty;
        _configService.Save(c);
        _notify.Success("Paramètres enregistrés");

        if (serverChanged) _configService.NotifyConnectionChanged();
    }

    /// <summary>
    /// Réinitialise l'adresse du serveur puis déconnecte l'utilisateur. Accessible à tous
    /// (un utilisateur bloqué peut ainsi repartir de zéro).
    /// </summary>
    private void ResetConnection()
    {
        if (!_dialogs.Confirm(
                "Réinitialiser la connexion",
                "L'adresse du serveur sera effacée et vous serez déconnecté. Continuer ?",
                "Réinitialiser et déconnecter", destructive: true))
            return;

        _configService.ResetServerConfig();
        SignOutRequested?.Invoke();
    }
}

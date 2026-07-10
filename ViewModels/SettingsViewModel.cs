using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoodleDrive.Services;
using Wpf.Ui.Appearance;

namespace DoodleDrive.ViewModels;

/// <summary>Paramètres : connexions MariaDB/FTP, thème, vue par défaut.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppConfigService _configService;
    private readonly DatabaseService _db;
    private readonly FtpService _ftp;
    private readonly NotificationService _notify;
    private readonly Session _session;

    public SettingsViewModel(AppConfigService configService, DatabaseService db, FtpService ftp,
        NotificationService notify, Session session)
    {
        _configService = configService;
        _db = db;
        _ftp = ftp;
        _notify = notify;
        _session = session;

        var c = configService.Current;
        _dbHost = c.DbHost; _dbPort = c.DbPort; _dbName = c.DbName; _dbUser = c.DbUser; _dbPassword = c.DbPassword;
        _ftpHost = c.FtpHost; _ftpPort = c.FtpPort; _ftpUser = c.FtpUser; _ftpPassword = c.FtpPassword;
        _ftpRootPath = c.FtpRootPath; _ftpUseTls = c.FtpUseTls;
        _theme = c.Theme; _defaultView = c.DefaultView;
        _launchAtStartup = StartupRegistration.IsEnabled();

        SaveCommand = new RelayCommand(Save);
        TestDbCommand = new AsyncRelayCommand(TestDbAsync, () => !IsBusy);
        TestFtpCommand = new AsyncRelayCommand(TestFtpAsync, () => !IsBusy);
    }

    public RelayCommand SaveCommand { get; }
    public AsyncRelayCommand TestDbCommand { get; }
    public AsyncRelayCommand TestFtpCommand { get; }

    /// <summary>Seul un admin peut voir/modifier la connexion serveur (les users ne choisissent pas leur point d'entrée).</summary>
    public bool IsAdmin => _session.IsAdmin;

    public IReadOnlyList<string> ThemeOptions { get; } = new[] { "System", "Light", "Dark" };
    public IReadOnlyList<string> ViewOptions { get; } = new[] { "Grid", "List" };

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
    [ObservableProperty] private string _defaultView;
    [ObservableProperty] private bool _launchAtStartup;
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private string _theme;

    partial void OnIsBusyChanged(bool value)
    {
        TestDbCommand.NotifyCanExecuteChanged();
        TestFtpCommand.NotifyCanExecuteChanged();
    }

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
        c.DbHost = DbHost.Trim(); c.DbPort = DbPort; c.DbName = DbName.Trim();
        c.DbUser = DbUser.Trim(); c.DbPassword = DbPassword;
        c.FtpHost = FtpHost.Trim(); c.FtpPort = FtpPort; c.FtpUser = FtpUser.Trim();
        c.FtpPassword = FtpPassword; c.FtpRootPath = FtpPathUtil.Normalize(FtpRootPath);
        c.FtpUseTls = FtpUseTls;
        c.Theme = Theme; c.DefaultView = DefaultView;
        _configService.Save(c);
        _notify.Success("Paramètres enregistrés");
    }

    /// <summary>
    /// Exporte les paramètres serveur courants vers un fichier <c>.ddconfig</c> chiffré,
    /// à importer sur les autres machines. Réservé aux administrateurs.
    /// </summary>
    public void ExportProfile(string path)
    {
        if (!IsAdmin) return;
        try
        {
            // Enregistre d'abord d'éventuelles modifications en cours, puis exporte.
            Save();
            var c = _configService.Current;
            File.WriteAllBytes(path, PortableConfig.Encrypt(PortableConfig.BuildEnvText(c)));
            _notify.Success("Profil exporté",
                "Fichier chiffré prêt à importer sur une autre machine (écran de connexion).");
        }
        catch (Exception ex)
        {
            _notify.Error("Export impossible", ex.Message);
        }
    }

    private async Task TestDbAsync()
    {
        IsBusy = true;
        try
        {
            Save();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await _db.TestConnectionAsync(cts.Token);
            _notify.Success("MariaDB", "Connexion réussie.");
        }
        catch (Exception ex)
        {
            _notify.Error("MariaDB injoignable", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task TestFtpAsync()
    {
        IsBusy = true;
        try
        {
            Save();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await _ftp.TestConnectionAsync(cts.Token);
            _notify.Success("FTP", "Connexion réussie.");
        }
        catch (Exception ex)
        {
            _notify.Error("FTP injoignable", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

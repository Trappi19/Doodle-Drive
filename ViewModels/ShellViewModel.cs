using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoodleDrive.Services;

namespace DoodleDrive.ViewModels;

/// <summary>ViewModel de la fenêtre principale : navigation entre Fichiers / Admin / Paramètres.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly AppServices _services;

    public ShellViewModel(AppServices services)
    {
        _services = services;

        Files = new FilesViewModel(services.Database, services.Ftp, services.Thumbnails,
            services.Dialogs, services.Notifications, services.Session, services.Config);
        Settings = new SettingsViewModel(services.Config, services.Notifications, services.Session, services.Dialogs);
        Settings.SignOutRequested += () => SignedOut?.Invoke();
        Settings.CheckUpdatesRequested += () => _ = RunUpdateFlowAsync(manual: true);
        Shares = new SharesViewModel(services.Database, services.Notifications, services.Session, services.Config, services.Dialogs);
        Sync = new SyncViewModel(services.Api, services.Sync, services.Ftp, services.Config, services.Dialogs, services.Notifications);
        if (services.Session.IsAdmin)
            Admin = new AdminViewModel(services.Database, services.Dialogs, services.Notifications, services.Session);

        NavigateFilesCommand = new AsyncRelayCommand(GoFilesAsync);
        NavigateAdminCommand = new AsyncRelayCommand(GoAdminAsync, () => IsAdmin);
        NavigateSharesCommand = new AsyncRelayCommand(GoSharesAsync);
        NavigateSyncCommand = new AsyncRelayCommand(GoSyncAsync);
        NavigateSettingsCommand = new RelayCommand(GoSettings);
        SignOutCommand = new RelayCommand(() => SignedOut?.Invoke());
        DismissToastCommand = new RelayCommand<Toast?>(t => { if (t is not null) services.Notifications.Dismiss(t); });

        _currentPage = Files;
    }

    public event Action? SignedOut;

    // ----- Bandeau de mise à jour (intégré à la fenêtre, non intrusif) -----
    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdatePercentText))]
    private double _updateProgress;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdatePercentText))]
    private bool _updateIndeterminate = true;
    [ObservableProperty] private string _updateStatus = string.Empty;

    public string UpdatePercentText => UpdateIndeterminate ? string.Empty : $"{UpdateProgress:0} %";

    public FilesViewModel Files { get; }
    public SettingsViewModel Settings { get; }
    public SharesViewModel Shares { get; }
    public SyncViewModel Sync { get; }
    public AdminViewModel? Admin { get; }

    public ObservableCollection<Toast> Toasts => _services.Notifications.Toasts;

    public bool IsAdmin => _services.Session.IsAdmin;
    public string UserName => _services.Session.UserName;
    public string RoleLabel => IsAdmin ? "Administrateur" : "Utilisateur";
    public string Initials => string.IsNullOrEmpty(UserName) ? "?" : UserName[..1].ToUpperInvariant();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilesActive))]
    [NotifyPropertyChangedFor(nameof(IsAdminActive))]
    [NotifyPropertyChangedFor(nameof(IsSharesActive))]
    [NotifyPropertyChangedFor(nameof(IsSyncActive))]
    [NotifyPropertyChangedFor(nameof(IsSettingsActive))]
    private ObservableObject _currentPage;

    public bool IsFilesActive => ReferenceEquals(CurrentPage, Files);
    public bool IsAdminActive => Admin is not null && ReferenceEquals(CurrentPage, Admin);
    public bool IsSharesActive => ReferenceEquals(CurrentPage, Shares);
    public bool IsSyncActive => ReferenceEquals(CurrentPage, Sync);
    public bool IsSettingsActive => ReferenceEquals(CurrentPage, Settings);

    public AsyncRelayCommand NavigateFilesCommand { get; }
    public AsyncRelayCommand NavigateAdminCommand { get; }
    public AsyncRelayCommand NavigateSharesCommand { get; }
    public AsyncRelayCommand NavigateSyncCommand { get; }
    public RelayCommand NavigateSettingsCommand { get; }
    public RelayCommand SignOutCommand { get; }
    public RelayCommand<Toast?> DismissToastCommand { get; }

    private bool _filesInitialized;

    public async Task StartAsync()
    {
        await GoFilesAsync();
        // Préchargement discret des onglets Partages / Synchronisation : quand l'utilisateur
        // clique dessus, le contenu est déjà là (le réseau via Funnel a une latence variable).
        _ = Shares.LoadAsync(silent: true);
        _syncInitialized = true;
        _ = Sync.LoadAsync(silent: true);
        _ = RunUpdateFlowAsync(manual: false); // vérif mise à jour en arrière-plan
        StartAutoSync();
    }

    private System.Windows.Threading.DispatcherTimer? _autoSyncTimer;
    private bool _autoSyncRunning;

    /// <summary>Lance la synchro auto au démarrage puis périodiquement (dossiers « Automatique »).</summary>
    private void StartAutoSync()
    {
        _ = RunAutoSyncAsync();
        _autoSyncTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
        _autoSyncTimer.Tick += (_, _) => _ = RunAutoSyncAsync();
        _autoSyncTimer.Start();
    }

    private async Task RunAutoSyncAsync()
    {
        if (_autoSyncRunning) return;
        _autoSyncRunning = true;
        try
        {
            List<Services.ApiSyncFolder> folders;
            try { folders = await _services.Api.GetSyncFoldersAsync(); }
            catch { return; } // serveur injoignable : on réessaiera au prochain tick

            var mid = _services.Config.Current.MachineId;
            foreach (var f in folders.Where(f => f.AutoSync && string.Equals(f.MachineId, mid, StringComparison.Ordinal)))
            {
                try
                {
                    var res = await _services.Sync.SyncAsync(f.Id, f.LocalPath, f.RemotePath);
                    await _services.Api.TouchSyncAsync(f.Id);
                    if (res.Changed > 0)
                        _services.Notifications.Success("Synchronisation automatique",
                            $"{FtpPathUtil.GetName(f.RemotePath)} : {res.Changed} fichier(s).");
                }
                catch { /* silencieux en auto : on ne dérange pas l'utilisateur */ }
            }
        }
        finally
        {
            _autoSyncRunning = false;
        }
    }

    private bool _updateFlowRunning;

    /// <summary>
    /// Vérifie s'il existe une nouvelle version ; si oui, propose de l'installer en un clic.
    /// <paramref name="manual"/> = déclenché depuis les Paramètres (on notifie « à jour » / les erreurs).
    /// </summary>
    private async Task RunUpdateFlowAsync(bool manual)
    {
        if (_updateFlowRunning) return;
        _updateFlowRunning = true;
        try
        {
            UpdateAvailable? upd;
            try
            {
                upd = await _services.Update.CheckAsync();
            }
            catch (Exception ex)
            {
                if (manual) _services.Notifications.Error("Vérification impossible", ex.Message);
                return; // au démarrage : échec silencieux (serveur injoignable, etc.)
            }

            if (upd is null)
            {
                if (manual)
                    _services.Notifications.Success("Application à jour",
                        $"Vous utilisez déjà la dernière version ({UpdateService.CurrentVersionText}).");
                return;
            }

            var message = $"La version {upd.VersionText} est disponible (vous avez {UpdateService.CurrentVersionText}).";
            if (!string.IsNullOrWhiteSpace(upd.Notes)) message += $"\n\n{upd.Notes}";
            message += "\n\nTélécharger et installer maintenant ? L'application se fermera puis se rouvrira.";

            if (!_services.Dialogs.Confirm("Mise à jour disponible", message, "Installer maintenant"))
                return;

            IsUpdating = true;
            UpdateIndeterminate = true;
            UpdateStatus = "Téléchargement de la mise à jour…";
            string setup;
            try
            {
                var progress = new Progress<double>(p => { UpdateIndeterminate = false; UpdateProgress = p; });
                setup = await _services.Update.DownloadAsync(upd, progress);
            }
            catch (Exception ex)
            {
                IsUpdating = false;
                _services.Notifications.Error("Téléchargement impossible", ex.Message);
                return;
            }

            UpdateIndeterminate = true;
            UpdateStatus = "Installation… l'application va redémarrer.";
            _services.Update.InstallAndRestart(setup); // lance l'installeur puis ferme l'app
        }
        finally
        {
            _updateFlowRunning = false;
        }
    }

    private async Task GoFilesAsync()
    {
        CurrentPage = Files;
        if (!_filesInitialized)
        {
            _filesInitialized = true;
            await Files.InitializeAsync();
        }
    }

    private async Task GoAdminAsync()
    {
        if (Admin is null) return;
        CurrentPage = Admin;
        await Admin.LoadAsync();
    }

    private async Task GoSharesAsync()
    {
        CurrentPage = Shares;
        // La liste déjà chargée s'affiche tout de suite ; on l'actualise en arrière-plan.
        await Shares.LoadAsync();
    }

    private bool _syncInitialized;

    private async Task GoSyncAsync()
    {
        CurrentPage = Sync;
        // Chargé une seule fois : une synchro en cours n'est pas interrompue/rechargée
        // quand on quitte puis revient sur l'onglet (elle continue en arrière-plan).
        if (!_syncInitialized)
        {
            _syncInitialized = true;
            await Sync.LoadAsync();
        }
        else if (Sync.LoadError is not null)
        {
            await Sync.LoadAsync(); // le préchargement avait échoué : on retente
        }
    }

    private void GoSettings() => CurrentPage = Settings;
}

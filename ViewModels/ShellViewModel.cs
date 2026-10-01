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
        Settings.PreferDirectChanged += value =>
        {
            services.Api.PreferDirect = value;
            _ = services.Api.RefreshRouteAsync();
        };
        Settings.SignOutRequested += () => SignedOut?.Invoke();
        Settings.CheckUpdatesRequested += () => _ = RunUpdateFlowAsync(manual: true);
        Shares = new SharesViewModel(services.Database, services.Notifications, services.Session, services.Config, services.Dialogs);
        Sync = new SyncViewModel(services.Api, services.Sync, services.Ftp, services.Config, services.Dialogs, services.Notifications);
        Trash = new TrashViewModel(services.Api, services.Notifications, services.Dialogs, services.Session);
        Trash.ItemRestored += () => _ = Files.RefreshCommand.ExecuteAsync(null);
        services.Api.RouteChanged += () => App.Dispatch(() =>
        {
            OnPropertyChanged(nameof(IsDirect));
            OnPropertyChanged(nameof(ConnectionText));
            OnPropertyChanged(nameof(ConnectionTooltip));
            OnPropertyChanged(nameof(ConnectionGlyph));
        });
        if (services.Session.IsAdmin)
            Admin = new AdminViewModel(services.Database, services.Dialogs, services.Notifications, services.Session);

        NavigateFilesCommand = new AsyncRelayCommand(GoFilesAsync);
        NavigateAdminCommand = new AsyncRelayCommand(GoAdminAsync, () => IsAdmin);
        NavigateSharesCommand = new AsyncRelayCommand(GoSharesAsync);
        NavigateSyncCommand = new AsyncRelayCommand(GoSyncAsync);
        NavigateTrashCommand = new AsyncRelayCommand(GoTrashAsync);
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
    public TrashViewModel Trash { get; }

    // ----- Barre latérale : espace disque du serveur + itinéraire réseau -----
    public System.Collections.ObjectModel.ObservableCollection<StorageVolumeViewModel> Volumes { get; } = new();
    public bool HasVolumes => Volumes.Count > 0;
    public bool IsDirect => _services.Api.IsDirect;
    public string ConnectionText => IsDirect ? "Connexion directe" : "Connexion via Internet";
    public string ConnectionGlyph => IsDirect ? "\uE945" : "\uE774";
    public string ConnectionTooltip => IsDirect
        ? "Accès direct au serveur via Tailscale (rapide)."
        : "Accès par l'adresse publique (Tailscale Funnel). L'accès direct est utilisé automatiquement quand ce PC est sur le réseau Tailscale.";
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
    [NotifyPropertyChangedFor(nameof(IsTrashActive))]
    [NotifyPropertyChangedFor(nameof(IsSettingsActive))]
    private ObservableObject _currentPage;

    public bool IsFilesActive => ReferenceEquals(CurrentPage, Files);

    partial void OnCurrentPageChanged(ObservableObject value)
    {
        if (!ReferenceEquals(value, Files)) Files.CancelSearch();
    }
    public bool IsAdminActive => Admin is not null && ReferenceEquals(CurrentPage, Admin);
    public bool IsSharesActive => ReferenceEquals(CurrentPage, Shares);
    public bool IsSyncActive => ReferenceEquals(CurrentPage, Sync);
    public bool IsTrashActive => ReferenceEquals(CurrentPage, Trash);
    public bool IsSettingsActive => ReferenceEquals(CurrentPage, Settings);

    public AsyncRelayCommand NavigateFilesCommand { get; }
    public AsyncRelayCommand NavigateAdminCommand { get; }
    public AsyncRelayCommand NavigateSharesCommand { get; }
    public AsyncRelayCommand NavigateSyncCommand { get; }
    public AsyncRelayCommand NavigateTrashCommand { get; }
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
        StartBackgroundRefresh();
    }

    private System.Windows.Threading.DispatcherTimer? _routeTimer;
    private System.Windows.Threading.DispatcherTimer? _storageTimer;
    private bool _lowSpaceWarned;

    /// <summary>
    /// Itinéraire réseau (direct/Internet) vérifié toutes les minutes ; espace disque toutes les 5 min.
    /// </summary>
    private void StartBackgroundRefresh()
    {
        _services.Api.PreferDirect = _services.Config.Current.PreferDirect;
        _ = _services.Api.RefreshRouteAsync();
        _routeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _routeTimer.Tick += (_, _) => _ = _services.Api.RefreshRouteAsync();
        _routeTimer.Start();

        _ = RefreshStorageAsync();
        _storageTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _storageTimer.Tick += (_, _) => _ = RefreshStorageAsync();
        _storageTimer.Start();
    }

    private async Task RefreshStorageAsync()
    {
        try
        {
            var s = await _services.Api.GetStorageAsync();
            Volumes.Clear();
            foreach (var v in s.Volumes) Volumes.Add(new StorageVolumeViewModel(v));
            OnPropertyChanged(nameof(HasVolumes));

            // Alerte une seule fois par session si un disque est presque plein (< 5 % libre).
            var critical = s.Volumes.FirstOrDefault(v => v.Total > 0 && v.Free * 100.0 / v.Total < 5);
            if (critical is not null && !_lowSpaceWarned)
            {
                _lowSpaceWarned = true;
                _services.Notifications.Warning("Espace disque presque plein",
                    $"{critical.Name} : {FileEntryViewModel.FormatSize(critical.Free)} libres.");
            }
        }
        catch { /* affichage best-effort : on réessaiera au prochain tick */ }
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

    private async Task GoTrashAsync()
    {
        CurrentPage = Trash;
        await Trash.LoadAsync();
    }

    private void GoSettings() => CurrentPage = Settings;
}

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
        Settings = new SettingsViewModel(services.Config, services.Database, services.Ftp, services.Notifications, services.Session);
        if (services.Session.IsAdmin)
            Admin = new AdminViewModel(services.Database, services.Dialogs, services.Notifications, services.Session);

        NavigateFilesCommand = new AsyncRelayCommand(GoFilesAsync);
        NavigateAdminCommand = new AsyncRelayCommand(GoAdminAsync, () => IsAdmin);
        NavigateSettingsCommand = new RelayCommand(GoSettings);
        SignOutCommand = new RelayCommand(() => SignedOut?.Invoke());
        DismissToastCommand = new RelayCommand<Toast?>(t => { if (t is not null) services.Notifications.Dismiss(t); });

        _currentPage = Files;
    }

    public event Action? SignedOut;

    public FilesViewModel Files { get; }
    public SettingsViewModel Settings { get; }
    public AdminViewModel? Admin { get; }

    public ObservableCollection<Toast> Toasts => _services.Notifications.Toasts;

    public bool IsAdmin => _services.Session.IsAdmin;
    public string UserName => _services.Session.UserName;
    public string RoleLabel => IsAdmin ? "Administrateur" : "Utilisateur";
    public string Initials => string.IsNullOrEmpty(UserName) ? "?" : UserName[..1].ToUpperInvariant();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilesActive))]
    [NotifyPropertyChangedFor(nameof(IsAdminActive))]
    [NotifyPropertyChangedFor(nameof(IsSettingsActive))]
    private ObservableObject _currentPage;

    public bool IsFilesActive => ReferenceEquals(CurrentPage, Files);
    public bool IsAdminActive => Admin is not null && ReferenceEquals(CurrentPage, Admin);
    public bool IsSettingsActive => ReferenceEquals(CurrentPage, Settings);

    public AsyncRelayCommand NavigateFilesCommand { get; }
    public AsyncRelayCommand NavigateAdminCommand { get; }
    public RelayCommand NavigateSettingsCommand { get; }
    public RelayCommand SignOutCommand { get; }
    public RelayCommand<Toast?> DismissToastCommand { get; }

    private bool _filesInitialized;

    public async Task StartAsync()
    {
        await GoFilesAsync();
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

    private void GoSettings() => CurrentPage = Settings;
}

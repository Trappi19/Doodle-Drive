using System.Windows;
using System.Windows.Threading;
using DoodleDrive.Services;
using DoodleDrive.ViewModels;
using DoodleDrive.Views;

namespace DoodleDrive;

public partial class App : Application
{
    private AppServices _services = null!;
    private bool _returningToLogin;

    /// <summary>Exécute une action sur le thread UI (utilisé depuis les tâches de fond).</summary>
    public static void Dispatch(Action action) => Current?.Dispatcher.Invoke(action);

    /// <summary>Positionne la fenêtre active comme propriétaire d'une boîte de dialogue.</summary>
    public static void SetOwner(Window window)
    {
        var owner = Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Current?.MainWindow;
        if (owner is not null && !ReferenceEquals(owner, window) && owner.IsVisible)
            window.Owner = owner;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _services = new AppServices();
        SettingsViewModel.ApplyTheme(_services.Config.Current.Theme);

        DispatcherUnhandledException += OnUnhandledException;

        ShowLogin();
    }

    private void ShowLogin()
    {
        var vm = new LoginViewModel(_services.Config, _services.Auth, _services.Database,
            _services.Ftp, _services.Session, _services.Notifications);
        var window = new LoginWindow { DataContext = vm };
        var authenticated = false;

        vm.LoginSucceeded += () =>
        {
            authenticated = true;
            ShowShell();
            window.Close();
        };

        window.Closed += (_, _) =>
        {
            if (!authenticated) Shutdown();
        };

        MainWindow = window;
        window.Show();
    }

    private void ShowShell()
    {
        var vm = new ShellViewModel(_services);
        var window = new MainWindow { DataContext = vm };

        vm.SignedOut += () =>
        {
            _returningToLogin = true;
            _services.Session.SignOut();
            window.Close();
        };

        window.Closed += (_, _) =>
        {
            if (_returningToLogin)
            {
                _returningToLogin = false;
                ShowLogin();
            }
            else
            {
                Shutdown();
            }
        };

        MainWindow = window;
        window.Show();
        _ = vm.StartAsync();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _services.Notifications.Error("Erreur inattendue", e.Exception.Message);
        e.Handled = true;
    }
}

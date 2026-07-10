using System.Threading;
using System.Windows;
using System.Windows.Threading;
using DoodleDrive.Services;
using DoodleDrive.ViewModels;
using DoodleDrive.Views;

namespace DoodleDrive;

public partial class App : Application
{
    // Instance unique (par session utilisateur). Le mutex détecte une instance déjà
    // lancée ; l'évènement sert à lui demander de ramener sa fenêtre au premier plan.
    private const string SingleInstanceMutexName = @"Local\DoodleDrive.SingleInstance";
    private const string ShowExistingEventName = @"Local\DoodleDrive.ShowExisting";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showExistingEvent;

    private AppServices _services = null!;
    private bool _returningToLogin;
    private TrayService? _tray;
    private bool _exitRequested;

    // Démarrage automatique avec Windows : l'app peut démarrer masquée (tâche de fond).
    private bool _startedFromStartup;
    private bool _startShellHidden;

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

        _startedFromStartup = e.Args.Contains(StartupRegistration.StartupArgument);

        // Une seule instance : si l'app tourne déjà (visible ou en tâche de fond),
        // on lui demande de s'afficher, puis cette 2ᵉ instance se ferme aussitôt.
        _instanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isNewInstance);
        if (!isNewInstance)
        {
            if (EventWaitHandle.TryOpenExisting(ShowExistingEventName, out var existing))
            {
                existing.Set();
                existing.Dispose();
            }
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Écoute les demandes d'affichage émises par une nouvelle tentative de lancement.
        _showExistingEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowExistingEventName);
        new Thread(ShowExistingListener) { IsBackground = true, Name = "SingleInstanceListener" }.Start();

        _services = new AppServices();
        SettingsViewModel.ApplyTheme(_services.Config.Current.Theme);

        DispatcherUnhandledException += OnUnhandledException;

        ShowLogin(allowAutoLogin: true);
    }

    /// <summary>Boucle de fond : ramène la fenêtre au premier plan quand une 2ᵉ instance le demande.</summary>
    private void ShowExistingListener()
    {
        while (_showExistingEvent is not null && _showExistingEvent.WaitOne())
            Dispatch(ActivateMainWindow);
    }

    /// <summary>Affiche et active la fenêtre courante (login ou fenêtre principale masquée dans le tray).</summary>
    private void ActivateMainWindow()
    {
        if (MainWindow is not { } window) return;
        if (!window.IsVisible) window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        // Bascule Topmost pour forcer le passage au premier plan de façon fiable.
        window.Topmost = true;
        window.Topmost = false;
    }

    private void ShowLogin(bool allowAutoLogin = false)
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

        // Démarrage en tâche de fond (lancé par Windows, sans « ouvrir la fenêtre ») :
        // si l'auto-login est possible, on n'affiche pas la fenêtre de connexion (pas de
        // flash) et le shell démarrera masqué. Sinon on affiche l'écran normalement.
        var background = allowAutoLogin && vm.CanAutoLogin
                         && _startedFromStartup && !_services.Config.Current.OpenWindowOnStartup;
        _startShellHidden = background;

        if (!background)
            window.Show();

        // Connexion automatique si « Rester connecté » a mémorisé les identifiants.
        // En cas d'échec (mot de passe changé, serveur injoignable), l'écran reste affiché.
        if (allowAutoLogin && vm.CanAutoLogin)
        {
            var loginTask = vm.LoginCommand.ExecuteAsync(null);
            if (background)
                loginTask.ContinueWith(_ => Dispatch(() =>
                {
                    // Auto-login échoué : on quitte le mode masqué et on affiche le login.
                    if (!authenticated)
                    {
                        _startShellHidden = false;
                        window.Show();
                        window.Activate();
                    }
                }), TaskScheduler.Default);
        }
    }

    private void ShowShell()
    {
        var vm = new ShellViewModel(_services);
        var window = new MainWindow { DataContext = vm };

        // Icône de la zone de notification : l'app tourne en tâche de fond.
        _tray = new TrayService();
        _tray.OpenRequested += () => Dispatch(() =>
        {
            window.Show();
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;
            window.Activate();
        });
        _tray.ExitRequested += () => Dispatch(() =>
        {
            _exitRequested = true;
            window.Close();
        });
        _tray.Show();

        vm.SignedOut += () =>
        {
            _returningToLogin = true;
            _services.Session.SignOut();
            window.Close();
        };

        window.Closing += (_, e) =>
        {
            // Fermer la fenêtre = passer en tâche de fond (sauf Quitter ou déconnexion).
            if (_exitRequested || _returningToLogin) return;
            e.Cancel = true;
            window.Hide();
        };

        window.Closed += (_, _) =>
        {
            _tray?.Dispose();
            _tray = null;
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

        // Démarrage en tâche de fond : fenêtre masquée, seule l'icône du tray est visible.
        var startHidden = _startShellHidden;
        _startShellHidden = false;
        _startedFromStartup = false; // ne concerne que le tout premier démarrage

        if (!startHidden)
            window.Show();
        _ = vm.StartAsync();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _services.Notifications.Error("Erreur inattendue", e.Exception.Message);
        e.Handled = true;
    }
}

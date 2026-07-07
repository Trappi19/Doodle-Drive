namespace DoodleDrive.Services;

/// <summary>
/// Conteneur simple des services partagés (composition manuelle, sans framework DI
/// pour rester léger). Instancié une fois au démarrage.
/// </summary>
public sealed class AppServices
{
    public AppServices()
    {
        Config = new AppConfigService();
        Notifications = new NotificationService();
        Session = new Session();
        Database = new DatabaseService(Config);
        Ftp = new FtpService(Config);
        Auth = new AuthService(Database);
        Thumbnails = new ThumbnailService(Ftp);
        Dialogs = new DialogService();
    }

    public AppConfigService Config { get; }
    public NotificationService Notifications { get; }
    public Session Session { get; }
    public DatabaseService Database { get; }
    public FtpService Ftp { get; }
    public AuthService Auth { get; }
    public ThumbnailService Thumbnails { get; }
    public DialogService Dialogs { get; }
}

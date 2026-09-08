namespace DoodleDrive.Models;

/// <summary>
/// Une connexion serveur nommée et enregistrée (MariaDB + FTP), pour pouvoir basculer
/// entre plusieurs serveurs. Les mots de passe sont chiffrés au repos comme le reste
/// de la config (DPAPI, géré par <c>AppConfigService</c>).
/// </summary>
public sealed class ConnectionProfile
{
    public string Name { get; set; } = string.Empty;

    public string DbHost { get; set; } = string.Empty;
    public int DbPort { get; set; } = 3306;
    public string DbName { get; set; } = string.Empty;
    public string DbUser { get; set; } = string.Empty;
    public string DbPassword { get; set; } = string.Empty;

    public string FtpHost { get; set; } = string.Empty;
    public int FtpPort { get; set; } = 21;
    public string FtpUser { get; set; } = string.Empty;
    public string FtpPassword { get; set; } = string.Empty;
    public string FtpRootPath { get; set; } = "/";
    public bool FtpUseTls { get; set; }

    public ConnectionProfile Clone() => (ConnectionProfile)MemberwiseClone();

    /// <summary>Crée un profil à partir des champs de connexion d'une <see cref="AppConfig"/>.</summary>
    public static ConnectionProfile FromConfig(string name, AppConfig c) => new()
    {
        Name = name,
        DbHost = c.DbHost, DbPort = c.DbPort, DbName = c.DbName, DbUser = c.DbUser, DbPassword = c.DbPassword,
        FtpHost = c.FtpHost, FtpPort = c.FtpPort, FtpUser = c.FtpUser, FtpPassword = c.FtpPassword,
        FtpRootPath = c.FtpRootPath, FtpUseTls = c.FtpUseTls
    };

    /// <summary>Applique ce profil aux champs de connexion d'une <see cref="AppConfig"/>.</summary>
    public void ApplyTo(AppConfig c)
    {
        c.DbHost = DbHost; c.DbPort = DbPort; c.DbName = DbName; c.DbUser = DbUser; c.DbPassword = DbPassword;
        c.FtpHost = FtpHost; c.FtpPort = FtpPort; c.FtpUser = FtpUser; c.FtpPassword = FtpPassword;
        c.FtpRootPath = FtpRootPath; c.FtpUseTls = FtpUseTls;
    }
}

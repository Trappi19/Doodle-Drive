namespace DoodleDrive.Models;

/// <summary>
/// Paramètres de connexion et préférences, persistés localement dans
/// <c>%AppData%\DoodleDrive\config.json</c>. Application personnelle : pas de chiffrement fort.
/// </summary>
public sealed class AppConfig
{
    // ----- MariaDB -----
    public string DbHost { get; set; } = "dalt.freeboxos.fr";
    public int DbPort { get; set; } = 13306;
    public string DbName { get; set; } = "cloud_perso";
    public string DbUser { get; set; } = string.Empty;
    public string DbPassword { get; set; } = string.Empty;

    // ----- FTP (disque dur sur la Freebox) -----
    public string FtpHost { get; set; } = "dalt.freeboxos.fr";
    public int FtpPort { get; set; } = 21;
    public string FtpUser { get; set; } = string.Empty;
    public string FtpPassword { get; set; } = string.Empty;

    /// <summary>Racine FTP à considérer comme la racine du drive (ex. "/Disque dur").</summary>
    public string FtpRootPath { get; set; } = "/";

    /// <summary>FTPS explicite si le serveur le supporte, sinon FTP simple.</summary>
    public bool FtpUseTls { get; set; }

    // ----- Session mémorisée (optionnelle) -----
    public bool RememberMe { get; set; }
    public string RememberedUsername { get; set; } = string.Empty;

    // ----- Préférences UI -----
    /// <summary>"System", "Light" ou "Dark".</summary>
    public string Theme { get; set; } = "System";

    /// <summary>"Grid" ou "List".</summary>
    public string DefaultView { get; set; } = "Grid";

    public string? LastDownloadFolder { get; set; }

    public AppConfig Clone() => (AppConfig)MemberwiseClone();
}

using System.IO;
using System.Text.Json;
using DoodleDrive.Models;

namespace DoodleDrive.Services;

/// <summary>Charge/enregistre <see cref="AppConfig"/> dans le dossier utilisateur.</summary>
public sealed class AppConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _directory;
    private readonly string _filePath;

    public AppConfigService()
    {
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DoodleDrive");
        _filePath = Path.Combine(_directory, "config.json");
        Current = Load();
    }

    /// <summary>Configuration active en mémoire.</summary>
    public AppConfig Current { get; private set; }

    private AppConfig Load()
    {
        var config = new AppConfig();
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
        }
        catch
        {
            // Config corrompue -> on repart sur les valeurs par défaut.
            config = new AppConfig();
        }

        // Les valeurs du fichier .env (identifiants privés, hors dépôt) ont priorité.
        ApplyEnvOverrides(config);
        return config;
    }

    /// <summary>
    /// Surcharge la config avec les clés définies dans un fichier <c>.env</c>.
    /// Seules les clés présentes et non vides écrasent la config existante, de sorte
    /// que le .env reste la source de vérité pour les identifiants qu'il définit.
    /// </summary>
    private static void ApplyEnvOverrides(AppConfig config)
    {
        var env = EnvFile.Load();
        if (env.Count == 0) return;

        string? Get(string key) => env.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

        if (Get("DOODLE_DB_HOST") is { } dbHost) config.DbHost = dbHost;
        if (Get("DOODLE_DB_PORT") is { } dbPort && int.TryParse(dbPort, out var dbPortValue)) config.DbPort = dbPortValue;
        if (Get("DOODLE_DB_NAME") is { } dbName) config.DbName = dbName;
        if (Get("DOODLE_DB_USER") is { } dbUser) config.DbUser = dbUser;
        if (Get("DOODLE_DB_PASSWORD") is { } dbPassword) config.DbPassword = dbPassword;

        if (Get("DOODLE_FTP_HOST") is { } ftpHost) config.FtpHost = ftpHost;
        if (Get("DOODLE_FTP_PORT") is { } ftpPort && int.TryParse(ftpPort, out var ftpPortValue)) config.FtpPort = ftpPortValue;
        if (Get("DOODLE_FTP_USER") is { } ftpUser) config.FtpUser = ftpUser;
        if (Get("DOODLE_FTP_PASSWORD") is { } ftpPassword) config.FtpPassword = ftpPassword;
        if (Get("DOODLE_FTP_ROOT") is { } ftpRoot) config.FtpRootPath = ftpRoot;
        if (Get("DOODLE_FTP_TLS") is { } ftpTls) config.FtpUseTls = ParseBool(ftpTls);
    }

    private static bool ParseBool(string value) =>
        value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "oui" or "on";

    public void Save(AppConfig config)
    {
        Current = config;
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(config, JsonOptions));
        }
        catch
        {
            // Écriture best-effort : une erreur d'enregistrement ne doit pas planter l'app.
        }
    }

    public void Save() => Save(Current);
}

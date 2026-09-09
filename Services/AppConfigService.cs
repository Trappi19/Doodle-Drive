using System.IO;
using System.Security.Cryptography;
using System.Text;
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

    /// <summary>Déclenché quand la connexion serveur active change (bascule, reset, import).</summary>
    public event Action? ConnectionChanged;

    /// <summary>Signale un changement de connexion (à appeler après modification des identifiants serveur).</summary>
    public void NotifyConnectionChanged() => ConnectionChanged?.Invoke();

    /// <summary>Noms des connexions enregistrées.</summary>
    public IReadOnlyList<string> ConnectionNames =>
        Current.Connections.Select(p => p.Name).ToList();

    private AppConfig Load()
    {
        var config = new AppConfig();
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                // Déchiffre les mots de passe stockés (rétrocompatible : un ancien
                // config.json en clair est accepté tel quel et re-chiffré au prochain Save).
                config.DbPassword = Unprotect(config.DbPassword);
                config.FtpPassword = Unprotect(config.FtpPassword);
                config.RememberedPassword = Unprotect(config.RememberedPassword);
                config.ApiToken = Unprotect(config.ApiToken);
                foreach (var profile in config.Connections)
                {
                    profile.DbPassword = Unprotect(profile.DbPassword);
                    profile.FtpPassword = Unprotect(profile.FtpPassword);
                }
            }
        }
        catch
        {
            // Config corrompue -> on repart sur les valeurs par défaut.
            config = new AppConfig();
        }

        // Le .env ne sert qu'à amorcer une config vierge (premier lancement en dev).
        // Une fois des identifiants enregistrés, il ne les écrase plus : sinon changer
        // de connexion serait annulé à chaque relance de l'app.
        if (string.IsNullOrWhiteSpace(config.DbUser) && string.IsNullOrWhiteSpace(config.DbPassword))
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
            // Chiffre les mots de passe avant écriture, sans altérer l'objet en mémoire.
            var toStore = config.Clone();
            toStore.DbPassword = Protect(config.DbPassword);
            toStore.FtpPassword = Protect(config.FtpPassword);
            toStore.RememberedPassword = Protect(config.RememberedPassword);
            toStore.ApiToken = Protect(config.ApiToken);
            // Copie profonde des connexions avec mots de passe chiffrés (Clone() est superficiel).
            toStore.Connections = config.Connections.Select(p =>
            {
                var q = p.Clone();
                q.DbPassword = Protect(p.DbPassword);
                q.FtpPassword = Protect(p.FtpPassword);
                return q;
            }).ToList();
            File.WriteAllText(_filePath, JsonSerializer.Serialize(toStore, JsonOptions));
        }
        catch
        {
            // Écriture best-effort : une erreur d'enregistrement ne doit pas planter l'app.
        }
    }

    public void Save() => Save(Current);

    /// <summary>
    /// Réinitialise les paramètres de connexion serveur (hôtes/ports par défaut, identifiants
    /// vidés) tout en conservant les préférences (thème, vue, session mémorisée). La machine
    /// se retrouve dans l'état « premier lancement » : re-saisie ou ré-import nécessaire.
    /// </summary>
    public void ResetServerConfig()
    {
        var c = Current;
        var defaults = new AppConfig();
        c.DbHost = defaults.DbHost; c.DbPort = defaults.DbPort; c.DbName = defaults.DbName;
        c.DbUser = defaults.DbUser; c.DbPassword = defaults.DbPassword;
        c.FtpHost = defaults.FtpHost; c.FtpPort = defaults.FtpPort; c.FtpUser = defaults.FtpUser;
        c.FtpPassword = defaults.FtpPassword; c.FtpRootPath = defaults.FtpRootPath; c.FtpUseTls = defaults.FtpUseTls;
        c.ActiveConnectionName = null;
        Save(c);
    }

    // ----- Connexions serveur enregistrées -----

    /// <summary>
    /// Enregistre les identifiants serveur courants sous un nom (crée ou remplace un profil
    /// existant du même nom), et en fait la connexion active.
    /// </summary>
    public void SaveCurrentAsConnection(string name)
    {
        name = name.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var c = Current;
        var profile = ConnectionProfile.FromConfig(name, c);
        var existing = c.Connections.FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) c.Connections[existing] = profile;
        else c.Connections.Add(profile);

        c.ActiveConnectionName = name;
        Save(c);
    }

    /// <summary>Bascule sur une connexion enregistrée : applique ses identifiants et la rend active.</summary>
    public bool ApplyConnection(string name)
    {
        var c = Current;
        var profile = c.Connections.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (profile is null) return false;

        profile.ApplyTo(c);
        c.ActiveConnectionName = profile.Name;
        Save(c);
        return true;
    }

    /// <summary>Supprime une connexion enregistrée (n'affecte pas les identifiants actuellement actifs).</summary>
    public void DeleteConnection(string name)
    {
        var c = Current;
        c.Connections.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(c.ActiveConnectionName, name, StringComparison.OrdinalIgnoreCase))
            c.ActiveConnectionName = null;
        Save(c);
    }

    // ----- Chiffrement local des identifiants (Windows DPAPI, portée utilisateur) -----
    // Les mots de passe sont chiffrés au repos dans config.json et liés au compte
    // Windows courant : copier le fichier sur une autre machine/compte le rend illisible.

    private const string EncPrefix = "enc:";

    /// <summary>Chiffre une valeur via DPAPI. Renvoie la valeur inchangée si vide ou déjà chiffrée.</summary>
    private static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain) || plain.StartsWith(EncPrefix, StringComparison.Ordinal))
            return plain;
        try
        {
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
            return EncPrefix + Convert.ToBase64String(bytes);
        }
        catch
        {
            // DPAPI indisponible : on conserve la valeur en clair plutôt que de la perdre.
            return plain;
        }
    }

    /// <summary>Déchiffre une valeur DPAPI. Une valeur sans préfixe (ancien format en clair) est renvoyée telle quelle.</summary>
    private static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(EncPrefix, StringComparison.Ordinal))
            return stored;
        try
        {
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(stored[EncPrefix.Length..]), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            // Chiffré pour un autre compte/machine : illisible ici, on repart à vide.
            return string.Empty;
        }
    }
}

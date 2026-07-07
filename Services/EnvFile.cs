using System.IO;

namespace DoodleDrive.Services;

/// <summary>
/// Petit lecteur de fichier <c>.env</c> (KEY=VALUE) sans dépendance externe.
/// Sert à fournir les identifiants privés (MariaDB / FTP) hors du dépôt git.
/// </summary>
public static class EnvFile
{
    /// <summary>
    /// Cherche un fichier <c>.env</c> (répertoire courant, dossier de l'exécutable,
    /// puis dossiers parents) et renvoie ses clés/valeurs. Vide si aucun trouvé.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Load()
    {
        var path = Locate();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (path is null) return result;

        try
        {
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
                    line = line["export ".Length..].TrimStart();

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();

                // Retire les guillemets englobants éventuels.
                if (value.Length >= 2 &&
                    ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                    value = value[1..^1];

                if (key.Length > 0)
                    result[key] = value;
            }
        }
        catch
        {
            // .env illisible : on l'ignore silencieusement.
        }

        return result;
    }

    private static string? Locate()
    {
        var candidates = new List<string>
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var start in candidates)
        {
            var dir = new DirectoryInfo(start);
            // Remonte jusqu'à 6 niveaux (utile en debug : bin/Debug/net10.0-windows/...).
            for (var i = 0; i < 6 && dir is not null; i++)
            {
                var candidate = Path.Combine(dir.FullName, ".env");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
        }

        return null;
    }
}

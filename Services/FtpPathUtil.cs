namespace DoodleDrive.Services;

/// <summary>Utilitaires de manipulation de chemins FTP (toujours des slashes avant, absolus).</summary>
public static class FtpPathUtil
{
    /// <summary>
    /// Préfixe technique masqué à l'affichage (ex. "/disques/ftpuser/SEAGATE 2TO"), remplacé par
    /// <see cref="DisplayRootLabel"/> (ex. "Home"). Purement cosmétique — les chemins réels
    /// envoyés à l'API restent complets. Définis au démarrage (App.OnStartup).
    /// </summary>
    public static string DisplayRoot = string.Empty;
    public static string DisplayRootLabel = string.Empty;

    /// <summary>Chemin « joli » pour l'utilisateur (ex. "Home/Sevan/Code/…").</summary>
    public static string ToDisplay(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var p = Normalize(path);
        var root = Normalize(DisplayRoot);
        if (!string.IsNullOrEmpty(DisplayRoot) && root != "/")
        {
            if (p == root) return string.IsNullOrEmpty(DisplayRootLabel) ? GetName(root) : DisplayRootLabel;
            if (p.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
            {
                var rest = p[(root.Length + 1)..];
                return string.IsNullOrEmpty(DisplayRootLabel) ? rest : $"{DisplayRootLabel}/{rest}";
            }
        }
        return p.TrimStart('/');
    }

    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "/";
        path = path.Replace('\\', '/').Trim();
        if (!path.StartsWith('/')) path = "/" + path;
        // Supprime les slashes redondants et le slash final (sauf racine).
        while (path.Contains("//")) path = path.Replace("//", "/");
        if (path.Length > 1 && path.EndsWith('/')) path = path.TrimEnd('/');
        return path;
    }

    public static string Combine(string parent, string name)
    {
        parent = Normalize(parent);
        name = name.Replace('\\', '/').Trim('/');
        return parent == "/" ? "/" + name : parent + "/" + name;
    }

    public static string GetParent(string path)
    {
        path = Normalize(path);
        if (path == "/") return "/";
        var idx = path.LastIndexOf('/');
        return idx <= 0 ? "/" : path[..idx];
    }

    public static string GetName(string path)
    {
        path = Normalize(path);
        if (path == "/") return "/";
        var idx = path.LastIndexOf('/');
        return path[(idx + 1)..];
    }

    /// <summary>Vrai si <paramref name="candidate"/> est égal ou descend de <paramref name="ancestor"/>.</summary>
    public static bool IsWithin(string candidate, string ancestor)
    {
        candidate = Normalize(candidate);
        ancestor = Normalize(ancestor);
        if (ancestor == "/") return true;
        return candidate == ancestor || candidate.StartsWith(ancestor + "/", StringComparison.Ordinal);
    }

    /// <summary>Segments du chemin pour construire un fil d'Ariane.</summary>
    public static IEnumerable<(string Name, string Path)> Breadcrumb(string path, string root)
    {
        root = Normalize(root);
        path = Normalize(path);
        yield return (root == "/" ? "Racine" : GetName(root), root);

        if (!IsWithin(path, root) || path == root) yield break;

        var relative = path[root.Length..].Trim('/');
        var current = root;
        foreach (var segment in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = Combine(current, segment);
            yield return (segment, current);
        }
    }
}

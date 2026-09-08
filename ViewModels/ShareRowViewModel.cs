using DoodleDrive.Models;

namespace DoodleDrive.ViewModels;

/// <summary>Une ligne de la liste « Mes partages » : un lien de partage avec ses infos calculées.</summary>
public sealed class ShareRowViewModel
{
    private readonly ShareLink _share;
    private readonly string _baseUrl;
    private readonly string _username;

    public ShareRowViewModel(ShareLink share, string baseUrl, string username)
    {
        _share = share;
        _baseUrl = baseUrl;
        _username = string.IsNullOrWhiteSpace(username) ? "user" : username;
    }

    public string Token => _share.Token;
    public string FileName => _share.FileName;
    public int ViewCount => _share.ViewCount;
    public bool IsDir => _share.IsDir;

    /// <summary>Glyphe Segoe Fluent : dossier (E8B7) ou fichier (E8A5).</summary>
    public string Glyph => _share.IsDir ? "\uE8B7" : "\uE8A5";

    public string ModeText => _share.IsDir
        ? (_share.Mode == "download" ? "Dossier (téléch.)" : "Dossier")
        : (_share.Mode == "download" ? "Téléchargement" : "Aperçu");

    public string CreatedText => _share.CreatedAt.ToString("dd/MM/yyyy HH:mm");

    public string ExpiryText => _share.ExpiresAt is { } e
        ? DateTime.SpecifyKind(e, DateTimeKind.Utc).ToLocalTime().ToString("dd/MM/yyyy HH:mm")
        : "Jamais";

    public bool IsRevoked => _share.Revoked;
    public bool IsExpired => _share.ExpiresAt is { } e && e < DateTime.UtcNow;
    public bool IsActive => !IsRevoked && !IsExpired;

    public string StatusText => IsRevoked ? "Révoqué" : IsExpired ? "Expiré" : "Actif";

    /// <summary>URL publique complète du partage (vide si l'URL de base n'est pas configurée).</summary>
    public string Url => string.IsNullOrWhiteSpace(_baseUrl)
        ? string.Empty
        : $"{_baseUrl.TrimEnd('/')}/u/{Uri.EscapeDataString(_username)}/{Uri.EscapeDataString(_share.Token)}";
}

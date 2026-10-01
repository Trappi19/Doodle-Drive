namespace DoodleDrive.Models;

/// <summary>Un lien de partage public enregistré en base (servi par le serveur de partage).</summary>
public sealed class ShareLink
{
    public string Token { get; init; } = string.Empty;
    public string FtpPath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;

    /// <summary>"download", "preview" ou "upload" (boîte de dépôt).</summary>
    public string Mode { get; init; } = "download";

    /// <summary>Vrai si le partage est un dossier (page web navigable) plutôt qu'un fichier.</summary>
    public bool IsDir { get; init; }

    public DateTime CreatedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public bool Revoked { get; init; }
    public int ViewCount { get; init; }

    /// <summary>Partage protégé par un mot de passe.</summary>
    public bool HasPassword { get; init; }

    /// <summary>Nombre max de téléchargements (null = illimité).</summary>
    public int? MaxDownloads { get; init; }

    public int DownloadCount { get; init; }
}

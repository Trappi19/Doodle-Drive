namespace DoodleDrive.Models;

/// <summary>Représente une ligne de la table <c>folders</c> (miroir logique de l'arborescence FTP).</summary>
public sealed class Folder
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>Chemin absolu du dossier sur le disque FTP (colonne <c>ftp_path</c>).</summary>
    public string FtpPath { get; init; } = string.Empty;

    public int? ParentId { get; init; }
    public int OwnerId { get; init; }
    public DateTime CreatedAt { get; init; }

    /// <summary>Nom du propriétaire, renseigné par certaines requêtes (jointure). Optionnel.</summary>
    public string? OwnerName { get; set; }
}

namespace DoodleDrive.ViewModels;

/// <summary>Un segment du fil d'Ariane (nom affiché + chemin FTP cible).</summary>
public sealed class BreadcrumbItem
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public bool IsLast { get; set; }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DoodleDrive.Models;

/// <summary>
/// Nœud d'arborescence affiché dans le panneau latéral. Enveloppe un <see cref="Folder"/>
/// avec son niveau d'accès effectif et ses enfants (résolus récursivement).
/// </summary>
public sealed partial class FolderNode : ObservableObject
{
    public FolderNode(Folder folder, FolderAccessLevel accessLevel)
    {
        Folder = folder;
        AccessLevel = accessLevel;
    }

    public Folder Folder { get; }

    public FolderAccessLevel AccessLevel { get; }

    public int Id => Folder.Id;
    public string Name => Folder.Name;
    public string FtpPath => Folder.FtpPath;
    public int? ParentId => Folder.ParentId;

    public ObservableCollection<FolderNode> Children { get; } = new();

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Glyphe Segoe Fluent Icons affichée dans l'arbre selon le niveau d'accès.</summary>
    public string Glyph => AccessLevel switch
    {
        FolderAccessLevel.Owner => "", // Folder
        FolderAccessLevel.Write => "", // NewFolder (partagé, écriture)
        _ => ""                        // Lock (partagé, lecture seule)
    };
}

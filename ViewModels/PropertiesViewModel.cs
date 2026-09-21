using DoodleDrive.Services;

namespace DoodleDrive.ViewModels;

/// <summary>Métadonnées d'un ou plusieurs éléments (fenêtre Propriétés).</summary>
public sealed class PropertiesViewModel
{
    public PropertiesViewModel(IReadOnlyList<FileEntryViewModel> items)
    {
        IsSingle = items.Count == 1;

        if (IsSingle)
        {
            var e = items[0];
            Title = e.Name;
            Glyph = e.Glyph;
            AccentBrushKey = e.AccentBrushKey;
            TypeText = e.KindText;
            SizeText = e.IsDirectory ? "—" : e.SizeText;
            ModifiedText = string.IsNullOrEmpty(e.ModifiedText) ? "—" : e.ModifiedText;
            LocationText = FtpPathUtil.GetParent(e.FullPath);
            FullPathText = e.FullPath;
        }
        else
        {
            var folders = items.Count(i => i.IsDirectory);
            var files = items.Count - folders;
            var totalBytes = items.Where(i => !i.IsDirectory).Sum(i => i.Size);

            Title = $"{items.Count} éléments sélectionnés";
            Glyph = ""; // dossier générique
            AccentBrushKey = "KindDefaultBrush";
            TypeText = $"{folders} dossier(s), {files} fichier(s)";
            SizeText = FileEntryViewModel.FormatSize(totalBytes) + (folders > 0 ? "  (contenu des dossiers non compté)" : "");
            ModifiedText = "—";
            LocationText = FtpPathUtil.GetParent(items[0].FullPath);
            FullPathText = string.Join("\n", items.Select(i => i.Name));
        }
    }

    public bool IsSingle { get; }
    public string Title { get; }
    public string Glyph { get; }
    public string AccentBrushKey { get; }
    public string TypeText { get; }
    public string SizeText { get; }
    public string ModifiedText { get; }
    public string LocationText { get; }
    public string FullPathText { get; }

    /// <summary>Libellé du champ « chemin » selon le mode (un élément = chemin complet, sinon la liste).</summary>
    public string FullPathLabel => IsSingle ? "Chemin complet" : "Éléments";
}

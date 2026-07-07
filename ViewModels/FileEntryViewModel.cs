using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using DoodleDrive.Models;
using DoodleDrive.Services;

namespace DoodleDrive.ViewModels;

/// <summary>Élément (fichier ou dossier) affiché dans la zone principale (grille ou liste).</summary>
public sealed partial class FileEntryViewModel : ObservableObject
{
    public FileEntryViewModel(RemoteEntry entry) => Entry = entry;

    public RemoteEntry Entry { get; }

    public string Name => Entry.Name;
    public string FullPath => Entry.FullPath;
    public bool IsDirectory => Entry.IsDirectory;
    public FileKind Kind => Entry.Kind;
    public DateTime Modified => Entry.Modified;
    public long Size => Entry.Size;

    public string Glyph => ThumbnailService.GetGlyph(Entry.Kind);
    public string AccentBrushKey => ThumbnailService.GetAccentBrushKey(Entry.Kind);

    public string SizeText => Entry.IsDirectory ? "Dossier" : FormatSize(Entry.Size);
    public string ModifiedText => Entry.Modified == default ? "" : Entry.Modified.ToString("dd/MM/yyyy HH:mm");
    public string KindText => Entry.IsDirectory ? "Dossier de fichiers" : DescribeKind(Entry.Kind, Entry.Extension);

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Vignette image chargée en tâche de fond (null = on affiche la glyphe typée).</summary>
    [ObservableProperty]
    private BitmapSource? _thumbnail;

    public bool CanHaveThumbnail => ThumbnailService.CanHaveImageThumbnail(Entry);

    public static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 o";
        string[] units = { "o", "Ko", "Mo", "Go", "To" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} o" : $"{size:0.#} {units[unit]}";
    }

    private static string DescribeKind(FileKind kind, string ext)
    {
        var label = kind switch
        {
            FileKind.Image => "Image",
            FileKind.Video => "Vidéo",
            FileKind.Audio => "Audio",
            FileKind.Pdf => "Document PDF",
            FileKind.Document => "Document",
            FileKind.Spreadsheet => "Feuille de calcul",
            FileKind.Presentation => "Présentation",
            FileKind.Archive => "Archive",
            FileKind.Code => "Code source",
            FileKind.Text => "Texte",
            _ => "Fichier"
        };
        return string.IsNullOrEmpty(ext) ? label : $"{label} ({ext.ToUpperInvariant()})";
    }
}

using System.IO;

namespace DoodleDrive.Models;

/// <summary>
/// Élément listé dans un dossier FTP (sous-dossier ou fichier). Correspond à un
/// <c>FtpListItem</c> de FluentFTP, transformé pour l'affichage.
/// </summary>
public sealed class RemoteEntry
{
    public required string Name { get; init; }

    /// <summary>Chemin absolu FTP complet (dossier parent + nom).</summary>
    public required string FullPath { get; init; }

    public bool IsDirectory { get; init; }

    /// <summary>Taille en octets (0 pour les dossiers).</summary>
    public long Size { get; init; }

    public DateTime Modified { get; init; }

    public FileKind Kind => IsDirectory ? FileKind.Folder : ClassifyExtension(Name);

    public string Extension => IsDirectory ? string.Empty : Path.GetExtension(Name).TrimStart('.').ToLowerInvariant();

    public static FileKind ClassifyExtension(string fileName)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "jpg" or "jpeg" or "png" or "gif" or "bmp" or "webp" or "tiff" or "tif" or "ico" or "heic" or "svg" => FileKind.Image,
            "mp4" or "mkv" or "avi" or "mov" or "wmv" or "flv" or "webm" or "m4v" or "mpg" or "mpeg" or "3gp" => FileKind.Video,
            "mp3" or "wav" or "flac" or "aac" or "ogg" or "wma" or "m4a" or "opus" => FileKind.Audio,
            "pdf" => FileKind.Pdf,
            "doc" or "docx" or "odt" or "rtf" or "pages" => FileKind.Document,
            "xls" or "xlsx" or "ods" or "csv" or "tsv" => FileKind.Spreadsheet,
            "ppt" or "pptx" or "odp" or "key" => FileKind.Presentation,
            "zip" or "rar" or "7z" or "tar" or "gz" or "bz2" or "xz" or "iso" => FileKind.Archive,
            "cs" or "js" or "ts" or "py" or "java" or "cpp" or "c" or "h" or "html" or "css" or "xml" or "json" or "sql" or "sh" or "ps1" => FileKind.Code,
            "txt" or "md" or "log" or "ini" or "cfg" or "yml" or "yaml" => FileKind.Text,
            _ => FileKind.Unknown
        };
    }
}

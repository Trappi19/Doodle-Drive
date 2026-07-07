using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using DoodleDrive.Models;

namespace DoodleDrive.Services;

/// <summary>
/// Fournit les glyphes de type de fichier et génère (à la demande, en tâche de fond)
/// des vignettes pour les images. Vidéos/PDF : glyphe typée pour rester léger et fluide.
/// </summary>
public sealed class ThumbnailService
{
    private readonly FtpService _ftp;
    private readonly string _cacheDir;
    private readonly SemaphoreSlim _throttle = new(3); // limite les téléchargements simultanés

    // Au-delà de cette taille, on n'essaie pas de générer une vignette image (trop lourd en FTP).
    private const long MaxImageBytesForThumbnail = 25 * 1024 * 1024;

    public ThumbnailService(FtpService ftp)
    {
        _ftp = ftp;
        _cacheDir = Path.Combine(Path.GetTempPath(), "DoodleDrive", "thumbs");
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>Glyphe Segoe Fluent Icons représentant le type de fichier.</summary>
    public static string GetGlyph(FileKind kind) => kind switch
    {
        FileKind.Folder => "",
        FileKind.Image => "",
        FileKind.Video => "",
        FileKind.Audio => "",
        FileKind.Pdf => "",
        FileKind.Document => "",
        FileKind.Spreadsheet => "",
        FileKind.Presentation => "",
        FileKind.Archive => "",
        FileKind.Code => "",
        FileKind.Text => "",
        _ => ""
    };

    /// <summary>Clé de ressource pinceau (définie dans App.xaml) pour la couleur d'accent du type.</summary>
    public static string GetAccentBrushKey(FileKind kind) => kind switch
    {
        FileKind.Image => "KindImageBrush",
        FileKind.Video => "KindVideoBrush",
        FileKind.Audio => "KindAudioBrush",
        FileKind.Pdf => "KindPdfBrush",
        FileKind.Document => "KindDocumentBrush",
        FileKind.Spreadsheet => "KindSpreadsheetBrush",
        FileKind.Presentation => "KindPresentationBrush",
        FileKind.Archive => "KindArchiveBrush",
        FileKind.Code => "KindCodeBrush",
        FileKind.Folder => "KindFolderBrush",
        _ => "KindDefaultBrush"
    };

    public static bool CanHaveImageThumbnail(RemoteEntry entry) =>
        entry.Kind == FileKind.Image
        && entry.Extension is not "svg" // format vectoriel non décodable par BitmapImage
        && entry.Size is > 0 and <= MaxImageBytesForThumbnail;

    /// <summary>
    /// Télécharge (avec cache disque) et décode une vignette image réduite. Renvoie null en cas d'échec.
    /// L'objet retourné est figé (Freeze) donc utilisable depuis le thread UI.
    /// </summary>
    public async Task<BitmapSource?> LoadImageThumbnailAsync(RemoteEntry entry, int decodePixelWidth = 220, CancellationToken ct = default)
    {
        if (!CanHaveImageThumbnail(entry)) return null;

        var cacheFile = Path.Combine(_cacheDir, CacheKey(entry));

        await _throttle.WaitAsync(ct);
        try
        {
            if (!File.Exists(cacheFile))
            {
                var ok = await _ftp.DownloadAsync(entry.FullPath, cacheFile, progress: null, ct);
                if (!ok) return null;
            }

            return await Task.Run(() => Decode(cacheFile, decodePixelWidth), ct);
        }
        catch
        {
            return null;
        }
        finally
        {
            _throttle.Release();
        }
    }

    private static BitmapSource? Decode(string path, int decodePixelWidth)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.DecodePixelWidth = decodePixelWidth;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static string CacheKey(RemoteEntry entry)
    {
        var raw = $"{entry.FullPath}|{entry.Size}|{entry.Modified.Ticks}";
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash) + ".img";
    }
}

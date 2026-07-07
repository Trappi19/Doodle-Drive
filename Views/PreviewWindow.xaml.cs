using System.IO;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;

namespace DoodleDrive.Views;

public partial class PreviewWindow : FluentWindow
{
    public PreviewWindow(string title, string localPath)
    {
        InitializeComponent();
        Title = title;
        Bar.Title = title;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.UriSource = new Uri(localPath);
            bmp.EndInit();
            bmp.Freeze();
            PreviewImage.Source = bmp;
        }
        catch
        {
            // Image illisible : la fenêtre reste vide plutôt que de planter.
            if (File.Exists(localPath)) { /* format non supporté */ }
        }
    }
}

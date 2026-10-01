using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace DoodleDrive.Views.Dialogs;

public partial class ShareDialog : FluentWindow
{
    public ShareDialog(string fileName, bool isDirectory = false)
    {
        InitializeComponent();
        FileNameText.Text = isDirectory ? $"📁 {fileName}" : fileName;

        if (isDirectory)
        {
            Title = "Partager un dossier";
            PreviewRadio.Content = "Consulter uniquement (parcourir et voir les fichiers)";
            DownloadRadio.Content = "Autoriser le téléchargement des fichiers";
            UploadRadio.Visibility = Visibility.Visible;
        }
    }

    /// <summary>"preview", "download" ou "upload" (boîte de dépôt, dossiers uniquement).</summary>
    public string Mode => DownloadRadio.IsChecked == true ? "download"
        : UploadRadio.IsChecked == true ? "upload" : "preview";

    /// <summary>Mot de passe du lien, ou null si non protégé.</summary>
    public string? Password => PasswordCheck.IsChecked == true && PasswordInput.Password.Length > 0
        ? PasswordInput.Password : null;

    /// <summary>Nombre max de téléchargements (mode téléchargement uniquement), null = illimité.</summary>
    public int? MaxDownloads =>
        Mode == "download" && int.TryParse((MaxBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var n) && n > 0
            ? n : null;

    private void Mode_OnChanged(object sender, RoutedEventArgs e)
    {
        var dl = DownloadRadio.IsChecked == true;
        MaxBox.IsEnabled = dl;
        MaxLabel.Opacity = dl ? 1 : 0.5;
    }

    private void Password_OnToggled(object sender, RoutedEventArgs e)
    {
        var on = PasswordCheck.IsChecked == true;
        PasswordInput.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        PasswordError.Visibility = Visibility.Collapsed;
        if (on) PasswordInput.Focus();
    }

    /// <summary>Date d'expiration en UTC, ou null si « Jamais ».</summary>
    public DateTime? ExpiresAtUtc
    {
        get
        {
            var days = (ExpiryBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (string.IsNullOrEmpty(days) || days == "0") return null;
            if (days == "1") return DateTime.UtcNow.AddHours(24);
            return int.TryParse(days, out var d) ? DateTime.UtcNow.AddDays(d) : null;
        }
    }

    private void Ok_OnClick(object sender, RoutedEventArgs e)
    {
        if (PasswordCheck.IsChecked == true && PasswordInput.Password.Length < 4)
        {
            PasswordError.Visibility = Visibility.Visible;
            PasswordInput.Focus();
            return;
        }
        DialogResult = true;
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

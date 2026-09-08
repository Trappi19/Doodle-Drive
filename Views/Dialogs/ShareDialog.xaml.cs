using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace DoodleDrive.Views.Dialogs;

public partial class ShareDialog : FluentWindow
{
    public ShareDialog(string fileName)
    {
        InitializeComponent();
        FileNameText.Text = fileName;
    }

    /// <summary>"preview" ou "download".</summary>
    public string Mode => DownloadRadio.IsChecked == true ? "download" : "preview";

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
        DialogResult = true;
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

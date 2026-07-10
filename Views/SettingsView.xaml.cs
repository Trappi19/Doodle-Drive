using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using DoodleDrive.ViewModels;

namespace DoodleDrive.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private void OnExportProfileClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;

        var dialog = new SaveFileDialog
        {
            Title = "Exporter mon profil (configuration chiffrée)",
            Filter = "Configuration Doodle Drive (*.ddconfig)|*.ddconfig",
            FileName = "DoodleDrive.ddconfig"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            vm.ExportProfile(dialog.FileName);
    }
}

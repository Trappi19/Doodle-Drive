using System.Windows;
using Microsoft.Win32;
using DoodleDrive.ViewModels;
using Wpf.Ui.Controls;

namespace DoodleDrive.Views;

public partial class LoginWindow : FluentWindow
{
    public LoginWindow()
    {
        InitializeComponent();

        // Glisser-déposer d'un fichier de configuration (.ddconfig / .env) sur la fenêtre.
        AllowDrop = true;
        DragOver += OnDragOver;
        Drop += OnDrop;
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not LoginViewModel vm) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            vm.ImportFromFile(files[0]);
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LoginViewModel vm) return;

        var dialog = new OpenFileDialog
        {
            Title = "Importer une configuration serveur",
            Filter = "Configuration Doodle Drive (*.ddconfig;*.env)|*.ddconfig;*.env|Tous les fichiers (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
            vm.ImportFromFile(dialog.FileName);
    }
}

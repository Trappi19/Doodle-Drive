using System.Windows;
using System.Windows.Controls;
using DoodleDrive.Models;
using DoodleDrive.Services;
using DoodleDrive.ViewModels;
using Wpf.Ui.Controls;

namespace DoodleDrive.Views.Dialogs;

public partial class RemoteFolderPickerDialog : FluentWindow
{
    private readonly RemoteFolderPickerViewModel _vm;

    public RemoteFolderPickerDialog(FtpService ftp, string rootPath, string localFolderName)
    {
        InitializeComponent();
        _vm = new RemoteFolderPickerViewModel(ftp, rootPath, localFolderName);
        DataContext = _vm;
        Loaded += async (_, _) => await _vm.LoadAsync();
    }

    /// <summary>Chemin en ligne choisi (sous-dossier compris si l'option est cochée). Valide après OK.</summary>
    public string? ResolvedPath => _vm.ResolvedPath;

    private void Tree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FolderNode node && !node.IsPlaceholder)
            _vm.SelectedNode = node;
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

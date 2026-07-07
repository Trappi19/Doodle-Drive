using System.Windows;
using DoodleDrive.ViewModels;
using Wpf.Ui.Controls;

namespace DoodleDrive.Views.Dialogs;

public partial class PermissionsDialog : FluentWindow
{
    public PermissionsDialog(PermissionsDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}

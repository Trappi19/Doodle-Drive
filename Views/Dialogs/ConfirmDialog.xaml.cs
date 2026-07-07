using System.Windows;
using Wpf.Ui.Controls;

namespace DoodleDrive.Views.Dialogs;

public partial class ConfirmDialog : FluentWindow
{
    public ConfirmDialog(string title, string message, string okText = "Confirmer", bool destructive = false)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        OkButton.Content = okText;
        OkButton.Appearance = destructive ? ControlAppearance.Danger : ControlAppearance.Primary;
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

using System.Windows;
using Wpf.Ui.Controls;

namespace DoodleDrive.Views.Dialogs;

public partial class InputDialog : FluentWindow
{
    public InputDialog(string title, string message, string defaultValue = "", string okText = "Valider")
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        ValueBox.Text = defaultValue;
        OkButton.Content = okText;

        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string Value => ValueBox.Text?.Trim() ?? string.Empty;

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

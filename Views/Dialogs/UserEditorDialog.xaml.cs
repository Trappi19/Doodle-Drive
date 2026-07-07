using System.Windows;
using DoodleDrive.Models;
using Wpf.Ui.Controls;

namespace DoodleDrive.Views.Dialogs;

public enum UserEditorMode { Create, ResetPassword }

public partial class UserEditorDialog : FluentWindow
{
    private readonly UserEditorMode _mode;

    public UserEditorDialog(UserEditorMode mode)
    {
        InitializeComponent();
        _mode = mode;

        if (mode == UserEditorMode.ResetPassword)
        {
            HeaderText.Text = "Réinitialiser le mot de passe";
            Bar.Title = "Réinitialiser le mot de passe";
            UsernamePanel.Visibility = Visibility.Collapsed;
            RolePanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            HeaderText.Text = "Nouveau compte";
        }

        Loaded += (_, _) =>
        {
            if (mode == UserEditorMode.Create) UsernameBox.Focus();
            else PasswordBox.Focus();
        };
    }

    /// <summary>Affiché pour information en mode réinitialisation (nom du compte ciblé).</summary>
    public string PresetUsername
    {
        set => HeaderText.Text = $"Réinitialiser le mot de passe de « {value} »";
    }

    public string Username => UsernameBox.Text?.Trim() ?? string.Empty;
    public string Password => PasswordBox.Password;
    public UserRole Role => RoleBox.SelectedIndex == 1 ? UserRole.Admin : UserRole.User;

    private void Ok_OnClick(object sender, RoutedEventArgs e)
    {
        if (_mode == UserEditorMode.Create && string.IsNullOrWhiteSpace(Username))
        {
            UsernameBox.Focus();
            return;
        }
        if (string.IsNullOrEmpty(Password))
        {
            PasswordBox.Focus();
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

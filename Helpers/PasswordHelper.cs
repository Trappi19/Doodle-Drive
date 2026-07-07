using System.Windows;
using System.Windows.Controls;

namespace DoodleDrive.Helpers;

/// <summary>
/// Permet de lier (MVVM) la propriété Password d'un PasswordBox standard, qui n'est pas
/// bindable nativement. Usage :
/// <c>&lt;PasswordBox h:PasswordHelper.BindPassword="True"
///     h:PasswordHelper.BoundPassword="{Binding Pwd, Mode=TwoWay}" /&gt;</c>
/// </summary>
public static class PasswordHelper
{
    public static readonly DependencyProperty BoundPasswordProperty = DependencyProperty.RegisterAttached(
        "BoundPassword", typeof(string), typeof(PasswordHelper),
        new FrameworkPropertyMetadata(string.Empty, OnBoundPasswordChanged));

    public static readonly DependencyProperty BindPasswordProperty = DependencyProperty.RegisterAttached(
        "BindPassword", typeof(bool), typeof(PasswordHelper),
        new PropertyMetadata(false, OnBindPasswordChanged));

    private static readonly DependencyProperty UpdatingProperty = DependencyProperty.RegisterAttached(
        "Updating", typeof(bool), typeof(PasswordHelper), new PropertyMetadata(false));

    public static string GetBoundPassword(DependencyObject d) => (string)d.GetValue(BoundPasswordProperty);
    public static void SetBoundPassword(DependencyObject d, string value) => d.SetValue(BoundPasswordProperty, value);

    public static bool GetBindPassword(DependencyObject d) => (bool)d.GetValue(BindPasswordProperty);
    public static void SetBindPassword(DependencyObject d, bool value) => d.SetValue(BindPasswordProperty, value);

    private static bool GetUpdating(DependencyObject d) => (bool)d.GetValue(UpdatingProperty);
    private static void SetUpdating(DependencyObject d, bool value) => d.SetValue(UpdatingProperty, value);

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;
        if (!GetBindPassword(box)) return;
        if (GetUpdating(box)) return;

        box.Password = e.NewValue as string ?? string.Empty;
    }

    private static void OnBindPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;

        if ((bool)e.OldValue) box.PasswordChanged -= HandlePasswordChanged;
        if ((bool)e.NewValue) box.PasswordChanged += HandlePasswordChanged;
    }

    private static void HandlePasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox box) return;
        SetUpdating(box, true);
        SetBoundPassword(box, box.Password);
        SetUpdating(box, false);
    }
}

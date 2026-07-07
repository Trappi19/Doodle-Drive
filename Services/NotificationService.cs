using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace DoodleDrive.Services;

public enum ToastType { Info, Success, Warning, Error }

public sealed class Toast
{
    public required string Title { get; init; }
    public string? Message { get; init; }
    public ToastType Type { get; init; }

    public string Glyph => Type switch
    {
        ToastType.Success => "",  // CheckmarkCircle
        ToastType.Warning => "",  // Warning
        ToastType.Error => "",     // ErrorBadge
        _ => ""                     // Info
    };

    public string AccentBrushKey => Type switch
    {
        ToastType.Success => "ToastSuccessBrush",
        ToastType.Warning => "ToastWarningBrush",
        ToastType.Error => "ToastErrorBrush",
        _ => "ToastInfoBrush"
    };
}

/// <summary>
/// Notifications non bloquantes affichées en bandeau in-app. Thread-safe :
/// les appels sont marshalés vers le thread UI.
/// </summary>
public sealed class NotificationService
{
    public ObservableCollection<Toast> Toasts { get; } = new();

    public void Info(string title, string? message = null) => Show(title, message, ToastType.Info);
    public void Success(string title, string? message = null) => Show(title, message, ToastType.Success);
    public void Warning(string title, string? message = null) => Show(title, message, ToastType.Warning);
    public void Error(string title, string? message = null) => Show(title, message, ToastType.Error);

    public void Show(string title, string? message, ToastType type, int durationMs = 4500)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;

        if (dispatcher.CheckAccess())
            Add(title, message, type, durationMs);
        else
            dispatcher.Invoke(() => Add(title, message, type, durationMs));
    }

    private void Add(string title, string? message, ToastType type, int durationMs)
    {
        var toast = new Toast { Title = title, Message = message, Type = type };
        Toasts.Add(toast);
        if (Toasts.Count > 5)
            Toasts.RemoveAt(0);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Toasts.Remove(toast);
        };
        timer.Start();
    }

    public void Dismiss(Toast toast) => Toasts.Remove(toast);
}

using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace DoodleDrive.Services;

/// <summary>
/// Icône de la zone de notification : fermer la fenêtre principale réduit l'app en
/// tâche de fond au lieu de la quitter (menu de l'icône ou « Quitter » pour arrêter).
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public TrayService()
    {
        _icon = new WinForms.NotifyIcon
        {
            Text = "Doodle Drive",
            Icon = LoadAppIcon(),
            Visible = false
        };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Ouvrir Doodle Drive", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => ExitRequested?.Invoke());
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    private static Drawing.Icon LoadAppIcon()
    {
        try
        {
            if (Environment.ProcessPath is { } exe)
                return Drawing.Icon.ExtractAssociatedIcon(exe) ?? Drawing.SystemIcons.Application;
        }
        catch
        {
            // Icône système en secours.
        }
        return Drawing.SystemIcons.Application;
    }

    public void Show() => _icon.Visible = true;

    /// <summary>Info-bulle affichée la première fois que la fenêtre part en tâche de fond.</summary>
    public void ShowBackgroundHint()
    {
        _icon.BalloonTipTitle = "Doodle Drive";
        _icon.BalloonTipText = "L'application continue en tâche de fond. Clic droit sur l'icône pour quitter.";
        _icon.ShowBalloonTip(3000);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}

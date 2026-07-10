using Microsoft.Win32;

namespace DoodleDrive.Services;

/// <summary>Lancement automatique à l'ouverture de session Windows (clé Run de l'utilisateur).</summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DoodleDrive";

    /// <summary>Argument passé par le lancement automatique, pour distinguer d'un lancement manuel.</summary>
    public const string StartupArgument = "--startup";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            var exe = Environment.ProcessPath
                ?? throw new InvalidOperationException("Chemin de l'exécutable introuvable.");
            key.SetValue(ValueName, $"\"{exe}\" {StartupArgument}");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}

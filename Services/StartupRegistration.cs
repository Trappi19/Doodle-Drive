using Microsoft.Win32;

namespace DoodleDrive.Services;

/// <summary>
/// Lancement automatique à l'ouverture de session Windows (clé Run de l'utilisateur).
/// Reste cohérent avec le Gestionnaire des tâches : celui-ci écrit un « override »
/// (StartupApproved) quand on désactive une entrée — on le lit et on l'efface au besoin.
/// </summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "DoodleDrive";

    /// <summary>Argument passé par le lancement automatique, pour distinguer d'un lancement manuel.</summary>
    public const string StartupArgument = "--startup";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            if (key?.GetValue(ValueName) is not string) return false;
            // Désactivé depuis le Gestionnaire des tâches ? (1er octet impair = désactivé)
            return !IsDisabledByTaskManager();
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
            ClearTaskManagerOverride(); // au cas où le Gestionnaire des tâches l'avait désactivé
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            ClearTaskManagerOverride();
        }
    }

    /// <summary>Vrai si le Gestionnaire des tâches a désactivé l'entrée (override StartupApproved).</summary>
    private static bool IsDisabledByTaskManager()
    {
        try
        {
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath);
            if (approved?.GetValue(ValueName) is byte[] data && data.Length > 0)
                return (data[0] & 1) == 1; // 0x03… = désactivé, 0x02… = activé
        }
        catch { /* pas d'override : considéré activé */ }
        return false;
    }

    private static void ClearTaskManagerOverride()
    {
        try
        {
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: true);
            approved?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch { /* best-effort */ }
    }
}

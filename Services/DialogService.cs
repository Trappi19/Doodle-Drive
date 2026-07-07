using System.IO;
using System.Windows;
using Microsoft.Win32;
using DoodleDrive.Views.Dialogs;

namespace DoodleDrive.Services;

/// <summary>Boîtes de dialogue (confirmation, saisie) et sélecteurs de fichiers/dossiers Windows.</summary>
public sealed class DialogService
{
    private static Window? Owner => Application.Current?.Windows
        .OfType<Window>()
        .FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;

    public bool Confirm(string title, string message, string okText = "Confirmer", bool destructive = false)
    {
        var dialog = new ConfirmDialog(title, message, okText, destructive) { Owner = Owner };
        return dialog.ShowDialog() == true;
    }

    /// <summary>Demande une saisie texte. Renvoie null si annulé.</summary>
    public string? Prompt(string title, string message, string defaultValue = "", string okText = "Valider")
    {
        var dialog = new InputDialog(title, message, defaultValue, okText) { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    public string[]? PickFilesToUpload()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Sélectionner les fichiers à envoyer",
            Multiselect = true,
            CheckFileExists = true
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileNames : null;
    }

    public string? PickDownloadFolder(string? initialDir = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choisir le dossier de destination"
        };
        if (!string.IsNullOrWhiteSpace(initialDir) && Directory.Exists(initialDir))
            dialog.InitialDirectory = initialDir;

        return dialog.ShowDialog(Owner) == true ? dialog.FolderName : null;
    }
}

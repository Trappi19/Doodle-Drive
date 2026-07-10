using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoodleDrive.Models;
using DoodleDrive.Services;
using DoodleDrive.Views.Dialogs;

namespace DoodleDrive.ViewModels;

/// <summary>Panneau administrateur : CRUD des comptes et gestion de tous les dossiers.</summary>
public sealed partial class AdminViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly DialogService _dialogs;
    private readonly NotificationService _notify;
    private readonly Session _session;

    public AdminViewModel(DatabaseService db, DialogService dialogs, NotificationService notify, Session session)
    {
        _db = db;
        _dialogs = dialogs;
        _notify = notify;
        _session = session;

        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        CreateUserCommand = new AsyncRelayCommand(CreateUserAsync, () => !IsBusy);
        SetDefaultPathCommand = new AsyncRelayCommand<User?>(SetDefaultPathAsync);
        ResetPasswordCommand = new AsyncRelayCommand<User?>(ResetPasswordAsync);
        ToggleRoleCommand = new AsyncRelayCommand<User?>(ToggleRoleAsync);
        DeleteUserCommand = new AsyncRelayCommand<User?>(DeleteUserAsync);
        DeleteFolderCommand = new AsyncRelayCommand<Folder?>(DeleteFolderAsync);
        ManageFolderAccessCommand = new AsyncRelayCommand<Folder?>(ManageFolderAccessAsync);
    }

    public ObservableCollection<User> Users { get; } = new();
    public ObservableCollection<Folder> Folders { get; } = new();

    [ObservableProperty] private bool _isBusy;

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand CreateUserCommand { get; }
    public AsyncRelayCommand<User?> SetDefaultPathCommand { get; }
    public AsyncRelayCommand<User?> ResetPasswordCommand { get; }
    public AsyncRelayCommand<User?> ToggleRoleCommand { get; }
    public AsyncRelayCommand<User?> DeleteUserCommand { get; }
    public AsyncRelayCommand<Folder?> DeleteFolderCommand { get; }
    public AsyncRelayCommand<Folder?> ManageFolderAccessCommand { get; }

    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        CreateUserCommand.NotifyCanExecuteChanged();
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Users.Clear();
            foreach (var u in await _db.GetAllUsersAsync()) Users.Add(u);

            Folders.Clear();
            foreach (var f in await _db.GetAllFoldersAsync()) Folders.Add(f);
        }
        catch (Exception ex)
        {
            _notify.Error("Chargement admin impossible", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CreateUserAsync()
    {
        var dialog = new UserEditorDialog(UserEditorMode.Create);
        App.SetOwner(dialog);
        if (dialog.ShowDialog() != true) return;

        var username = dialog.Username.Trim();
        var password = dialog.Password;
        var role = dialog.Role;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            _notify.Warning("Champs manquants", "Identifiant et mot de passe requis.");
            return;
        }

        try
        {
            IsBusy = true;
            if (await _db.UsernameExistsAsync(username))
            {
                _notify.Warning("Identifiant pris", $"« {username} » existe déjà.");
                return;
            }

            await _db.CreateUserAsync(username, AuthService.HashPassword(password), role);
            _notify.Success("Compte créé", username);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _notify.Error("Création impossible", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Chemin d'atterrissage attribué à l'utilisateur à la connexion (priorité :
    /// dernier chemin visité, puis ce chemin par défaut, puis l'automatique).
    /// Saisie vide = retour au comportement automatique.
    /// </summary>
    private async Task SetDefaultPathAsync(User? user)
    {
        if (user is null) return;
        var input = _dialogs.Prompt(
            "Chemin par défaut",
            $"Dossier d'atterrissage de « {user.Username} » à la connexion (vide = automatique) :",
            user.DefaultPath ?? string.Empty);
        if (input is null) return; // annulé

        var normalized = string.IsNullOrWhiteSpace(input) ? null : FtpPathUtil.Normalize(input);
        try
        {
            await _db.UpdateUserDefaultPathAsync(user.Id, normalized);
            if (normalized is null)
                _notify.Success("Chemin par défaut retiré", user.Username);
            else
                _notify.Success("Chemin par défaut défini", $"{user.Username} → {normalized}");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _notify.Error("Mise à jour impossible", ex.Message);
        }
    }

    private async Task ResetPasswordAsync(User? user)
    {
        if (user is null) return;
        var dialog = new UserEditorDialog(UserEditorMode.ResetPassword) { PresetUsername = user.Username };
        App.SetOwner(dialog);
        if (dialog.ShowDialog() != true) return;

        if (string.IsNullOrEmpty(dialog.Password))
        {
            _notify.Warning("Mot de passe vide", "Aucun changement effectué.");
            return;
        }

        try
        {
            await _db.UpdateUserPasswordAsync(user.Id, AuthService.HashPassword(dialog.Password));
            _notify.Success("Mot de passe réinitialisé", user.Username);
        }
        catch (Exception ex)
        {
            _notify.Error("Réinitialisation impossible", ex.Message);
        }
    }

    private async Task ToggleRoleAsync(User? user)
    {
        if (user is null) return;
        var newRole = user.Role == UserRole.Admin ? UserRole.User : UserRole.Admin;

        if (user.Role == UserRole.Admin && await _db.GetAdminCountAsync() <= 1)
        {
            _notify.Warning("Action bloquée", "Il doit rester au moins un administrateur.");
            return;
        }

        var verb = newRole == UserRole.Admin ? "promouvoir administrateur" : "rétrograder utilisateur";
        if (!_dialogs.Confirm("Changer le rôle", $"Voulez-vous {verb} « {user.Username} » ?"))
            return;

        try
        {
            await _db.UpdateUserRoleAsync(user.Id, newRole);
            _notify.Success("Rôle mis à jour", $"{user.Username} → {(newRole == UserRole.Admin ? "admin" : "user")}");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _notify.Error("Mise à jour impossible", ex.Message);
        }
    }

    private async Task DeleteUserAsync(User? user)
    {
        if (user is null) return;
        if (user.Id == _session.UserId)
        {
            _notify.Warning("Action bloquée", "Vous ne pouvez pas supprimer votre propre compte.");
            return;
        }
        if (user.Role == UserRole.Admin && await _db.GetAdminCountAsync() <= 1)
        {
            _notify.Warning("Action bloquée", "Il doit rester au moins un administrateur.");
            return;
        }

        if (!_dialogs.Confirm("Supprimer le compte",
                $"Supprimer « {user.Username} » ? Ses dossiers et partages seront également supprimés (CASCADE).",
                "Supprimer", destructive: true))
            return;

        try
        {
            await _db.DeleteUserAsync(user.Id);
            _notify.Success("Compte supprimé", user.Username);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _notify.Error("Suppression impossible", ex.Message);
        }
    }

    private async Task DeleteFolderAsync(Folder? folder)
    {
        if (folder is null) return;
        if (!_dialogs.Confirm("Retirer le dossier",
                $"Retirer « {folder.Name} » de la base (et ses sous-dossiers/partages) ?\n\n" +
                "Les fichiers sur le disque FTP ne sont pas supprimés.",
                "Retirer", destructive: true))
            return;

        try
        {
            await _db.DeleteFolderAsync(folder.Id);
            _notify.Success("Dossier retiré de la base", folder.Name);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _notify.Error("Suppression impossible", ex.Message);
        }
    }

    private async Task ManageFolderAccessAsync(Folder? folder)
    {
        if (folder is null) return;
        var vm = new PermissionsDialogViewModel(_db, _notify, _session, folder);
        await vm.LoadAsync();
        var dialog = new PermissionsDialog(vm);
        App.SetOwner(dialog);
        dialog.ShowDialog();
    }
}

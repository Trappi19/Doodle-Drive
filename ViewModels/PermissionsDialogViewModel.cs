using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoodleDrive.Models;
using DoodleDrive.Services;

namespace DoodleDrive.ViewModels;

public sealed partial class PermissionRow : ObservableObject
{
    public PermissionRow(User user, PermissionLevel level)
    {
        User = user;
        _level = level;
    }

    public User User { get; }
    public string Username => User.Username;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LevelLabel))]
    private PermissionLevel _level;

    public string LevelLabel => Level == PermissionLevel.Write ? "Écriture" : "Lecture seule";
}

/// <summary>Gestion des accès partagés d'un dossier (propriétaire ou admin uniquement).</summary>
public sealed partial class PermissionsDialogViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly NotificationService _notify;
    private readonly Session _session;
    private readonly Folder _folder;

    public PermissionsDialogViewModel(DatabaseService db, NotificationService notify, Session session, Folder folder)
    {
        _db = db;
        _notify = notify;
        _session = session;
        _folder = folder;

        AddCommand = new AsyncRelayCommand(AddAsync, () => SelectedUserToAdd is not null);
        RemoveCommand = new AsyncRelayCommand<PermissionRow?>(RemoveAsync);
        SetLevelCommand = new AsyncRelayCommand<object?>(SetLevelAsync);
    }

    public string FolderName => _folder.Name;
    public string FolderPath => _folder.FtpPath;

    public ObservableCollection<PermissionRow> Permissions { get; } = new();
    public ObservableCollection<User> AddableUsers { get; } = new();

    public IReadOnlyList<PermissionLevel> Levels { get; } = new[] { PermissionLevel.Read, PermissionLevel.Write };

    [ObservableProperty] private User? _selectedUserToAdd;
    [ObservableProperty] private PermissionLevel _selectedLevelToAdd = PermissionLevel.Read;
    [ObservableProperty] private bool _isBusy;

    public AsyncRelayCommand AddCommand { get; }
    public AsyncRelayCommand<PermissionRow?> RemoveCommand { get; }
    public AsyncRelayCommand<object?> SetLevelCommand { get; }

    partial void OnSelectedUserToAddChanged(User? value) => AddCommand.NotifyCanExecuteChanged();

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Permissions.Clear();
            var perms = await _db.GetFolderPermissionsAsync(_folder.Id);
            foreach (var (user, level) in perms)
                Permissions.Add(new PermissionRow(user, level));

            var allUsers = await _db.GetAllUsersAsync();
            RefreshAddableUsers(allUsers);
        }
        catch (Exception ex)
        {
            _notify.Error("Chargement des permissions impossible", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshAddableUsers(IReadOnlyList<User> allUsers)
    {
        var taken = Permissions.Select(p => p.User.Id).ToHashSet();
        taken.Add(_folder.OwnerId);

        AddableUsers.Clear();
        foreach (var u in allUsers.Where(u => !taken.Contains(u.Id)).OrderBy(u => u.Username))
            AddableUsers.Add(u);
    }

    private async Task AddAsync()
    {
        if (SelectedUserToAdd is null) return;
        var user = SelectedUserToAdd;
        try
        {
            IsBusy = true;
            await _db.SetPermissionAsync(_folder.Id, user.Id, SelectedLevelToAdd, _session.UserId);
            Permissions.Add(new PermissionRow(user, SelectedLevelToAdd));
            AddableUsers.Remove(user);
            SelectedUserToAdd = null;
            _notify.Success("Accès ajouté", $"{user.Username} — {(SelectedLevelToAdd == PermissionLevel.Write ? "écriture" : "lecture")}");
        }
        catch (Exception ex)
        {
            _notify.Error("Ajout impossible", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RemoveAsync(PermissionRow? row)
    {
        if (row is null) return;
        try
        {
            IsBusy = true;
            await _db.RemovePermissionAsync(_folder.Id, row.User.Id);
            Permissions.Remove(row);
            AddableUsers.Add(row.User);
            _notify.Info("Accès retiré", row.Username);
        }
        catch (Exception ex)
        {
            _notify.Error("Retrait impossible", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Paramètre attendu : le PermissionRow (via CommandParameter) — bascule lecture/écriture.</summary>
    private async Task SetLevelAsync(object? parameter)
    {
        if (parameter is not PermissionRow row) return;
        var newLevel = row.Level == PermissionLevel.Write ? PermissionLevel.Read : PermissionLevel.Write;
        try
        {
            await _db.SetPermissionAsync(_folder.Id, row.User.Id, newLevel, _session.UserId);
            row.Level = newLevel;
        }
        catch (Exception ex)
        {
            _notify.Error("Changement de niveau impossible", ex.Message);
        }
    }
}

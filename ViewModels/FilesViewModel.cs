using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoodleDrive.Models;
using DoodleDrive.Services;
using DoodleDrive.Views;
using DoodleDrive.Views.Dialogs;

namespace DoodleDrive.ViewModels;

public enum SortField { Name, Modified, Size, Kind }

/// <summary>Navigateur de fichiers : arbre latéral, fil d'Ariane, grille/liste, upload/download, partage.</summary>
public sealed partial class FilesViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly FtpService _ftp;
    private readonly ThumbnailService _thumbnails;
    private readonly DialogService _dialogs;
    private readonly NotificationService _notify;
    private readonly Session _session;
    private readonly AppConfigService _configService;

    private List<(Folder Folder, FolderAccessLevel Access)> _accessible = new();
    private readonly List<FileEntryViewModel> _allEntries = new();
    private CancellationTokenSource? _thumbCts;
    private bool _isProcessingUploads;

    // Historique précédent/suivant (boutons latéraux de la souris, comme l'Explorateur).
    private readonly Stack<string> _backHistory = new();
    private readonly Stack<string> _forwardHistory = new();
    private bool _isHistoryNavigation;
    private bool _hasNavigated;

    public FilesViewModel(
        DatabaseService db, FtpService ftp, ThumbnailService thumbnails, DialogService dialogs,
        NotificationService notify, Session session, AppConfigService configService)
    {
        _db = db;
        _ftp = ftp;
        _thumbnails = thumbnails;
        _dialogs = dialogs;
        _notify = notify;
        _session = session;
        _configService = configService;

        _isGridView = !string.Equals(configService.Current.DefaultView, "List", StringComparison.OrdinalIgnoreCase);

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        NavigateUpCommand = new AsyncRelayCommand(NavigateUpAsync, CanNavigateUp);
        NavigateBackCommand = new AsyncRelayCommand(NavigateBackAsync, () => _backHistory.Count > 0 && !IsBusy);
        NavigateForwardCommand = new AsyncRelayCommand(NavigateForwardAsync, () => _forwardHistory.Count > 0 && !IsBusy);
        NavigateToCommand = new AsyncRelayCommand<string?>(NavigateToAsync);
        OpenEntryCommand = new AsyncRelayCommand<FileEntryViewModel?>(OpenEntryAsync);
        NewFolderCommand = new AsyncRelayCommand(NewFolderAsync, () => CanWriteCurrent && !IsBusy);
        RenameCommand = new AsyncRelayCommand<FileEntryViewModel?>(RenameAsync);
        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync, () => HasSelection && CanWriteCurrent);
        DownloadSelectedCommand = new AsyncRelayCommand(DownloadSelectedAsync, () => HasSelection && CanReadCurrent);
        UploadFilesCommand = new AsyncRelayCommand(UploadFilesAsync, () => CanWriteCurrent && !IsBusy);
        PreviewCommand = new AsyncRelayCommand<FileEntryViewModel?>(PreviewAsync);
        ManageAccessCommand = new AsyncRelayCommand(ManageAccessCurrentAsync, () => CanManageCurrent);
        ManageAccessForNodeCommand = new AsyncRelayCommand<FolderNode?>(ManageAccessForNodeAsync);
        ManageAccessForEntryCommand = new AsyncRelayCommand<FileEntryViewModel?>(ManageAccessForEntryAsync);
        CopyPathCommand = new RelayCommand<FileEntryViewModel?>(CopyPath);
        CopyCurrentPathCommand = new RelayCommand(() => CopyToClipboard(CurrentPath));
        ToggleViewCommand = new RelayCommand(() => IsGridView = !IsGridView);
        SetSortCommand = new RelayCommand<string?>(SetSort);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        SignalUploadPanelCommand = new RelayCommand(() => IsUploadPanelOpen = !IsUploadPanelOpen);
    }

    // ----- Commandes -----
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand NavigateUpCommand { get; }
    public AsyncRelayCommand NavigateBackCommand { get; }
    public AsyncRelayCommand NavigateForwardCommand { get; }
    public AsyncRelayCommand<string?> NavigateToCommand { get; }
    public AsyncRelayCommand<FileEntryViewModel?> OpenEntryCommand { get; }
    public AsyncRelayCommand NewFolderCommand { get; }
    public AsyncRelayCommand<FileEntryViewModel?> RenameCommand { get; }
    public AsyncRelayCommand DeleteSelectedCommand { get; }
    public AsyncRelayCommand DownloadSelectedCommand { get; }
    public AsyncRelayCommand UploadFilesCommand { get; }
    public AsyncRelayCommand<FileEntryViewModel?> PreviewCommand { get; }
    public AsyncRelayCommand ManageAccessCommand { get; }
    public AsyncRelayCommand<FolderNode?> ManageAccessForNodeCommand { get; }
    public AsyncRelayCommand<FileEntryViewModel?> ManageAccessForEntryCommand { get; }
    public RelayCommand<FileEntryViewModel?> CopyPathCommand { get; }
    public RelayCommand CopyCurrentPathCommand { get; }
    public RelayCommand ToggleViewCommand { get; }
    public RelayCommand<string?> SetSortCommand { get; }
    public RelayCommand ClearSearchCommand { get; }
    public RelayCommand SignalUploadPanelCommand { get; }

    // ----- Collections -----
    public ObservableCollection<FolderNode> FolderTree { get; } = new();
    public ObservableCollection<FileEntryViewModel> Entries { get; } = new();
    public ObservableCollection<BreadcrumbItem> Breadcrumb { get; } = new();
    public ObservableCollection<UploadItemViewModel> Uploads { get; } = new();

    // ----- État -----
    [ObservableProperty] private FolderNode? _selectedFolderNode;
    [ObservableProperty] private string _currentPath = "/";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isGridView;
    [ObservableProperty] private bool _isUploadPanelOpen;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private FolderAccessLevel _currentAccess = FolderAccessLevel.None;
    [ObservableProperty] private string _emptyMessage = "Ce dossier est vide.";

    private SortField _sortField = SortField.Name;
    private bool _sortAscending = true;

    public bool IsAdmin => _session.IsAdmin;
    public bool HasSelection => SelectedCount > 0;
    public bool CanWriteCurrent => _session.IsAdmin || CurrentAccess.CanWrite();
    public bool CanReadCurrent => _session.IsAdmin || CurrentAccess.CanRead();
    public bool CanManageCurrent { get; private set; }
    public bool IsEntriesEmpty => Entries.Count == 0 && !IsBusy;
    public bool HasActiveUploads => Uploads.Any(u => u.IsActive);
    public string CurrentAccessLabel => CurrentAccess switch
    {
        FolderAccessLevel.Owner => "Propriétaire",
        FolderAccessLevel.Write => "Écriture",
        FolderAccessLevel.Read => "Lecture seule",
        FolderAccessLevel.Traverse => "Dossier de passage",
        _ => _session.IsAdmin ? "Administrateur" : "Aucun accès"
    };

    // =====================================================================
    //  Chargement initial / arbre des dossiers
    // =====================================================================

    public async Task InitializeAsync()
    {
        await LoadFolderTreeAsync();

        // Comportement automatique : premier dossier attribué (racine FTP pour l'admin).
        var auto = _session.IsAdmin
            ? FtpPathUtil.Normalize(_configService.Current.FtpRootPath)
            : FolderTree.FirstOrDefault()?.FtpPath ?? FtpPathUtil.Normalize(_configService.Current.FtpRootPath);

        // Priorité : dernier chemin visité -> chemin par défaut (admin) -> automatique.
        // Un candidat n'est retenu que s'il existe encore sur le FTP et reste accessible.
        var start = auto;
        foreach (var candidate in new[] { _session.CurrentUser?.LastPath, _session.CurrentUser?.DefaultPath })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            var path = FtpPathUtil.Normalize(candidate);
            if (AccessForPath(path) == FolderAccessLevel.None) continue;
            try
            {
                if (!await _ftp.DirectoryExistsAsync(path)) continue;
            }
            catch
            {
                continue;
            }
            start = path;
            break;
        }

        await NavigateToAsync(start);
    }

    public async Task LoadFolderTreeAsync()
    {
        try
        {
            FolderTree.Clear();

            if (_session.IsAdmin)
            {
                var all = await _db.GetAllFoldersAsync();
                _accessible = all.Select(f => (f, FolderAccessLevel.Owner)).ToList();

                // Racine synthétique = racine FTP, pour parcourir tout le disque.
                var rootPath = FtpPathUtil.Normalize(_configService.Current.FtpRootPath);
                var rootNode = new FolderNode(new Folder { Id = 0, Name = "Tout le drive", FtpPath = rootPath }, FolderAccessLevel.Owner);
                BuildChildren(rootNode, all.ToList(), null);
                FolderTree.Add(rootNode);
            }
            else
            {
                _accessible = (await _db.GetAccessibleFoldersAsync(_session.UserId)).ToList();
                var accessibleIds = _accessible.Select(a => a.Folder.Id).ToHashSet();
                var folders = _accessible.Select(a => a.Folder).ToList();

                // Racines = dossiers accessibles dont le parent n'est pas lui-même accessible.
                foreach (var (folder, access) in _accessible
                             .Where(a => a.Folder.ParentId is not int pid || !accessibleIds.Contains(pid))
                             .OrderBy(a => a.Folder.Name))
                {
                    var node = new FolderNode(folder, access);
                    BuildChildren(node, folders, folder.Id);
                    FolderTree.Add(node);
                }
            }
        }
        catch (Exception ex)
        {
            _notify.Error("Chargement des dossiers impossible", ex.Message);
        }
    }

    private void BuildChildren(FolderNode parent, List<Folder> all, int? parentId)
    {
        foreach (var child in all.Where(f => f.ParentId == parentId).OrderBy(f => f.Name))
        {
            var access = _accessible.FirstOrDefault(a => a.Folder.Id == child.Id).Access;
            if (access == FolderAccessLevel.None) access = FolderAccessLevel.Owner; // admin
            var node = new FolderNode(child, access);
            BuildChildren(node, all, child.Id);
            parent.Children.Add(node);
        }
    }

    partial void OnSelectedFolderNodeChanged(FolderNode? value)
    {
        if (value is null) return;
        _ = NavigateToAsync(value.FtpPath);
    }

    // =====================================================================
    //  Navigation FTP
    // =====================================================================

    private bool CanNavigateUp() =>
        !IsBusy && CurrentPath != BreadcrumbRootFor(CurrentPath);

    private async Task NavigateUpAsync()
    {
        var root = BreadcrumbRootFor(CurrentPath);
        if (CurrentPath == root) return;
        await NavigateToAsync(FtpPathUtil.GetParent(CurrentPath));
    }

    private async Task NavigateToAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        path = FtpPathUtil.Normalize(path);

        if (!_session.IsAdmin && AccessForPath(path) == FolderAccessLevel.None)
        {
            _notify.Warning("Accès refusé", "Vous n'avez pas accès à ce dossier.");
            return;
        }

        IsBusy = true;
        try
        {
            var entries = await _ftp.ListAsync(path);

            // Alimente l'historique (sauf refresh, navigation d'historique ou 1er chargement).
            if (!_isHistoryNavigation && _hasNavigated && CurrentPath != path)
            {
                _backHistory.Push(CurrentPath);
                _forwardHistory.Clear();
            }
            _hasNavigated = true;

            CurrentPath = path;
            CurrentAccess = AccessForPath(path);
            await UpdateManageStateAsync(path);
            BuildBreadcrumb(path);

            // En mode « couloir » (ancêtre d'un dossier autorisé), on ne montre que
            // les sous-dossiers menant à un dossier autorisé : ni les autres dossiers,
            // ni les fichiers du dossier de passage.
            var corridorOnly = !_session.IsAdmin && CurrentAccess == FolderAccessLevel.Traverse;

            _allEntries.Clear();
            foreach (var e in entries)
            {
                if (corridorOnly && (!e.IsDirectory || !IsCorridorDirectory(e.FullPath)))
                    continue;

                var vm = new FileEntryViewModel(e);
                vm.PropertyChanged += EntryPropertyChanged;
                _allEntries.Add(vm);
            }

            EmptyMessage = corridorOnly
                ? "Vous n'avez accès qu'à certains dossiers de ce chemin."
                : "Ce dossier est vide.";

            ApplyFilterAndSort();
            StartThumbnailLoading();
            _ = SaveLastPathAsync(path); // mémorisé pour la prochaine connexion (PC ou mobile)
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _notify.Error("Navigation impossible", ex.Message);
        }
        finally
        {
            IsBusy = false;
            RaiseContextChanged();
        }
    }

    private Task RefreshAsync() => NavigateToAsync(CurrentPath);

    private async Task NavigateBackAsync()
    {
        if (_backHistory.Count == 0) return;
        var target = _backHistory.Pop();
        _forwardHistory.Push(CurrentPath);
        _isHistoryNavigation = true;
        try
        {
            await NavigateToAsync(target);
        }
        finally
        {
            _isHistoryNavigation = false;
        }
    }

    private async Task NavigateForwardAsync()
    {
        if (_forwardHistory.Count == 0) return;
        var target = _forwardHistory.Pop();
        _backHistory.Push(CurrentPath);
        _isHistoryNavigation = true;
        try
        {
            await NavigateToAsync(target);
        }
        finally
        {
            _isHistoryNavigation = false;
        }
    }

    private async Task SaveLastPathAsync(string path)
    {
        try
        {
            await _db.UpdateUserLastPathAsync(_session.UserId, path);
        }
        catch
        {
            // Mémorisation best-effort : ne doit jamais gêner la navigation.
        }
    }

    private void BuildBreadcrumb(string path)
    {
        Breadcrumb.Clear();
        var root = BreadcrumbRootFor(path);
        var items = FtpPathUtil.Breadcrumb(path, root).ToList();
        for (var i = 0; i < items.Count; i++)
        {
            Breadcrumb.Add(new BreadcrumbItem
            {
                Name = items[i].Name,
                Path = items[i].Path,
                IsLast = i == items.Count - 1
            });
        }
    }

    private string BreadcrumbRootFor(string path)
    {
        // Racine FTP pour tout le monde : l'utilisateur peut ainsi remonter le
        // « couloir » (patate/eco+/…) jusqu'à ses dossiers autorisés.
        return FtpPathUtil.Normalize(_configService.Current.FtpRootPath);
    }

    /// <summary>
    /// Niveau d'accès effectif sur un chemin :
    /// - <b>Owner/Write/Read</b> si le chemin est dans un dossier autorisé (ou un de ses descendants) ;
    /// - <b>Traverse</b> si le chemin est un ancêtre d'un dossier autorisé (couloir vers celui-ci) ;
    /// - <b>None</b> sinon.
    /// </summary>
    private FolderAccessLevel AccessForPath(string path)
    {
        if (_session.IsAdmin) return FolderAccessLevel.Owner;

        // 1) Chemin situé dans un dossier autorisé (ou un descendant) -> accès complet hérité.
        var best = FolderAccessLevel.None;
        foreach (var (folder, access) in _accessible)
        {
            if (FtpPathUtil.IsWithin(path, folder.FtpPath) && access > best)
                best = access;
        }
        if (best != FolderAccessLevel.None) return best;

        // 2) Chemin ancêtre d'un dossier autorisé -> simple traversée (couloir).
        foreach (var (folder, _) in _accessible)
        {
            if (FtpPathUtil.IsWithin(folder.FtpPath, path) && folder.FtpPath != FtpPathUtil.Normalize(path))
                return FolderAccessLevel.Traverse;
        }

        return FolderAccessLevel.None;
    }

    /// <summary>Vrai si <paramref name="dirPath"/> est un dossier du couloir (ancêtre ou égal d'un dossier autorisé).</summary>
    private bool IsCorridorDirectory(string dirPath)
    {
        dirPath = FtpPathUtil.Normalize(dirPath);
        foreach (var (folder, _) in _accessible)
        {
            if (FtpPathUtil.IsWithin(folder.FtpPath, dirPath)) // le dossier autorisé est dans/égal à dirPath
                return true;
        }
        return false;
    }

    private async Task UpdateManageStateAsync(string path)
    {
        try
        {
            var folder = await _db.GetFolderByPathAsync(path);
            CanManageCurrent = folder is not null && (_session.IsAdmin || folder.OwnerId == _session.UserId);
        }
        catch
        {
            CanManageCurrent = false;
        }
        ManageAccessCommand.NotifyCanExecuteChanged();
    }

    // =====================================================================
    //  Recherche / tri
    // =====================================================================

    partial void OnSearchTextChanged(string value) => ApplyFilterAndSort();

    private void SetSort(string? field)
    {
        if (!Enum.TryParse<SortField>(field, out var parsed)) return;
        if (_sortField == parsed) _sortAscending = !_sortAscending;
        else { _sortField = parsed; _sortAscending = true; }
        ApplyFilterAndSort();
    }

    private void ApplyFilterAndSort()
    {
        IEnumerable<FileEntryViewModel> query = _allEntries;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            query = query.Where(e => e.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        // Dossiers d'abord, puis tri sur le champ choisi.
        Comparison<FileEntryViewModel> comparison = _sortField switch
        {
            SortField.Modified => (a, b) => a.Modified.CompareTo(b.Modified),
            SortField.Size => (a, b) => a.Size.CompareTo(b.Size),
            SortField.Kind => (a, b) => string.Compare(a.KindText, b.KindText, StringComparison.OrdinalIgnoreCase),
            _ => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
        };

        var list = query.ToList();
        list.Sort((a, b) =>
        {
            if (a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1;
            var r = comparison(a, b);
            return _sortAscending ? r : -r;
        });

        Entries.Clear();
        foreach (var e in list) Entries.Add(e);
        OnPropertyChanged(nameof(IsEntriesEmpty));
    }

    // =====================================================================
    //  Vignettes (chargées en tâche de fond)
    // =====================================================================

    private void StartThumbnailLoading()
    {
        _thumbCts?.Cancel();
        _thumbCts = new CancellationTokenSource();
        var ct = _thumbCts.Token;
        var targets = _allEntries.Where(e => e.CanHaveThumbnail).ToList();

        _ = Task.Run(async () =>
        {
            foreach (var entry in targets)
            {
                if (ct.IsCancellationRequested) return;
                var thumb = await _thumbnails.LoadImageThumbnailAsync(entry.Entry, ct: ct);
                if (thumb is not null && !ct.IsCancellationRequested)
                    App.Dispatch(() => entry.Thumbnail = thumb);
            }
        }, ct);
    }

    // =====================================================================
    //  Sélection
    // =====================================================================

    private void EntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileEntryViewModel.IsSelected))
        {
            SelectedCount = _allEntries.Count(x => x.IsSelected);
            RaiseContextChanged();
        }
    }

    private IReadOnlyList<FileEntryViewModel> SelectedEntries => _allEntries.Where(e => e.IsSelected).ToList();

    private void RaiseContextChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanWriteCurrent));
        OnPropertyChanged(nameof(CanReadCurrent));
        OnPropertyChanged(nameof(CanManageCurrent));
        OnPropertyChanged(nameof(CurrentAccessLabel));
        OnPropertyChanged(nameof(IsEntriesEmpty));
        OnPropertyChanged(nameof(HasActiveUploads));
        NewFolderCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        DownloadSelectedCommand.NotifyCanExecuteChanged();
        UploadFilesCommand.NotifyCanExecuteChanged();
        ManageAccessCommand.NotifyCanExecuteChanged();
        NavigateUpCommand.NotifyCanExecuteChanged();
        NavigateBackCommand.NotifyCanExecuteChanged();
        NavigateForwardCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value) => RaiseContextChanged();
    partial void OnCurrentAccessChanged(FolderAccessLevel value) => RaiseContextChanged();

    // =====================================================================
    //  Ouverture / prévisualisation
    // =====================================================================

    private async Task OpenEntryAsync(FileEntryViewModel? entry)
    {
        if (entry is null) return;
        if (entry.IsDirectory)
        {
            await NavigateToAsync(entry.FullPath);
            return;
        }

        if (entry.Kind == FileKind.Image)
        {
            await PreviewAsync(entry);
            return;
        }

        await OpenFileExternallyAsync(entry);
    }

    private async Task PreviewAsync(FileEntryViewModel? entry)
    {
        if (entry is null || entry.IsDirectory) return;
        if (entry.Kind != FileKind.Image)
        {
            await OpenFileExternallyAsync(entry);
            return;
        }

        try
        {
            IsBusy = true;
            var temp = Path.Combine(Path.GetTempPath(), "DoodleDrive", "preview");
            Directory.CreateDirectory(temp);
            var local = Path.Combine(temp, entry.Name);
            var ok = await _ftp.DownloadAsync(entry.FullPath, local);
            if (!ok) { _notify.Error("Prévisualisation impossible", entry.Name); return; }

            var window = new PreviewWindow(entry.Name, local);
            App.SetOwner(window);
            window.Show();
        }
        catch (Exception ex)
        {
            _notify.Error("Prévisualisation impossible", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenFileExternallyAsync(FileEntryViewModel entry)
    {
        try
        {
            IsBusy = true;
            var temp = Path.Combine(Path.GetTempPath(), "DoodleDrive", "open");
            Directory.CreateDirectory(temp);
            var local = Path.Combine(temp, entry.Name);
            var ok = await _ftp.DownloadAsync(entry.FullPath, local);
            if (!ok) { _notify.Error("Ouverture impossible", entry.Name); return; }

            Process.Start(new ProcessStartInfo(local) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _notify.Error("Ouverture impossible", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // =====================================================================
    //  Opérations dossiers/fichiers
    // =====================================================================

    private async Task NewFolderAsync()
    {
        var name = _dialogs.Prompt("Nouveau dossier", "Nom du dossier :", "Nouveau dossier");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = SanitizeName(name);

        var remotePath = FtpPathUtil.Combine(CurrentPath, name);
        try
        {
            IsBusy = true;
            if (await _ftp.DirectoryExistsAsync(remotePath))
            {
                _notify.Warning("Dossier existant", $"« {name} » existe déjà ici.");
                return;
            }

            await _ftp.CreateDirectoryAsync(remotePath);
            await RegisterFolderAsync(remotePath);
            _notify.Success("Dossier créé", name);
            await NavigateToAsync(CurrentPath);
            await LoadFolderTreeAsync();
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

    private async Task<int?> RegisterFolderAsync(string remotePath)
    {
        try
        {
            var existing = await _db.GetFolderByPathAsync(remotePath);
            if (existing is not null) return existing.Id;

            var parentPath = FtpPathUtil.GetParent(remotePath);
            var parent = parentPath == remotePath ? null : await _db.GetFolderByPathAsync(parentPath);
            return await _db.CreateFolderAsync(FtpPathUtil.GetName(remotePath), remotePath, parent?.Id, _session.UserId);
        }
        catch (Exception ex)
        {
            _notify.Warning("Enregistrement du dossier en base incomplet", ex.Message);
            return null;
        }
    }

    private async Task RenameAsync(FileEntryViewModel? entry)
    {
        if (entry is null || !CanWriteCurrent) return;

        var newName = _dialogs.Prompt("Renommer", "Nouveau nom :", entry.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == entry.Name) return;
        newName = SanitizeName(newName);

        var oldPath = entry.FullPath;
        var newPath = FtpPathUtil.Combine(CurrentPath, newName);
        try
        {
            IsBusy = true;
            await _ftp.RenameAsync(oldPath, newPath);

            if (entry.IsDirectory)
            {
                var folder = await _db.GetFolderByPathAsync(oldPath);
                if (folder is not null)
                    await _db.RenameFolderAsync(folder.Id, oldPath, newPath, newName);
            }

            _notify.Success("Renommé", newName);
            await NavigateToAsync(CurrentPath);
            await LoadFolderTreeAsync();
        }
        catch (Exception ex)
        {
            _notify.Error("Renommage impossible", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteSelectedAsync()
    {
        var items = SelectedEntries;
        if (items.Count == 0 || !CanWriteCurrent) return;

        var label = items.Count == 1 ? $"« {items[0].Name} »" : $"{items.Count} éléments";
        if (!_dialogs.Confirm("Supprimer", $"Supprimer définitivement {label} ?", "Supprimer", destructive: true))
            return;

        IsBusy = true;
        var errors = 0;
        foreach (var item in items)
        {
            try
            {
                if (item.IsDirectory)
                {
                    await _ftp.DeleteDirectoryAsync(item.FullPath);
                    await _db.DeleteFoldersByPathAsync(item.FullPath);
                }
                else
                {
                    await _ftp.DeleteFileAsync(item.FullPath);
                }
            }
            catch (Exception ex)
            {
                errors++;
                _notify.Error("Suppression impossible", $"{item.Name} : {ex.Message}");
            }
        }
        IsBusy = false;

        if (errors == 0) _notify.Success("Supprimé", label);
        await NavigateToAsync(CurrentPath);
        await LoadFolderTreeAsync();
    }

    private async Task DownloadSelectedAsync()
    {
        var items = SelectedEntries;
        if (items.Count == 0) return;

        var dest = _dialogs.PickDownloadFolder(_configService.Current.LastDownloadFolder);
        if (dest is null) return;

        var c = _configService.Current;
        c.LastDownloadFolder = dest;
        _configService.Save(c);

        IsBusy = true;
        var count = 0;
        try
        {
            foreach (var item in items)
            {
                if (item.IsDirectory)
                    count += await DownloadDirectoryAsync(item.FullPath, Path.Combine(dest, item.Name));
                else
                {
                    var ok = await _ftp.DownloadAsync(item.FullPath, Path.Combine(dest, item.Name));
                    if (ok) count++;
                }
            }
            _notify.Success("Téléchargement terminé", $"{count} fichier(s) enregistré(s).");
        }
        catch (Exception ex)
        {
            _notify.Error("Téléchargement interrompu", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<int> DownloadDirectoryAsync(string remoteDir, string localDir)
    {
        Directory.CreateDirectory(localDir);
        var count = 0;
        var entries = await _ftp.ListAsync(remoteDir);
        foreach (var e in entries)
        {
            if (e.IsDirectory)
                count += await DownloadDirectoryAsync(e.FullPath, Path.Combine(localDir, e.Name));
            else if (await _ftp.DownloadAsync(e.FullPath, Path.Combine(localDir, e.Name)))
                count++;
        }
        return count;
    }

    // =====================================================================
    //  Upload (sélecteur + glisser-déposer)
    // =====================================================================

    private async Task UploadFilesAsync()
    {
        var files = _dialogs.PickFilesToUpload();
        if (files is null || files.Length == 0) return;
        await HandleDropAsync(files);
    }

    /// <summary>Appelé par la vue lors d'un glisser-déposer (fichiers et/ou dossiers locaux).</summary>
    public async Task HandleDropAsync(IEnumerable<string> localPaths)
    {
        if (!CanWriteCurrent)
        {
            _notify.Warning("Envoi refusé", "Vous n'avez pas les droits d'écriture sur ce dossier.");
            return;
        }

        var newItems = new List<UploadItemViewModel>();
        foreach (var path in localPaths)
        {
            if (Directory.Exists(path))
                await EnqueueDirectoryAsync(path, CurrentPath, newItems);
            else if (File.Exists(path))
                newItems.Add(new UploadItemViewModel(path, FtpPathUtil.Combine(CurrentPath, Path.GetFileName(path)), Path.GetFileName(path)));
        }

        if (newItems.Count == 0) return;
        foreach (var item in newItems) Uploads.Add(item);
        IsUploadPanelOpen = true;
        await ProcessUploadQueueAsync();
    }

    private async Task EnqueueDirectoryAsync(string localDir, string remoteParent, List<UploadItemViewModel> sink)
    {
        var name = SanitizeName(Path.GetFileName(localDir.TrimEnd(Path.DirectorySeparatorChar)));
        var remoteDir = FtpPathUtil.Combine(remoteParent, name);
        try
        {
            await _ftp.CreateDirectoryAsync(remoteDir);
            await RegisterFolderAsync(remoteDir);
        }
        catch (Exception ex)
        {
            _notify.Error("Création de dossier distante impossible", ex.Message);
            return;
        }

        foreach (var file in Directory.GetFiles(localDir))
            sink.Add(new UploadItemViewModel(file, FtpPathUtil.Combine(remoteDir, Path.GetFileName(file)), Path.GetFileName(file)));

        foreach (var sub in Directory.GetDirectories(localDir))
            await EnqueueDirectoryAsync(sub, remoteDir, sink);
    }

    private async Task ProcessUploadQueueAsync()
    {
        if (_isProcessingUploads) return;
        _isProcessingUploads = true;
        try
        {
            while (true)
            {
                var item = Uploads.FirstOrDefault(u => u.Status == UploadStatus.Pending);
                if (item is null) break;

                item.Status = UploadStatus.Uploading;
                var progress = new Progress<double>(p => item.Progress = p);
                try
                {
                    var ok = await _ftp.UploadAsync(item.LocalPath, item.RemotePath, progress);
                    item.Progress = 100;
                    item.Status = ok ? UploadStatus.Completed : UploadStatus.Failed;
                }
                catch (Exception ex)
                {
                    item.ErrorMessage = ex.Message;
                    item.Status = UploadStatus.Failed;
                }

                // Rafraîchit si l'upload concerne le dossier courant.
                if (FtpPathUtil.GetParent(item.RemotePath) == CurrentPath)
                    await NavigateToAsync(CurrentPath);
            }

            var done = Uploads.Count(u => u.Status == UploadStatus.Completed);
            var failed = Uploads.Count(u => u.Status == UploadStatus.Failed);
            if (failed == 0) _notify.Success("Envoi terminé", $"{done} fichier(s) envoyé(s).");
            else _notify.Warning("Envoi terminé avec erreurs", $"{done} réussi(s), {failed} échoué(s).");

            await LoadFolderTreeAsync();
        }
        finally
        {
            _isProcessingUploads = false;
            OnPropertyChanged(nameof(HasActiveUploads));
        }
    }

    // =====================================================================
    //  Partage / permissions
    // =====================================================================

    private Task ManageAccessCurrentAsync() => OpenPermissionsForPathAsync(CurrentPath);

    private Task ManageAccessForNodeAsync(FolderNode? node) =>
        node is null ? Task.CompletedTask : OpenPermissionsForPathAsync(node.FtpPath);

    private Task ManageAccessForEntryAsync(FileEntryViewModel? entry) =>
        entry is null || !entry.IsDirectory ? Task.CompletedTask : OpenPermissionsForPathAsync(entry.FullPath);

    /// <summary>
    /// Ouvre le panneau de partage pour un chemin FTP. En tant qu'admin, un dossier
    /// non encore enregistré en base est enregistré à la volée (permet d'attribuer
    /// n'importe quel dossier du disque à un utilisateur).
    /// </summary>
    private async Task OpenPermissionsForPathAsync(string path)
    {
        var folder = await EnsureManageableFolderAsync(path);
        if (folder is null)
        {
            _notify.Warning("Partage indisponible", "Ce dossier n'est pas enregistré comme dossier partageable.");
            return;
        }
        if (!_session.IsAdmin && folder.OwnerId != _session.UserId)
        {
            _notify.Warning("Partage refusé", "Seul le propriétaire (ou un admin) peut gérer les accès.");
            return;
        }

        var vm = new PermissionsDialogViewModel(_db, _notify, _session, folder);
        await vm.LoadAsync();
        var dialog = new PermissionsDialog(vm);
        App.SetOwner(dialog);
        dialog.ShowDialog();

        // Le partage a pu changer l'accès d'utilisateurs : on rafraîchit l'arbre.
        await LoadFolderTreeAsync();
    }

    private async Task<Folder?> EnsureManageableFolderAsync(string path)
    {
        var folder = await _db.GetFolderByPathAsync(path);
        if (folder is not null) return folder;

        // Seul l'admin peut transformer un dossier FTP quelconque en dossier partageable.
        if (!_session.IsAdmin) return null;

        await RegisterFolderAsync(path);
        return await _db.GetFolderByPathAsync(path);
    }

    // =====================================================================
    //  Divers
    // =====================================================================

    private void CopyPath(FileEntryViewModel? entry)
    {
        if (entry is not null) CopyToClipboard(entry.FullPath);
    }

    private void CopyToClipboard(string path)
    {
        try
        {
            System.Windows.Clipboard.SetText(path);
            _notify.Success("Chemin copié", path);
        }
        catch (Exception ex)
        {
            // Le presse-papiers Windows peut être momentanément verrouillé par une autre app.
            _notify.Error("Copie impossible", ex.Message);
        }
    }

    private static string SanitizeName(string name)
    {
        name = name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Replace('/', '_').Replace('\\', '_');
    }

    partial void OnIsUploadPanelOpenChanged(bool value) { }
}

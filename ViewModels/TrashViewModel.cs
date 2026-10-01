using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoodleDrive.Models;
using DoodleDrive.Services;

namespace DoodleDrive.ViewModels;

/// <summary>Un élément de la corbeille.</summary>
public sealed class TrashRowViewModel(ApiTrashItem item)
{
    public int Id => item.Id;
    public string Name => item.Name;
    public bool IsDir => item.IsDir;
    public string OriginalPath => item.OriginalPath;

    public string Glyph => item.IsDir
        ? ThumbnailService.GetGlyph(FileKind.Folder)
        : ThumbnailService.GetGlyph(RemoteEntry.ClassifyExtension(item.Name));

    public string AccentBrushKey => item.IsDir
        ? ThumbnailService.GetAccentBrushKey(FileKind.Folder)
        : ThumbnailService.GetAccentBrushKey(RemoteEntry.ClassifyExtension(item.Name));

    /// <summary>Dossier d'origine (affichage « joli »).</summary>
    public string LocationText => FtpPathUtil.ToDisplay(FtpPathUtil.GetParent(item.OriginalPath));

    public string DeletedText => item.DeletedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public string SizeText => FileEntryViewModel.FormatSize(item.Size);
    public string DeletedBy => item.DeletedBy ?? "—";

    public string ExpiresText
    {
        get
        {
            var left = item.ExpiresAt - DateTime.UtcNow;
            if (left.TotalHours < 1) return "bientôt";
            return left.TotalDays < 1 ? $"dans {(int)left.TotalHours} h" : $"dans {(int)Math.Ceiling(left.TotalDays)} j";
        }
    }
}

/// <summary>Onglet Corbeille : éléments supprimés, restaurables pendant la durée de rétention.</summary>
public sealed partial class TrashViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly NotificationService _notify;
    private readonly DialogService _dialogs;
    private readonly Session _session;

    public TrashViewModel(ApiClient api, NotificationService notify, DialogService dialogs, Session session)
    {
        _api = api;
        _notify = notify;
        _dialogs = dialogs;
        _session = session;

        RefreshCommand = new AsyncRelayCommand(() => LoadAsync());
        RestoreCommand = new AsyncRelayCommand<TrashRowViewModel?>(RestoreAsync);
        DeleteForeverCommand = new AsyncRelayCommand<TrashRowViewModel?>(DeleteForeverAsync);
        EmptyCommand = new AsyncRelayCommand(EmptyAsync, () => Items.Count > 0 && !IsBusy);
    }

    /// <summary>Un élément a été restauré (le Shell rafraîchit la vue Fichiers).</summary>
    public event Action? ItemRestored;

    public ObservableCollection<TrashRowViewModel> Items { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(IsFirstLoading))]
    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    private string? _loadError;

    [ObservableProperty] private int _retentionDays = 30;

    private bool _hasLoaded;

    public bool IsAdmin => _session.IsAdmin;
    public bool IsFirstLoading => IsBusy && Items.Count == 0;
    public bool HasLoadError => !IsBusy && LoadError is not null && Items.Count == 0;
    public bool IsEmpty => _hasLoaded && !IsBusy && Items.Count == 0 && LoadError is null;

    public string Subtitle => IsAdmin
        ? $"Éléments supprimés (tous les utilisateurs) — conservés {RetentionDays} jours avant suppression définitive"
        : $"Vos éléments supprimés — conservés {RetentionDays} jours avant suppression définitive";

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand<TrashRowViewModel?> RestoreCommand { get; }
    public AsyncRelayCommand<TrashRowViewModel?> DeleteForeverCommand { get; }
    public AsyncRelayCommand EmptyCommand { get; }

    partial void OnIsBusyChanged(bool value) => EmptyCommand.NotifyCanExecuteChanged();
    partial void OnRetentionDaysChanged(int value) => OnPropertyChanged(nameof(Subtitle));

    public async Task LoadAsync(bool silent = false)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var list = await _api.GetTrashAsync();
            RetentionDays = list.RetentionDays;
            Items.Clear();
            foreach (var i in list.Items) Items.Add(new TrashRowViewModel(i));
            _hasLoaded = true;
            LoadError = null;
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
            if (!silent) _notify.Error("Chargement de la corbeille impossible", ex.Message);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsFirstLoading));
            OnPropertyChanged(nameof(HasLoadError));
            EmptyCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task RestoreAsync(TrashRowViewModel? row)
    {
        if (row is null) return;
        try
        {
            await _api.RestoreTrashAsync(row.Id);
            Items.Remove(row);
            _notify.Success("Restauré", $"{row.Name} → {row.LocationText}");
            ItemRestored?.Invoke();
        }
        catch (Exception ex)
        {
            _notify.Error("Restauration impossible", ex.Message);
        }
        finally
        {
            OnPropertyChanged(nameof(IsEmpty));
            EmptyCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task DeleteForeverAsync(TrashRowViewModel? row)
    {
        if (row is null) return;
        if (!_dialogs.Confirm("Supprimer définitivement",
                $"« {row.Name} » sera supprimé définitivement. Cette action est irréversible.",
                "Supprimer définitivement", destructive: true))
            return;
        try
        {
            await _api.DeleteTrashAsync(row.Id);
            Items.Remove(row);
            _notify.Success("Supprimé définitivement", row.Name);
        }
        catch (Exception ex)
        {
            _notify.Error("Suppression impossible", ex.Message);
        }
        finally
        {
            OnPropertyChanged(nameof(IsEmpty));
            EmptyCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task EmptyAsync()
    {
        var what = IsAdmin ? "Tous les éléments de la corbeille (de tous les utilisateurs)" : "Tous vos éléments supprimés";
        if (!_dialogs.Confirm("Vider la corbeille",
                $"{what} seront supprimés définitivement ({Items.Count} élément(s)). Cette action est irréversible.",
                "Vider la corbeille", destructive: true))
            return;
        try
        {
            await _api.EmptyTrashAsync();
            _notify.Success("Corbeille vidée");
        }
        catch (Exception ex)
        {
            _notify.Error("Impossible de vider la corbeille", ex.Message);
        }
        await LoadAsync();
    }
}

/// <summary>Un disque du serveur (barre latérale : espace libre).</summary>
public sealed class StorageVolumeViewModel(ApiVolume v)
{
    public string Name => v.Name;
    public double UsedPercent => v.Total <= 0 ? 0 : (v.Total - v.Free) * 100.0 / v.Total;
    public bool IsLow => v.Total > 0 && v.Free * 100.0 / v.Total < 10;
    public string FreeText => $"{FileEntryViewModel.FormatSize(v.Free)} libres sur {FileEntryViewModel.FormatSize(v.Total)}";
}

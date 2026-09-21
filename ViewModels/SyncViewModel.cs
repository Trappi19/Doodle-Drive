using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoodleDrive.Services;
using DoodleDrive.Views.Dialogs;

namespace DoodleDrive.ViewModels;

/// <summary>Une paire de synchronisation (dossier local ↔ dossier en ligne).</summary>
public sealed partial class SyncFolderRowViewModel : ObservableObject
{
    private readonly Func<SyncFolderRowViewModel, bool, Task>? _persistAuto;

    public SyncFolderRowViewModel(ApiSyncFolder f, bool isThisMachine,
        Func<SyncFolderRowViewModel, bool, Task>? persistAuto = null)
    {
        Id = f.Id;
        LocalPath = f.LocalPath;
        RemotePath = f.RemotePath;
        MachineName = f.MachineName;
        IsThisMachine = isThisMachine;
        _autoSync = f.AutoSync;      // assignation directe : ne déclenche pas la persistance
        _lastSyncAt = f.LastSyncAt;
        _persistAuto = persistAuto;
    }

    public int Id { get; }
    public string LocalPath { get; }
    public string RemotePath { get; }
    public string MachineName { get; }
    public bool IsThisMachine { get; }

    /// <summary>Chemin complet « local ⇄ en ligne » (affiché en défilement au survol).</summary>
    public string PathSummary => $"{LocalPath}    ⇄    {RemotePath}";

    [ObservableProperty] private bool _autoSync;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _progressIndeterminate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastSyncText))]
    private DateTime? _lastSyncAt;

    public string LastSyncText => LastSyncAt is { } d
        ? $"Dernière synchro : {d.ToLocalTime():dd/MM/yyyy HH:mm}"
        : "Jamais synchronisé";

    partial void OnAutoSyncChanged(bool value)
    {
        if (_persistAuto is not null) _ = _persistAuto(this, value);
    }
}

/// <summary>Onglet Synchronisation : gérer les paires local ↔ en ligne de cette machine, voir celles des autres.</summary>
public sealed partial class SyncViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly SyncService _sync;
    private readonly FtpService _ftp;
    private readonly AppConfigService _config;
    private readonly DialogService _dialogs;
    private readonly NotificationService _notify;

    public SyncViewModel(ApiClient api, SyncService sync, FtpService ftp, AppConfigService config,
        DialogService dialogs, NotificationService notify)
    {
        _api = api;
        _sync = sync;
        _ftp = ftp;
        _config = config;
        _dialogs = dialogs;
        _notify = notify;

        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        AddCommand = new AsyncRelayCommand(AddAsync, () => !IsBusy);
        SyncAllCommand = new AsyncRelayCommand(SyncAllAsync, () => !IsBusy && ThisMachine.Count > 0);
        SendRowCommand = new AsyncRelayCommand<SyncFolderRowViewModel?>(r => RunSyncAsync(r, SyncDirection.Upload));
        FetchRowCommand = new AsyncRelayCommand<SyncFolderRowViewModel?>(r => RunSyncAsync(r, SyncDirection.Download));
        SyncRowCommand = new AsyncRelayCommand<SyncFolderRowViewModel?>(r => RunSyncAsync(r, SyncDirection.Both));
        CheckRowCommand = new AsyncRelayCommand<SyncFolderRowViewModel?>(CheckRowAsync);
        RemoveRowCommand = new AsyncRelayCommand<SyncFolderRowViewModel?>(RemoveRowAsync);
    }

    public ObservableCollection<SyncFolderRowViewModel> ThisMachine { get; } = new();
    public ObservableCollection<SyncFolderRowViewModel> OtherMachines { get; } = new();

    [ObservableProperty] private bool _isBusy;

    public string MachineName => Environment.MachineName;
    public bool HasThisMachine => ThisMachine.Count > 0;
    public bool HasOtherMachines => OtherMachines.Count > 0;

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddCommand { get; }
    public AsyncRelayCommand SyncAllCommand { get; }
    public AsyncRelayCommand<SyncFolderRowViewModel?> SendRowCommand { get; }
    public AsyncRelayCommand<SyncFolderRowViewModel?> FetchRowCommand { get; }
    public AsyncRelayCommand<SyncFolderRowViewModel?> SyncRowCommand { get; }
    public AsyncRelayCommand<SyncFolderRowViewModel?> CheckRowCommand { get; }
    public AsyncRelayCommand<SyncFolderRowViewModel?> RemoveRowCommand { get; }

    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        AddCommand.NotifyCanExecuteChanged();
        SyncAllCommand.NotifyCanExecuteChanged();
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var all = await _api.GetSyncFoldersAsync();
            var mid = _config.Current.MachineId;

            ThisMachine.Clear();
            OtherMachines.Clear();
            foreach (var f in all)
            {
                if (string.Equals(f.MachineId, mid, StringComparison.Ordinal))
                    ThisMachine.Add(new SyncFolderRowViewModel(f, true, PersistAutoAsync));
                else
                    OtherMachines.Add(new SyncFolderRowViewModel(f, false));
            }
            OnPropertyChanged(nameof(HasThisMachine));
            OnPropertyChanged(nameof(HasOtherMachines));
            SyncAllCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _notify.Error("Chargement des synchronisations impossible", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddAsync()
    {
        var local = _dialogs.PickDownloadFolder(_config.Current.LastDownloadFolder);
        if (string.IsNullOrWhiteSpace(local)) return;
        var localName = Path.GetFileName(local.TrimEnd('\\', '/'));

        // Sélecteur navigable du drive (+ case « créer un sous-dossier au même nom »).
        var picker = new RemoteFolderPickerDialog(_ftp, _config.Current.FtpRootPath, localName);
        App.SetOwner(picker);
        if (picker.ShowDialog() != true) return;

        var remote = picker.ResolvedPath;
        if (string.IsNullOrWhiteSpace(remote)) return;
        remote = FtpPathUtil.Normalize(remote);

        try
        {
            await _api.CreateSyncFolderAsync(_config.Current.MachineId, MachineName, local, remote, false);
            _notify.Success("Synchronisation ajoutée", $"{localName} ↔ {remote}");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _notify.Error("Ajout impossible", ex.Message);
        }
    }

    private async Task CheckRowAsync(SyncFolderRowViewModel? row)
    {
        if (row is null || row.IsBusy) return;
        row.IsBusy = true;
        row.Status = "Vérification…";
        try
        {
            var plan = await _sync.PreviewAsync(row.LocalPath, row.RemotePath);
            if (plan.Count == 0)
            {
                row.Status = "À jour — rien à synchroniser.";
            }
            else
            {
                var up = plan.Count(p => p.Action is SyncAction.UploadNew or SyncAction.UploadModified);
                var down = plan.Count - up;
                row.Status = $"{plan.Count} fichier(s) à synchroniser  ({up} ↑, {down} ↓)";
            }
        }
        catch (Exception ex)
        {
            row.Status = "Erreur de vérification.";
            _notify.Error("Vérification impossible", ex.Message);
        }
        finally
        {
            row.IsBusy = false;
        }
    }

    private async Task SyncAllAsync()
    {
        foreach (var row in ThisMachine.ToList())
            await RunSyncAsync(row, SyncDirection.Both);
    }

    private async Task RunSyncAsync(SyncFolderRowViewModel? row, SyncDirection direction)
    {
        if (row is null || row.IsBusy) return;

        var (running, doneVerb) = direction switch
        {
            SyncDirection.Upload => ("Envoi…", "Envoi"),
            SyncDirection.Download => ("Réception…", "Réception"),
            _ => ("Synchronisation…", "Synchronisation")
        };

        row.IsBusy = true;
        row.Status = running;
        row.Progress = 0;
        row.ProgressIndeterminate = true; // le temps du diff (avant le 1er transfert)
        try
        {
            var progress = new Progress<SyncProgress>(sp =>
            {
                row.ProgressIndeterminate = false;
                row.Progress = sp.Percent;
                row.Status = string.IsNullOrEmpty(sp.CurrentFile)
                    ? running
                    : $"{doneVerb} {sp.Done}/{sp.Total} — {sp.CurrentFile}";
            });
            var res = await _sync.SyncAsync(row.LocalPath, row.RemotePath, direction, progress);
            await _api.TouchSyncAsync(row.Id);
            row.LastSyncAt = DateTime.UtcNow;

            if (res.Failed == 0)
            {
                row.Status = res.Changed == 0 ? "À jour — rien à échanger." : $"Terminé — {res.Changed} fichier(s).";
                _notify.Success($"{doneVerb} terminé",
                    $"{FtpPathUtil.GetName(row.RemotePath)} : {res.Changed} fichier(s).");
            }
            else
            {
                row.Status = $"{res.Changed} ok, {res.Failed} échec(s).";
                _notify.Warning($"{doneVerb} terminé avec erreurs",
                    $"{res.Changed} ok, {res.Failed} échec(s).");
            }
        }
        catch (Exception ex)
        {
            row.Status = "Échec de l'opération.";
            _notify.Error("Opération impossible", ex.Message);
        }
        finally
        {
            row.IsBusy = false;
        }
    }

    private async Task RemoveRowAsync(SyncFolderRowViewModel? row)
    {
        if (row is null) return;
        if (!_dialogs.Confirm("Retirer la synchronisation",
                $"Retirer la synchronisation de « {row.LocalPath} » ?\n\nLes fichiers (locaux et en ligne) ne sont PAS supprimés — on arrête juste de les synchroniser.",
                "Retirer", destructive: true))
            return;

        try
        {
            await _api.DeleteSyncFolderAsync(row.Id);
            await LoadAsync();
            _notify.Info("Synchronisation retirée", row.LocalPath);
        }
        catch (Exception ex)
        {
            _notify.Error("Retrait impossible", ex.Message);
        }
    }

    private async Task PersistAutoAsync(SyncFolderRowViewModel row, bool value)
    {
        try
        {
            await _api.SetSyncAutoAsync(row.Id, value);
        }
        catch (Exception ex)
        {
            _notify.Error("Changement du mode automatique impossible", ex.Message);
        }
    }
}

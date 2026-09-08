using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoodleDrive.Services;

namespace DoodleDrive.ViewModels;

/// <summary>Onglet « Mes partages » : liste et gestion des liens de partage créés par l'utilisateur.</summary>
public sealed partial class SharesViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly NotificationService _notify;
    private readonly Session _session;
    private readonly AppConfigService _configService;
    private readonly DialogService _dialogs;

    public SharesViewModel(DatabaseService db, NotificationService notify, Session session,
        AppConfigService configService, DialogService dialogs)
    {
        _db = db;
        _notify = notify;
        _session = session;
        _configService = configService;
        _dialogs = dialogs;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        CopyCommand = new RelayCommand<ShareRowViewModel?>(Copy);
        OpenCommand = new RelayCommand<ShareRowViewModel?>(Open);
        RevokeCommand = new AsyncRelayCommand<ShareRowViewModel?>(RevokeAsync);
    }

    public ObservableCollection<ShareRowViewModel> Shares { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isBusy;

    public bool IsEmpty => Shares.Count == 0 && !IsBusy;

    /// <summary>Vrai si l'URL du serveur de partage n'est pas configurée (Paramètres).</summary>
    public bool IsShareNotConfigured => string.IsNullOrWhiteSpace(_configService.Current.ShareBaseUrl);

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand<ShareRowViewModel?> CopyCommand { get; }
    public RelayCommand<ShareRowViewModel?> OpenCommand { get; }
    public AsyncRelayCommand<ShareRowViewModel?> RevokeCommand { get; }

    public async Task LoadAsync()
    {
        IsBusy = true;
        OnPropertyChanged(nameof(IsShareNotConfigured));
        try
        {
            var baseUrl = _configService.Current.ShareBaseUrl?.Trim() ?? string.Empty;
            var user = _session.UserName;

            var list = await _db.GetSharesAsync(_session.UserId);
            Shares.Clear();
            foreach (var s in list)
                Shares.Add(new ShareRowViewModel(s, baseUrl, user));
        }
        catch (Exception ex)
        {
            _notify.Error("Chargement des partages impossible", ex.Message);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private void Copy(ShareRowViewModel? row)
    {
        if (row is null) return;
        if (string.IsNullOrEmpty(row.Url))
        {
            _notify.Warning("Lien indisponible", "Configurez l'URL de partage dans Paramètres.");
            return;
        }
        try
        {
            System.Windows.Clipboard.SetText(row.Url);
            _notify.Success("Lien copié", row.FileName);
        }
        catch (Exception ex)
        {
            _notify.Error("Copie impossible", ex.Message);
        }
    }

    private void Open(ShareRowViewModel? row)
    {
        if (row is null || string.IsNullOrEmpty(row.Url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(row.Url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _notify.Error("Ouverture impossible", ex.Message);
        }
    }

    private async Task RevokeAsync(ShareRowViewModel? row)
    {
        if (row is null) return;
        if (!_dialogs.Confirm("Révoquer le lien",
                $"Le lien vers « {row.FileName} » cessera immédiatement de fonctionner. Continuer ?",
                "Révoquer", destructive: true))
            return;

        try
        {
            await _db.RevokeShareAsync(row.Token);
            _notify.Success("Lien révoqué", row.FileName);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _notify.Error("Révocation impossible", ex.Message);
        }
    }
}

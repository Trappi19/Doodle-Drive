using CommunityToolkit.Mvvm.ComponentModel;

namespace DoodleDrive.ViewModels;

public enum UploadStatus { Pending, Uploading, Paused, Completed, Failed, Canceled }

/// <summary>Un fichier dans la file d'attente d'envoi, avec sa progression et son contrôle (pause/annuler).</summary>
public sealed partial class UploadItemViewModel : ObservableObject
{
    public UploadItemViewModel(string localPath, string remotePath, string displayName)
    {
        LocalPath = localPath;
        RemotePath = remotePath;
        FileName = displayName;
    }

    public string LocalPath { get; }
    public string RemotePath { get; }
    public string FileName { get; }

    /// <summary>Identifiant stable de l'envoi (permet la reprise côté serveur).</summary>
    public string UploadId { get; } = Guid.NewGuid().ToString("N");

    /// <summary>Jeton d'annulation de l'envoi en cours (recréé à chaque reprise).</summary>
    public CancellationTokenSource Cts { get; private set; } = new();

    public void ResetCts()
    {
        Cts.Dispose();
        Cts = new CancellationTokenSource();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate))]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    [NotifyPropertyChangedFor(nameof(CanResume))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    private UploadStatus _status = UploadStatus.Pending;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Actif = à conserver dans la file (en cours, en attente ou en pause).</summary>
    public bool IsActive => Status is UploadStatus.Pending or UploadStatus.Uploading or UploadStatus.Paused;
    // Envoi chunké : la progression est toujours connue → vraie barre (jamais indéterminée).
    public bool IsIndeterminate => false;

    public bool CanPause => Status is UploadStatus.Pending or UploadStatus.Uploading;
    public bool CanResume => Status is UploadStatus.Paused or UploadStatus.Failed;
    public bool CanCancel => Status is UploadStatus.Pending or UploadStatus.Uploading or UploadStatus.Paused or UploadStatus.Failed;

    public string StatusText => Status switch
    {
        UploadStatus.Pending => "En attente",
        UploadStatus.Uploading => "Envoi…",
        UploadStatus.Paused => "En pause",
        UploadStatus.Completed => "Terminé",
        UploadStatus.Failed => ErrorMessage is null ? "Échec" : $"Échec : {ErrorMessage}",
        UploadStatus.Canceled => "Annulé",
        _ => string.Empty
    };
}

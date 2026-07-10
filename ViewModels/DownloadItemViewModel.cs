using CommunityToolkit.Mvvm.ComponentModel;

namespace DoodleDrive.ViewModels;

public enum DownloadStatus { Pending, Downloading, Completed, Failed, Canceled }

/// <summary>Un fichier dans la file d'attente de téléchargement, avec sa progression.</summary>
public sealed partial class DownloadItemViewModel : ObservableObject
{
    public DownloadItemViewModel(string remotePath, string localPath, string displayName)
    {
        RemotePath = remotePath;
        LocalPath = localPath;
        FileName = displayName;
    }

    public string RemotePath { get; }
    public string LocalPath { get; }
    public string FileName { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate))]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    private DownloadStatus _status = DownloadStatus.Pending;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string? _errorMessage;

    public bool IsActive => Status is DownloadStatus.Pending or DownloadStatus.Downloading;
    public bool IsIndeterminate => Status == DownloadStatus.Downloading && Progress <= 0;

    public string StatusText => Status switch
    {
        DownloadStatus.Pending => "En attente",
        DownloadStatus.Downloading => "Téléchargement…",
        DownloadStatus.Completed => "Terminé",
        DownloadStatus.Failed => ErrorMessage is null ? "Échec" : $"Échec : {ErrorMessage}",
        DownloadStatus.Canceled => "Annulé",
        _ => string.Empty
    };
}

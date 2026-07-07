using CommunityToolkit.Mvvm.ComponentModel;

namespace DoodleDrive.ViewModels;

public enum UploadStatus { Pending, Uploading, Completed, Failed, Canceled }

/// <summary>Un fichier dans la file d'attente d'envoi, avec sa progression.</summary>
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate))]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    private UploadStatus _status = UploadStatus.Pending;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string? _errorMessage;

    public bool IsActive => Status is UploadStatus.Pending or UploadStatus.Uploading;
    public bool IsIndeterminate => Status == UploadStatus.Uploading && Progress <= 0;

    public string StatusText => Status switch
    {
        UploadStatus.Pending => "En attente",
        UploadStatus.Uploading => "Envoi…",
        UploadStatus.Completed => "Terminé",
        UploadStatus.Failed => ErrorMessage is null ? "Échec" : $"Échec : {ErrorMessage}",
        UploadStatus.Canceled => "Annulé",
        _ => string.Empty
    };
}

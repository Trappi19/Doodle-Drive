using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DoodleDrive.Models;
using DoodleDrive.Services;

namespace DoodleDrive.ViewModels;

/// <summary>Navigateur de dossiers du drive (pour choisir un emplacement en ligne).</summary>
public sealed partial class RemoteFolderPickerViewModel : ObservableObject
{
    private readonly FtpService _ftp;
    private readonly string _rootPath;

    public RemoteFolderPickerViewModel(FtpService ftp, string rootPath, string localFolderName)
    {
        _ftp = ftp;
        _rootPath = FtpPathUtil.Normalize(rootPath);
        LocalFolderName = localFolderName;
    }

    public ObservableCollection<FolderNode> Roots { get; } = new();

    [ObservableProperty] private FolderNode? _selectedNode;
    [ObservableProperty] private bool _isBusy;

    /// <summary>Créer un sous-dossier au nom du dossier local (comportement « uploader le dossier »).</summary>
    [ObservableProperty] private bool _createSubfolder = true;

    public string LocalFolderName { get; }

    /// <summary>Chemin en ligne choisi, sous-dossier compris si l'option est cochée.</summary>
    public string? ResolvedPath
    {
        get
        {
            var basePath = SelectedNode?.FtpPath ?? _rootPath;
            return CreateSubfolder ? FtpPathUtil.Combine(basePath, LocalFolderName) : basePath;
        }
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Roots.Clear();
            var root = new FolderNode(new Folder { Id = 0, Name = "Tout le drive", FtpPath = _rootPath }, FolderAccessLevel.Owner)
            {
                ChildrenLoaded = true,
                IsExpanded = true
            };
            Roots.Add(root);
            await LoadChildrenAsync(root);
            SelectedNode = root;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadChildrenAsync(FolderNode node)
    {
        node.ChildrenLoaded = true;
        try
        {
            var dirs = (await _ftp.ListAsync(node.FtpPath))
                .Where(e => e.IsDirectory)
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase);
            node.Children.Clear();
            foreach (var d in dirs)
                node.Children.Add(CreateLazyNode(d.Name, d.FullPath));
        }
        catch
        {
            node.ChildrenLoaded = false; // échec : on pourra réessayer au prochain dépliage
        }
    }

    private FolderNode CreateLazyNode(string name, string path)
    {
        var node = new FolderNode(new Folder { Id = 0, Name = name, FtpPath = path }, FolderAccessLevel.Owner)
        {
            ChildrenLoaded = false,
            IsExpanded = false
        };
        node.Children.Add(FolderNode.CreatePlaceholder());
        node.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FolderNode.IsExpanded) && node.IsExpanded && !node.ChildrenLoaded)
                _ = LoadChildrenAsync(node);
        };
        return node;
    }
}

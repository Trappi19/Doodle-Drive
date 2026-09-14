using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DoodleDrive.Models;
using DoodleDrive.ViewModels;

namespace DoodleDrive.Views;

public partial class FilesView : UserControl
{
    private string _lastAnimatedPath = string.Empty;

    // Glisser-déposer interne (déplacement d'une sélection vers un dossier).
    private const string EntriesDragFormat = "DoodleDrive.EntryList";
    private Point _dragStartPoint;
    private bool _maybeDragging;
    private List<FileEntryViewModel> _dragSnapshot = new();

    public FilesView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private FilesViewModel? Vm => DataContext as FilesViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is FilesViewModel oldVm) oldVm.PropertyChanged -= OnVmPropertyChanged;
        if (e.NewValue is FilesViewModel newVm) newVm.PropertyChanged += OnVmPropertyChanged;
    }

    /// <summary>
    /// Anime l'entrée du contenu à chaque changement de dossier : glissement depuis
    /// la droite en descendant, depuis la gauche en remontant (comme sur mobile).
    /// </summary>
    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FilesViewModel.CurrentPath) || Vm is null) return;

        var newPath = Vm.CurrentPath;
        if (newPath == _lastAnimatedPath) return;

        var direction = Depth(newPath).CompareTo(Depth(_lastAnimatedPath));
        var isFirstLoad = _lastAnimatedPath.Length == 0;
        _lastAnimatedPath = newPath;
        if (isFirstLoad) return; // pas d'animation au tout premier affichage

        AnimateContentEntry(direction);
    }

    private static int Depth(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;

    private void AnimateContentEntry(int direction)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(260));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var slide = new TranslateTransform(direction * 48, 0);
        ContentHost.RenderTransform = slide;
        slide.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(direction * 48, 0, duration) { EasingFunction = ease });
        ContentHost.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
    }

    private void FolderTreeView_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (Vm is not null && e.NewValue is FolderNode node)
            Vm.SelectedFolderNode = node;
    }

    private void Content_OnDragOver(object sender, DragEventArgs e)
    {
        // Fichiers venant de Windows -> envoi (copie). Le déplacement interne est géré
        // par les dossiers de la grille/l'arbre (ci-dessous), pas par la zone de fond.
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Content_OnDrop(object sender, DragEventArgs e)
    {
        if (Vm is null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            await Vm.HandleDropAsync(paths);
    }

    // ===================== Glisser-déposer interne (déplacement) =====================

    private void Items_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        var pressed = FindData<FileEntryViewModel>(e.OriginalSource as DependencyObject);
        _maybeDragging = pressed is not null;

        // On capture la sélection MAINTENANT : un mousedown sur un élément déjà sélectionné
        // fait réduire la sélection du ListBox à ce seul élément juste après — trop tard pour
        // récupérer les autres. Si l'élément saisi fait partie de la sélection, on garde tout.
        if (sender is ListBox list && pressed is not null)
        {
            var sel = list.SelectedItems.OfType<FileEntryViewModel>().ToList();
            _dragSnapshot = sel.Contains(pressed) && sel.Count > 0
                ? sel
                : new List<FileEntryViewModel> { pressed };
        }
        else
        {
            _dragSnapshot = pressed is null ? new() : new List<FileEntryViewModel> { pressed };
        }
    }

    private void Items_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_maybeDragging || e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _maybeDragging = false;
        if (_dragSnapshot.Count == 0) return;

        var set = new List<FileEntryViewModel>(_dragSnapshot);
        // Ré-affiche la sélection complète (le ListBox a pu la réduire au mousedown).
        foreach (var it in set) it.IsSelected = true;

        var data = new DataObject();
        data.SetData(EntriesDragFormat, set);
        try { DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move); }
        catch { /* glisser interrompu : sans conséquence */ }
    }

    private void Items_OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(EntriesDragFormat)) return; // laisse remonter le FileDrop (envoi)

        var target = FindData<FileEntryViewModel>(e.OriginalSource as DependencyObject);
        e.Effects = target is { IsDirectory: true } && !IsDragged(e, target)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Items_OnDrop(object sender, DragEventArgs e)
    {
        if (Vm is null || !e.Data.GetDataPresent(EntriesDragFormat)) return; // FileDrop -> Content_OnDrop
        e.Handled = true;

        var target = FindData<FileEntryViewModel>(e.OriginalSource as DependencyObject);
        if (target is not { IsDirectory: true }) return;
        if (e.Data.GetData(EntriesDragFormat) is not List<FileEntryViewModel> set || set.Contains(target)) return;

        await Vm.MoveEntriesAsync(set, target.FullPath);
    }

    private void Tree_OnDragOver(object sender, DragEventArgs e)
    {
        var node = FindData<FolderNode>(e.OriginalSource as DependencyObject);
        e.Effects = e.Data.GetDataPresent(EntriesDragFormat)
                    && node is { IsPlaceholder: false } && !string.IsNullOrEmpty(node.FtpPath)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Tree_OnDrop(object sender, DragEventArgs e)
    {
        if (Vm is null || !e.Data.GetDataPresent(EntriesDragFormat)) return;
        e.Handled = true;

        var node = FindData<FolderNode>(e.OriginalSource as DependencyObject);
        if (node is null || node.IsPlaceholder || string.IsNullOrEmpty(node.FtpPath)) return;
        if (e.Data.GetData(EntriesDragFormat) is not List<FileEntryViewModel> set) return;

        await Vm.MoveEntriesAsync(set, node.FtpPath);
    }

    private static bool IsDragged(DragEventArgs e, FileEntryViewModel target) =>
        e.Data.GetData(EntriesDragFormat) is List<FileEntryViewModel> set && set.Contains(target);

    /// <summary>Remonte l'arbre visuel jusqu'à trouver un élément dont le DataContext est un <typeparamref name="T"/>.</summary>
    private static T? FindData<T>(DependencyObject? src) where T : class
    {
        while (src is not null)
        {
            if (src is FrameworkElement { DataContext: T match }) return match;
            src = VisualTreeHelper.GetParent(src);
        }
        return null;
    }

    private async void DetailItem_OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm is not null && sender is ListViewItem { DataContext: FileEntryViewModel entry })
            await Vm.OpenEntryCommand.ExecuteAsync(entry);
    }

    /// <summary>Boutons latéraux de la souris = précédent/suivant, comme dans l'Explorateur.</summary>
    private async void FilesView_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm is null) return;

        if (e.ChangedButton == MouseButton.XButton1 && Vm.NavigateBackCommand.CanExecute(null))
        {
            e.Handled = true;
            await Vm.NavigateBackCommand.ExecuteAsync(null);
        }
        else if (e.ChangedButton == MouseButton.XButton2 && Vm.NavigateForwardCommand.CanExecute(null))
        {
            e.Handled = true;
            await Vm.NavigateForwardCommand.ExecuteAsync(null);
        }
    }
}

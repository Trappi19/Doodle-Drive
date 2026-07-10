using System.ComponentModel;
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
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Content_OnDrop(object sender, DragEventArgs e)
    {
        if (Vm is null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            await Vm.HandleDropAsync(paths);
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

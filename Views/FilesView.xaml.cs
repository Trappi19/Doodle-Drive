using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DoodleDrive.Models;
using DoodleDrive.ViewModels;

namespace DoodleDrive.Views;

public partial class FilesView : UserControl
{
    public FilesView() => InitializeComponent();

    private FilesViewModel? Vm => DataContext as FilesViewModel;

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
}

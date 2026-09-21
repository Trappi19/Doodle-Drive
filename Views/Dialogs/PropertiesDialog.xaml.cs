using System.Collections.Generic;
using DoodleDrive.ViewModels;
using Wpf.Ui.Controls;

namespace DoodleDrive.Views.Dialogs;

public partial class PropertiesDialog : FluentWindow
{
    public PropertiesDialog(IReadOnlyList<FileEntryViewModel> items)
    {
        InitializeComponent();
        DataContext = new PropertiesViewModel(items);
    }
}

using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace DoodleDrive.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        // Suit automatiquement le thème clair/sombre de Windows si l'option "System" est active.
        SystemThemeWatcher.Watch(this);
    }
}

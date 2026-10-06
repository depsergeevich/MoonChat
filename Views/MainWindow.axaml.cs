using Avalonia.Controls;
using MoonChat.Client.Services;
using MoonChat.Client.ViewModels;

namespace MoonChat.Client.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Kicks off the session restore result and the initial chat load.
        (DataContext as MainWindowViewModel)?.Start();
    }
}
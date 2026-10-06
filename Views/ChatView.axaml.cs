using Avalonia.Controls;
using Avalonia.Input;
using MoonChat.Client.ViewModels;

namespace MoonChat.Client.Views;

public partial class ChatView : UserControl
{
    public ChatView() => InitializeComponent();

    /// <summary>
    /// Enter sends, Shift+Enter inserts a newline. Any other key still reaches
    /// the view model so the typing indicator goes out.
    /// </summary>
    private async void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ChatViewModel vm)
        {
            return;
        }

        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            // Consume it, otherwise the newline is inserted as well.
            e.Handled = true;
        }

        await vm.SubmitComposerAsync();
    }
}
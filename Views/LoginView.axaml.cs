using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MoonChat.Client.ViewModels;

namespace MoonChat.Client.Views;

public partial class LoginView : UserControl
{
    public LoginView() => InitializeComponent();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Enter && DataContext is LoginViewModel vm)
        {
            if (vm.SignInCommand.CanExecute(null))
            {
                vm.SignInCommand.Execute(null);
            }

            e.Handled = true;
        }
    }
}
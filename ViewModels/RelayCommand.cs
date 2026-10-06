using System.Windows.Input;

namespace MoonChat.Client.ViewModels;

/// <summary>
/// Tiny command wrapper. The app has a handful of actions, so pulling in a
/// full MVVM framework would be more ceremony than it saves.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        : this(
            _ =>
            {
                execute(_);
                return Task.CompletedTask;
            },
            canExecute)
    {
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        try
        {
            await _execute(parameter);
        }
        catch (Exception ex)
        {
            // A command throwing would otherwise surface as an unobserved
            // task exception on the dispatcher.
            System.Diagnostics.Debug.WriteLine("command failed: " + ex);
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
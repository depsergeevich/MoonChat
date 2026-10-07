using MoonChat.Client.ViewModels;

namespace MoonChat.Client.Tests;

/// <summary>
/// <see cref="RelayCommand"/> is the app's only ICommand. It is deliberately
/// <c>async void</c> (it has to satisfy <see cref="System.Windows.Input.ICommand"/>),
/// so these tests assert on observable side effects rather than on the returned
/// task, and use <see cref="Wait"/> to settle asynchronous work.
/// </summary>
public class RelayCommandTests
{
    [Fact]
    public void Execute_invokes_the_synchronous_action()
    {
        var calls = 0;
        var command = new RelayCommand(_ => calls++);

        command.Execute(null);

        Assert.Equal(1, calls);
    }

    [Fact]
    public void Execute_passes_the_parameter_through()
    {
        object? seen = null;
        var command = new RelayCommand(p => seen = p);

        command.Execute("hello");

        Assert.Equal("hello", seen);
    }

    [Fact]
    public void Execute_passes_a_null_parameter_through()
    {
        var wasNull = false;
        var command = new RelayCommand(p => wasNull = p is null);

        command.Execute(null);

        Assert.True(wasNull);
    }

    [Fact]
    public void Execute_supports_an_asynchronous_action()
    {
        var done = false;
        var command = new RelayCommand(async _ =>
        {
            await Task.Yield();
            done = true;
        });

        command.Execute(null);

        Assert.True(Wait.Until(() => done), "the async action never completed");
    }

    [Fact]
    public void CanExecute_is_true_when_no_predicate_is_given()
    {
        var command = new RelayCommand(_ => { });

        Assert.True(command.CanExecute(null));
        Assert.True(command.CanExecute("anything"));
    }

    [Fact]
    public void CanExecute_delegates_to_the_predicate()
    {
        var command = new RelayCommand(_ => { }, _ => false);

        Assert.False(command.CanExecute(null));
    }

    [Fact]
    public void Execute_does_nothing_when_CanExecute_is_false()
    {
        var calls = 0;
        var command = new RelayCommand(_ => calls++, _ => false);

        command.Execute(null);

        Assert.Equal(0, calls);
    }

    [Fact]
    public void Execute_runs_when_CanExecute_is_true()
    {
        var calls = 0;
        var command = new RelayCommand(_ => calls++, _ => true);

        command.Execute(null);

        Assert.Equal(1, calls);
    }

    [Fact]
    public void CanExecute_receives_the_parameter()
    {
        var command = new RelayCommand(_ => { }, p => p is string s && s.Length > 2);

        Assert.False(command.CanExecute("ab"));
        Assert.True(command.CanExecute("abc"));
    }

    [Fact]
    public void RaiseCanExecuteChanged_raises_the_event()
    {
        var command = new RelayCommand(_ => { });
        var raised = 0;
        command.CanExecuteChanged += (_, _) => raised++;

        command.RaiseCanExecuteChanged();

        Assert.Equal(1, raised);
    }

    [Fact]
    public void CanExecuteChanged_is_not_raised_by_Execute_itself()
    {
        // Documents the contract the view models rely on: they must call
        // RaiseCanExecuteChanged explicitly after mutating the gating state.
        var command = new RelayCommand(_ => { });
        var raised = 0;
        command.CanExecuteChanged += (_, _) => raised++;

        command.Execute(null);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void RaiseCanExecuteChanged_passes_the_command_as_sender()
    {
        var command = new RelayCommand(_ => { });
        object? sender = null;
        command.CanExecuteChanged += (s, _) => sender = s;

        command.RaiseCanExecuteChanged();

        Assert.Same(command, sender);
    }

    [Fact]
    public void Execute_swallows_a_synchronous_exception()
    {
        var command = new RelayCommand(_ => throw new InvalidOperationException("boom"));

        // The whole point of the try/catch: an unobserved async void exception
        // would otherwise tear down the dispatcher.
        var exception = Record.Exception(() => command.Execute(null));

        Assert.Null(exception);
    }

    [Fact]
    public void Execute_swallows_an_asynchronous_exception()
    {
        var command = new RelayCommand(async _ =>
        {
            await Task.Yield();
            throw new InvalidOperationException("boom");
        });

        // Nothing to assert beyond "the process survives". If the catch were
        // removed, this would surface as an unhandled task exception.
        command.Execute(null);

        Assert.True(Wait.Until(() => true), "sanity");
    }

    [Fact]
    public void multiple_subscribers_all_receive_CanExecuteChanged()
    {
        var command = new RelayCommand(_ => { });
        var count = 0;
        command.CanExecuteChanged += (_, _) => count++;
        command.CanExecuteChanged += (_, _) => count++;

        command.RaiseCanExecuteChanged();

        Assert.Equal(2, count);
    }

    [Fact]
    public void disposing_a_subscriber_stops_notifications()
    {
        var command = new RelayCommand(_ => { });
        var count = 0;
        void Handler(object? _, EventArgs __) => count++;

        command.CanExecuteChanged += Handler;
        command.RaiseCanExecuteChanged();
        command.CanExecuteChanged -= Handler;
        command.RaiseCanExecuteChanged();

        Assert.Equal(1, count);
    }
}
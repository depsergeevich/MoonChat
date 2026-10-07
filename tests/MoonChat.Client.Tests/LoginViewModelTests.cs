using System.Net;
using System.Text;
using MoonChat.Client.Services;
using MoonChat.Client.ViewModels;

namespace MoonChat.Client.Tests;

/// <summary>
/// <see cref="LoginViewModel"/> holds the submit-gating rules. A wrong gate is
/// the difference between "the button does nothing" and "signs in with an empty
/// username", so the rules are pinned here.
///
/// Every request goes through an in-memory handler, so these run with no network
/// and no waiting on real I/O.
/// </summary>
public class LoginViewModelTests : IDisposable
{
    private readonly TempSessionStore _temp = TempSessionStore.Create();
    private readonly SingleSessionGuard _guard = SingleSessionGuard.TryAcquire();

    public void Dispose()
    {
        _guard.Dispose();
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private LoginViewModel Create(
        out StubHandler handler,
        Func<HttpRequestMessage, HttpResponseMessage>? responder = null)
    {
        handler = responder is null ? StubHandler.Json("{}") : new StubHandler(responder);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080/") };
        var api = new ApiClient(http, _temp.Store);
        return new LoginViewModel(api, _temp.Store, new RecordingDiagnostics(), _guard);
    }

    private LoginViewModel Create() => Create(out _);

    // --- submit gating ---

    [Fact]
    public void submit_is_blocked_while_every_field_is_empty()
        => Assert.False(Create().SignInCommand.CanExecute(null));

    [Fact]
    public void submit_needs_a_username()
    {
        var vm = Create();

        vm.Password = "secret";

        Assert.False(vm.SignInCommand.CanExecute(null));
    }

    [Fact]
    public void submit_needs_a_password()
    {
        var vm = Create();

        vm.Username = "ada";

        Assert.False(vm.SignInCommand.CanExecute(null));
    }

    [Fact]
    public void submit_is_enabled_with_a_username_and_password()
    {
        var vm = Create();

        vm.Username = "ada";
        vm.Password = "secret";

        Assert.True(vm.SignInCommand.CanExecute(null));
    }

    [Fact]
    public void submit_treats_a_whitespace_username_as_empty()
    {
        var vm = Create();

        vm.Username = "   ";
        vm.Password = "secret";

        Assert.False(vm.SignInCommand.CanExecute(null));
    }

    [Fact]
    public void submit_treats_a_whitespace_password_as_empty()
    {
        var vm = Create();

        vm.Username = "ada";
        vm.Password = "   ";

        Assert.False(vm.SignInCommand.CanExecute(null));
    }

    [Fact]
    public void registration_also_requires_a_display_name()
    {
        var vm = Create();
        vm.Username = "ada";
        vm.Password = "secret";

        vm.ToggleModeCommand.Execute(null);
        Assert.True(vm.IsRegistering);

        Assert.False(vm.SignInCommand.CanExecute(null));

        vm.DisplayName = "Ada Lovelace";

        Assert.True(vm.SignInCommand.CanExecute(null));
    }

    [Fact]
    public void signing_in_does_not_require_a_display_name()
    {
        var vm = Create();
        vm.Username = "ada";
        vm.Password = "secret";

        Assert.True(vm.SignInCommand.CanExecute(null));
    }

    // --- derived labels ---

    [Fact]
    public void sign_in_mode_labels_are_the_default()
    {
        var vm = Create();

        Assert.False(vm.IsRegistering);
        Assert.Equal("Sign in", vm.SubmitLabel);
        Assert.Equal("Welcome back", vm.Heading);
        Assert.Equal("New here? Create an account", vm.SwitchLabel);
        Assert.False(vm.IsDisplayNameVisible);
    }

    [Fact]
    public void toggling_the_mode_swaps_every_label()
    {
        var vm = Create();

        vm.ToggleModeCommand.Execute(null);

        Assert.True(vm.IsRegistering);
        Assert.Equal("Create account", vm.SubmitLabel);
        Assert.Equal("Create your account", vm.Heading);
        Assert.Equal("Already have an account? Sign in", vm.SwitchLabel);
        Assert.True(vm.IsDisplayNameVisible);

        vm.ToggleModeCommand.Execute(null);

        Assert.False(vm.IsRegistering);
        Assert.Equal("Sign in", vm.SubmitLabel);
    }

    [Fact]
    public void toggling_the_mode_clears_any_error()
    {
        // The stub must reject, otherwise the sign-in succeeds and there is no
        // error to clear.
        var vm = Create(out _, _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(
                """{"error":{"code":"invalid_credentials","message":"nope"}}""",
                Encoding.UTF8,
                "application/json"),
        });

        vm.Username = "ada";
        vm.Password = "bad";
        vm.SignInCommand.Execute(null);
        Assert.True(Wait.Until(() => vm.Error is not null), "the error never appeared");

        vm.ToggleModeCommand.Execute(null);

        Assert.Null(vm.Error);
    }

    [Fact]
    public void the_logo_falls_back_to_the_app_initial()
        => Assert.Equal("M", Create().LogoInitial);

    [Fact]
    public void the_logo_uses_the_first_letter_of_the_username()
    {
        var vm = Create();

        vm.Username = "ada";

        Assert.Equal("A", vm.LogoInitial);
    }

    [Fact]
    public void the_logo_uppercases_a_lowercase_initial()
    {
        var vm = Create();

        vm.Username = "bob";

        Assert.Equal("B", vm.LogoInitial);
    }

    // --- the session notice ---

    [Fact]
    public void a_window_that_owns_the_session_shows_no_notice()
    {
        // If another MoonChat window holds the lock this process is not the
        // owner, and the notice explains the otherwise-mysterious login screen.
        var vm = Create();

        if (_guard.IsOwner)
        {
            Assert.Null(vm.SessionNotice);
        }
        else
        {
            Assert.NotNull(vm.SessionNotice);
        }
    }

    // --- actual submission ---

    [Fact]
    public void a_successful_sign_in_raises_SignedIn_and_stores_the_session()
    {
        var vm = Create(
            out var handler,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"token":"jwt-abc","user":{"id":42,"username":"ada","displayName":"Ada Lovelace"}}""",
                    Encoding.UTF8,
                    "application/json"),
            });

        var signedIn = 0;
        vm.SignedIn = () =>
        {
            signedIn++;
            return Task.CompletedTask;
        };

        vm.Username = "  ada  ";
        vm.Password = "secret";
        vm.SignInCommand.Execute(null);

        Assert.True(Wait.Until(() => signedIn == 1), "SignedIn was never raised");

        // The username is trimmed before it goes on the wire.
        Assert.Contains("\"username\":\"ada\"", handler.Bodies[0]);
        Assert.Equal("http://localhost:8080/api/auth/login", handler.Requests[0].RequestUri!.AbsoluteUri);

        Assert.Equal("jwt-abc", _temp.Store.Token);
        Assert.Equal("Ada Lovelace", _temp.Store.User?.DisplayName);
    }

    [Fact]
    public void a_successful_sign_in_clears_the_password_field()
    {
        var vm = Create(out _, _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"token":"jwt-abc","user":{"id":42,"username":"ada"}}""",
                Encoding.UTF8,
                "application/json"),
        });

        vm.SignedIn = () => Task.CompletedTask;
        vm.Username = "ada";
        vm.Password = "secret";
        vm.SignInCommand.Execute(null);

        Assert.True(Wait.Until(() => vm.Password == ""), "the password field was not cleared");
    }

    [Fact]
    public void a_rejected_sign_in_surfaces_the_server_message()
    {
        var vm = Create(out _, _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(
                """{"error":{"code":"invalid_credentials","message":"Wrong username or password."}}""",
                Encoding.UTF8,
                "application/json"),
        });

        var signedIn = 0;
        vm.SignedIn = () =>
        {
            signedIn++;
            return Task.CompletedTask;
        };

        vm.Username = "ada";
        vm.Password = "nope";
        vm.SignInCommand.Execute(null);

        Assert.True(Wait.Until(() => vm.Error is not null), "the error was never set");
        Assert.Equal("Wrong username or password.", vm.Error);
        Assert.Equal(0, signedIn);
        Assert.False(_temp.Store.IsSignedIn);
    }

    [Fact]
    public void a_failed_sign_in_re_enables_the_form()
    {
        var vm = Create(out _, _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(
                """{"error":{"code":"invalid_credentials","message":"nope"}}""",
                Encoding.UTF8,
                "application/json"),
        });

        vm.Username = "ada";
        vm.Password = "bad";
        vm.SignInCommand.Execute(null);

        Assert.True(Wait.Until(() => vm.IsIdle), "the form stayed busy after a failed sign-in");
        Assert.True(vm.SignInCommand.CanExecute(null));
    }

    [Fact]
    public void an_unreachable_server_is_reported_without_storing_a_session()
    {
        var log = new RecordingDiagnostics();
        var http = new HttpClient(new StubHandler(_ => throw new HttpRequestException("connection refused")))
        {
            BaseAddress = new Uri("http://localhost:8080/"),
        };
        var vm = new LoginViewModel(new ApiClient(http, _temp.Store), _temp.Store, log, _guard);

        vm.Username = "ada";
        vm.Password = "secret";
        vm.SignInCommand.Execute(null);

        Assert.True(Wait.Until(() => vm.Error is not null), "the transport failure was never reported");
        Assert.Contains("connection refused", vm.Error!);
        Assert.NotEmpty(log.Entries);
        Assert.False(_temp.Store.IsSignedIn);
    }

    [Fact]
    public void executing_the_command_while_disabled_makes_no_request()
    {
        var vm = Create(out var handler);

        vm.SignInCommand.Execute(null);

        Assert.Empty(handler.Requests);
        Assert.Null(vm.Error);
    }

    [Fact]
    public void registering_posts_to_the_register_endpoint()
    {
        var vm = Create(
            out var handler,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"token":"jwt-new","user":{"id":9,"username":"grace","displayName":"Grace Hopper"}}""",
                    Encoding.UTF8,
                    "application/json"),
            });

        vm.SignedIn = () => Task.CompletedTask;
        vm.Username = "grace";
        vm.DisplayName = "Grace Hopper";
        vm.Password = "pw";
        vm.ToggleModeCommand.Execute(null);
        vm.SignInCommand.Execute(null);

        Assert.True(Wait.Until(() => _temp.Store.IsSignedIn), "the session was never stored");
        Assert.Equal("http://localhost:8080/api/auth/register", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Contains("\"displayName\":\"Grace Hopper\"", handler.Bodies[0]);
    }

    [Fact]
    public void toggling_the_mode_does_not_lose_the_typed_credentials()
    {
        var vm = Create();

        vm.Username = "ada";
        vm.DisplayName = "Ada";
        vm.Password = "secret";
        vm.ToggleModeCommand.Execute(null);
        vm.ToggleModeCommand.Execute(null);

        Assert.Equal("ada", vm.Username);
        Assert.Equal("Ada", vm.DisplayName);
        Assert.Equal("secret", vm.Password);
    }
}
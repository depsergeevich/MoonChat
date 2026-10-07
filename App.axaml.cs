using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using MoonChat.Client.Services;
using MoonChat.Client.ViewModels;
using MoonChat.Client.Views;

namespace MoonChat.Client;

/// <summary>
/// Application shell. Wires up the service graph and hosts the window; which
/// view is shown is decided by <see cref="MainWindowViewModel"/>.
/// </summary>
public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public IServiceProvider? Services { get; private set; }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();

        // Taken before anything touches the session: the first window up owns the
        // cached session, any later one starts signed out.
        var guard = SingleSessionGuard.TryAcquire();
        services.AddSingleton(guard);

        // AuthHandler reads the session, so the HttpClient is built by hand
        // rather than through AddHttpClient.
        services.AddSingleton(provider => new HttpClient(
                new AuthHandler(provider.GetRequiredService<SessionStore>()))
            {
                BaseAddress = new Uri(ApiBaseAddress),
                Timeout = TimeSpan.FromSeconds(30),
            });

        services.AddSingleton<SessionStore>();
        services.AddSingleton<ApiClient>();
        services.AddSingleton<AvatarCache>();
        services.AddSingleton<ChatState>();
        services.AddSingleton<IFilePicker, DesktopFilePicker>();
        services.AddSingleton<IDiagnostics, DiagnosticLog>();
        services.AddSingleton<LoginViewModel>();
        services.AddSingleton<ChatViewModel>();
        services.AddSingleton<ProfileViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        var provider = services.BuildServiceProvider();
        Services = provider;

        var log = provider.GetRequiredService<IDiagnostics>();
        log.Info($"Avalonia lifetime: {ApplicationLifetime?.GetType().Name ?? "(none)"}");

        // Restore before the first window shows, otherwise a returning user
        // would briefly see the sign-in screen.
        var session = provider.GetRequiredService<SessionStore>();
        if (guard.IsOwner)
        {
            session.Restore();
        }
        else
        {
            // Another window already holds the session. Drop whatever was cached
            // without deleting the file: it still belongs to that window, and
            // deleting it would sign the owner out on its next restart.
            session.DiscardRestored();
        }

        log.Info($"session guard: owner={guard.IsOwner}; signedIn={session.IsSignedIn}");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var shell = provider.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = shell };
            desktop.Exit += (_, _) => guard.Dispose();
            log.Info($"main window assigned: {desktop.MainWindow?.GetType().Name}");
        }
        else
        {
            log.Error("ApplicationLifetime is not a desktop lifetime; no window will be shown");
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Address of the MoonChat server. Overridable with MOONCHAT_API_BASE so a
    /// client can be pointed at a different host or port without a rebuild.
    /// </summary>
    private static string ApiBaseAddress
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("MOONCHAT_API_BASE");
            return string.IsNullOrWhiteSpace(configured)
                ? "http://127.0.0.1:8080/"
                : configured.TrimEnd('/') + "/";
        }
    }
}
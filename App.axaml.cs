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
        services.AddSingleton<IDiagnostics, DiagnosticLog>();
        services.AddSingleton<LoginViewModel>();
        services.AddSingleton<ChatViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        var provider = services.BuildServiceProvider();
        Services = provider;

        var log = provider.GetRequiredService<IDiagnostics>();
        log.Info($"Avalonia lifetime: {ApplicationLifetime?.GetType().Name ?? "(none)"}");

        // Restore before the first window shows, otherwise a returning user
        // would briefly see the sign-in screen.
        var session = provider.GetRequiredService<SessionStore>();
        session.Restore();
        log.Info($"session restored: signedIn={session.IsSignedIn}");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var shell = provider.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = shell };
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
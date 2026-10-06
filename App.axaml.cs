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

        // Restore before the first window shows, otherwise a returning user
        // would briefly see the sign-in screen.
        provider.GetRequiredService<SessionStore>().RestoreAsync().GetAwaiter().GetResult();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var shell = provider.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = shell };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Address of the MoonChat server.</summary>
    private const string ApiBaseAddress = "http://127.0.0.1:8080/";
}
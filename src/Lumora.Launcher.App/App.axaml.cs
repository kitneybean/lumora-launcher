using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Lumora.Launcher.App.Services;
using Lumora.Launcher.App.Services.Demo;
using Lumora.Launcher.App.ViewModels;
using Lumora.Launcher.App.Views;
using Lumora.Launcher.Core.Api;
using Lumora.Launcher.Core.Game;
using Lumora.Launcher.Core.Settings;
using Lumora.Launcher.Core.Status;

namespace Lumora.Launcher.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? [];
            var root = Option(args, "--data") ?? Environment.GetEnvironmentVariable("LUMORA_HOME") ?? LauncherPaths.DefaultRoot;
            ApplyBuiltInAuthServer();
            var store = new SettingsStore(new LauncherPaths(root));
            var services = args.Contains("--demo") ? CreateDemo(store) : CreateReal(store);
            services.ApplyTheme();

            var window = new MainWindow { DataContext = new MainWindowViewModel(services) };
            services.Platform.MainWindow = window;
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) => store.SaveGlobal(services.Settings);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Offline demo data (for screenshots and UI work): <c>--demo</c>.</summary>
    public static AppServices CreateDemo(SettingsStore store) =>
        new(store, new DemoLumoraApi(), new DemoGameService(), new DemoServerStatusService(), isDemo: true);

    private static AppServices CreateReal(SettingsStore store)
    {
        AppServices services = null!;
        var settings = store.LoadGlobal();
        var http = LumoraApiClient.CreateHttpClient(settings.ProxyUrl);
        var api = new LumoraApiClient(() => services.Settings.AuthServerUrl, http);
        var game = new GameService(api, store, () => services.Settings);
        services = new AppServices(store, api, game, new ServerListPing(), isDemo: false)
        {
            Sessions = new SessionStore(Path.Combine(store.Paths.Root, "session.dat")),
        };
        return services;
    }

    /// <summary>Uses the auth server baked into this build (LumoraAuthServer) as the default.</summary>
    private static void ApplyBuiltInAuthServer()
    {
        var url = System.Reflection.CustomAttributeExtensions.GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>(typeof(App).Assembly)
            .FirstOrDefault(a => a.Key == "LumoraAuthServer")?.Value;
        if (!string.IsNullOrWhiteSpace(url))
            LauncherSettings.DefaultAuthServerUrl = url.TrimEnd('/');
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : args.FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.Ordinal))?[(name.Length + 1)..];
    }
}

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Lumora.Launcher.App.Services;
using Lumora.Launcher.App.Services.Demo;
using Lumora.Launcher.App.ViewModels;
using Lumora.Launcher.App.Views;
using Lumora.Launcher.Core.Settings;

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
            var store = new SettingsStore(new LauncherPaths(root));

            // Until the Lumora Auth API and CmlLib integration land, the launcher runs on demo services.
            var services = new AppServices(store, new DemoLumoraApi(), new DemoGameService(), new DemoServerStatusService(), isDemo: true);
            services.ApplyTheme();

            var window = new MainWindow { DataContext = new MainWindowViewModel(services) };
            services.Platform.MainWindow = window;
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) => store.SaveGlobal(services.Settings);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : args.FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.Ordinal))?[(name.Length + 1)..];
    }
}

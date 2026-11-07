// Renders launcher screens to PNG without a display (Avalonia headless + Skia).
// Usage: dotnet run --project tools/Lumora.Launcher.Screenshots -- [output-dir]
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lumora.Launcher.App;
using Lumora.Launcher.App.Services;
using Lumora.Launcher.App.Services.Demo;
using Lumora.Launcher.App.ViewModels;
using Lumora.Launcher.App.Views;
using Lumora.Launcher.Core.Settings;

var output = Path.GetFullPath(args.Length > 0 ? args[0] : "docs/screenshots");
Directory.CreateDirectory(output);

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();

var dataDir = Path.Combine(Path.GetTempPath(), "lumora-screenshots-" + Guid.NewGuid().ToString("N"));
var services = new AppServices(new SettingsStore(new LauncherPaths(dataDir)), new DemoLumoraApi(), new DemoGameService(), new DemoServerStatusService(), isDemo: true);
var root = new MainWindowViewModel(services);
var window = new MainWindow { DataContext = root, Width = 1180, Height = 740 };
services.Platform.MainWindow = window;
window.Show();

void Pump(int ms)
{
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < ms)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Thread.Sleep(15);
    }
}

void WaitFor(Func<bool> condition, int timeoutMs = 10000)
{
    var sw = Stopwatch.StartNew();
    while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
        Pump(30);
}

void Shot(string name)
{
    Pump(250);
    var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered");
    var path = Path.Combine(output, name + ".png");
    frame.Save(path, quality: null);
    Console.WriteLine(path);
}

T Find<T>() where T : Control => window.GetVisualDescendants().OfType<T>().First();

// 1. Login
Pump(300);
Shot("01-login");

var login = (LoginViewModel)root.CurrentScreen;

// 1b. Long error must wrap inside its box.
login.Error = "Не удалось связаться с https://mc.4ntares.ru. Проверьте интернет или адрес сервера внизу.";
Pump(200);
Shot("01b-login-error");
login.Error = null;

login.Login = "Steve";
login.Password = "demo";
login.LoginCommand.Execute(null);
WaitFor(() => root.CurrentScreen is ShellViewModel);
var shell = (ShellViewModel)root.CurrentScreen;

// 2. Servers with live status
WaitFor(() => shell.Servers.Servers.Count > 0 && shell.Servers.Servers.All(s => s.Status is not null));
Shot("02-servers");

// 3. Installing a build
var tech = shell.Servers.Servers.First(s => s.Name.Contains("Tech"));
tech.PrimaryCommand.Execute(null);
Pump(1800);
Shot("03-installing");

// 4. Build settings: optional mods
var survival = shell.Servers.Servers.First(s => s.Name.Contains("Survival"));
survival.OpenSettingsCommand.Execute(null);
Shot("04-build-mods");

// 5. Global settings: memory tab
shell.GoTo(shell.Settings);
Pump(200);
Find<TabControl>().SelectedIndex = 1;
Shot("05-settings-memory");

Find<TabControl>().SelectedIndex = 3;
Shot("06-settings-launcher");

// 6. Skin
shell.GoTo(shell.Skin);
Shot("07-skin");

// 7. Console while the game "runs"
shell.GoTo(shell.Servers);
Pump(200);
survival.PrimaryCommand.Execute(null);
shell.ShowConsole();
Pump(3500);
Shot("08-console");

// 8. Light theme
services.Settings.Theme = "light";
services.ApplyTheme();
shell.GoTo(shell.Servers);
WaitFor(() => !tech.IsInstalling);
Shot("09-servers-light");

window.Close();
try { Directory.Delete(dataDir, recursive: true); } catch (IOException) { }

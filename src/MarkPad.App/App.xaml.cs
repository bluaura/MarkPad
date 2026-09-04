using MarkPad.App.Services;
using MarkPad.Core.Assets;
using MarkPad.Core.Documents;
using MarkPad.Core.Mru;
using MarkPad.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Serilog;

namespace MarkPad.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
        UnhandledException += OnUnhandledException;
    }

    /// <summary>Application-wide DI container (ARCHITECTURE.md §1).</summary>
    public IServiceProvider Services { get; }

    public static new App Current => (App)Application.Current;

    public static string LocalDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MarkPad");

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Directory.CreateDirectory(LocalDataDir);
        Services.GetRequiredService<SettingsStore>().Load();
        Services.GetRequiredService<RecentFilesStore>().Load();
        Services.GetRequiredService<AssetService>().CleanupStale(TimeSpan.FromDays(7));

        // Warm up the shared WebView2 environment while the shell is being built (ARCHITECTURE §7.3).
        _ = WebViewEnvironment.GetAsync();

        _window = new MainWindow();
        _window.Activate();

        // Later activations (Explorer double-click while running) arrive here (ADR-10).
        ActivationService.Start(_window.DispatcherQueue);
        ActivationService.FilesActivated += files =>
        {
            _window.AppWindow.Show();
            _window.Activate();
            _ = _window.OpenFilesAsync(files);
        };

        var initial = ActivationService.GetInitialFiles();
        if (initial.Count > 0)
        {
            _ = _window.OpenFilesAsync(initial);
        }
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSerilog(LoggingSetup.CreateLogger(), dispose: true));
        services.AddSingleton<DocumentIO>();
        services.AddSingleton(new SettingsStore(Path.Combine(LocalDataDir, "settings.json")));
        services.AddSingleton(new RecentFilesStore(Path.Combine(LocalDataDir, "recent.json")));
        services.AddSingleton<ThemeService>();
        services.AddSingleton<JumpListService>();
        services.AddSingleton(sp => new AssetService(
            Path.Combine(LocalDataDir, "pending-assets"),
            () => sp.GetRequiredService<SettingsStore>().Current.Images.FolderName));
        return services.BuildServiceProvider();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Services.GetRequiredService<ILogger<App>>().LogCritical(e.Exception, "Unhandled exception");
        // Recovery snapshot flush arrives with T-42.
    }
}

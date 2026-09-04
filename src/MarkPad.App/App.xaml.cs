using MarkPad.App.Services;
using MarkPad.Core.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Serilog;

namespace MarkPad.App;

public partial class App : Application
{
    private Window? _window;

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
        // Warm up the shared WebView2 environment while the shell is being built (ARCHITECTURE §7.3).
        _ = WebViewEnvironment.GetAsync();

        _window = new MainWindow();
        _window.Activate();

        var files = Environment.GetCommandLineArgs().Skip(1).Where(File.Exists).ToArray();
        if (files.Length > 0 && _window is MainWindow main)
        {
            _ = main.OpenFileAsync(files[0]);
        }
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSerilog(LoggingSetup.CreateLogger(), dispose: true));
        services.AddSingleton<DocumentIO>();
        return services.BuildServiceProvider();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Services.GetRequiredService<ILogger<App>>().LogCritical(e.Exception, "Unhandled exception");
        // Recovery snapshot flush arrives with T-42.
    }
}

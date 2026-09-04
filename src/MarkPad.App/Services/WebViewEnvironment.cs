using Microsoft.Web.WebView2.Core;

namespace MarkPad.App.Services;

/// <summary>
/// One <see cref="CoreWebView2Environment"/> for the whole app (ARCHITECTURE.md §2.2): every tab's WebView2
/// shares the browser process; only renderer processes scale with tab count.
/// </summary>
public static class WebViewEnvironment
{
    private static readonly Lazy<Task<CoreWebView2Environment>> s_environment = new(CreateAsync);

    public static string UserDataFolder { get; } = Path.Combine(App.LocalDataDir, "webview2");

    /// <summary>Folder that holds the Vite bundle (index.html, editor.js, index.css).</summary>
    public static string EditorAssetsDir { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "editor");

    public static Task<CoreWebView2Environment> GetAsync() => s_environment.Value;

    /// <summary>Minimum Evergreen runtime we accept (Chromium major). Checked at startup (ARCHITECTURE §1).</summary>
    public const int MinimumRuntimeMajor = 120;

    public static string? InstalledRuntimeVersion()
    {
        try
        {
            return CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or FileNotFoundException)
        {
            return null;
        }
    }

    private static async Task<CoreWebView2Environment> CreateAsync()
    {
        Directory.CreateDirectory(UserDataFolder);
        var options = new CoreWebView2EnvironmentOptions
        {
            // Korean UI strings inside WebView2 (context menus are disabled anyway), and no tracking.
            Language = "ko-KR",
            AdditionalBrowserArguments = "--disable-features=msSmartScreenProtection",
        };
        return await CoreWebView2Environment.CreateWithOptionsAsync(browserExecutableFolder: null, UserDataFolder, options);
    }
}

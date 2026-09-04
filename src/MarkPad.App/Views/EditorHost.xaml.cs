using MarkPad.App.Bridge;
using MarkPad.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.System;

namespace MarkPad.App.Views;

/// <summary>
/// Hosts the Milkdown bundle in a WebView2 (ARCHITECTURE.md §3.2 / §4.1): shared environment, virtual host
/// mapping for the bundle and the document folder, navigation lock-down, and the <see cref="EditorBridge"/>.
/// </summary>
public sealed partial class EditorHost : UserControl, IAsyncDisposable
{
    public const string AppHost = "app.markpad";
    public const string DocHost = "doc.markpad";
    private static readonly Uri s_appUri = new($"https://{AppHost}/index.html");

    private readonly ILogger<EditorHost> _log = App.Current.Services.GetRequiredService<ILogger<EditorHost>>();
    private EditorBridge? _bridge;
    private string? _mappedDocDir;
    private bool _disposed;

    public EditorHost()
    {
        InitializeComponent();
    }

    public EditorBridge Bridge => _bridge ?? throw new BridgeException(BridgeException.NotReady, "editor not initialized");

    public bool IsInitialized => _bridge is not null;

    public event EventHandler<ChangedEvent>? Changed;
    public event EventHandler<SelectionContext>? SelectionChanged;
    public event EventHandler<ShortcutEvent>? ShortcutRequested;
    public event EventHandler? RendererCrashed;

    /// <summary>Creates the CoreWebView2 (shared environment), applies security settings and navigates to the bundle.</summary>
    public async Task InitializeAsync()
    {
        if (_bridge is not null) return;

        var env = await WebViewEnvironment.GetAsync();
        await Web.EnsureCoreWebView2Async(env);
        var core = Web.CoreWebView2;

        var s = core.Settings;
        s.IsWebMessageEnabled = true;
        s.AreDefaultContextMenusEnabled = false;   // JS-side context menu (ARCHITECTURE §4.1)
        s.AreBrowserAcceleratorKeysEnabled = false; // Ctrl+F/P/O… go through the JS host keymap (ADR-06)
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;
        s.IsPinchZoomEnabled = false;
        s.AreDefaultScriptDialogsEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.IsGeneralAutofillEnabled = false;
#if DEBUG
        s.AreDevToolsEnabled = true;
#else
        s.AreDevToolsEnabled = false;
#endif

        core.SetVirtualHostNameToFolderMapping(AppHost, WebViewEnvironment.EditorAssetsDir, CoreWebView2HostResourceAccessKind.DenyCors);
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.ProcessFailed += OnProcessFailed;

        _bridge = new EditorBridge(core, DispatcherQueue, _log);
        _bridge.EventReceived += OnBridgeEvent;
        RegisterHostHandlers(_bridge);

        var devUrl = Environment.GetEnvironmentVariable("MARKPAD_EDITOR_URL");
        var target = string.IsNullOrWhiteSpace(devUrl) ? s_appUri : new Uri(devUrl);
        _log.LogInformation("editor: navigating to {Url}", target);
        core.Navigate(target.ToString());

        await _bridge.WhenReady;
        Spinner.IsActive = false;
        Spinner.Visibility = Visibility.Collapsed;
    }

    /// <summary>Maps <c>doc.markpad</c> to the document's folder so relative image paths resolve (ADR-05).</summary>
    public void MapDocumentFolder(string? directory)
    {
        var core = Web.CoreWebView2;
        if (core is null) return;
        if (string.Equals(_mappedDocDir, directory, StringComparison.OrdinalIgnoreCase)) return;
        if (_mappedDocDir is not null)
        {
            core.ClearVirtualHostNameToFolderMapping(DocHost);
            _mappedDocDir = null;
        }
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            core.SetVirtualHostNameToFolderMapping(DocHost, directory, CoreWebView2HostResourceAccessKind.Allow);
            _mappedDocDir = directory;
        }
    }

    public Task LoadAsync(string textLf, string? path, bool readOnly, EditorSettingsDto settings)
    {
        MapDocumentFolder(path is null ? null : Path.GetDirectoryName(path));
        return Bridge.CallAsync("doc.load", new DocLoadParams(textLf, path, readOnly, settings));
    }

    public async Task<string> GetMarkdownAsync()
    {
        var r = await Bridge.CallAsync<DocGetMarkdownResult>("doc.getMarkdown");
        return r?.Text ?? string.Empty;
    }

    public async Task<DocSerializeResult> SerializeForSaveAsync(string originalLf)
    {
        var r = await Bridge.CallAsync<DocSerializeResult>("doc.serializeForSave", new DocSerializeParams(originalLf));
        return r ?? new DocSerializeResult(string.Empty, []);
    }

    public Task ExecuteAsync(string method, object? parameters = null) => Bridge.CallAsync(method, parameters);

    public void FocusEditor()
    {
        Web.Focus(FocusState.Programmatic);
        _ = Bridge.CallAsync("view.focus");
    }

    private void RegisterHostHandlers(EditorBridge bridge)
    {
        bridge.RegisterHandler("link.open", async (p, _) =>
        {
            var href = p?.TryGetProperty("href", out var h) == true ? h.GetString() : null;
            if (Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto")
            {
                await Launcher.LaunchUriAsync(uri);
                return new LinkOpenResult(true);
            }
            return new LinkOpenResult(false); // relative .md links open in a new tab from T-25 on
        });

        bridge.RegisterHandler("image.resolve", (p, _) =>
        {
            // Drive-letter mapping arrives with T-24; until then absolute paths are passed through.
            var src = p?.TryGetProperty("src", out var s) == true ? s.GetString() ?? "" : "";
            return Task.FromResult<object?>(new ImageResolveResult(src));
        });
    }

    private void OnBridgeEvent(object? sender, BridgeEventArgs e)
    {
        switch (e.Method)
        {
            case "changed":
                if (e.PayloadAs<ChangedEvent>() is { } c) Changed?.Invoke(this, c);
                break;
            case "selection":
                if (e.PayloadAs<SelectionContext>() is { } sel) SelectionChanged?.Invoke(this, sel);
                break;
            case "shortcut":
                if (e.PayloadAs<ShortcutEvent>() is { } sc) ShortcutRequested?.Invoke(this, sc);
                break;
        }
    }

    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri)) { args.Cancel = true; return; }
        var devUrl = Environment.GetEnvironmentVariable("MARKPAD_EDITOR_URL");
        var allowed = uri.Host.Equals(AppHost, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(devUrl) && Uri.TryCreate(devUrl, UriKind.Absolute, out var dev) && dev.Host == uri.Host);
        if (!allowed)
        {
            args.Cancel = true;
            _log.LogWarning("editor: blocked navigation to {Uri}", args.Uri);
        }
    }

    private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            _ = Launcher.LaunchUriAsync(uri);
        }
    }

    private void OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args)
    {
        _log.LogError("editor: WebView2 process failed ({Kind}, {Reason})", args.ProcessFailedKind, args.Reason);
        RendererCrashed?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_bridge is not null)
        {
            _bridge.EventReceived -= OnBridgeEvent;
            await _bridge.DisposeAsync();
            _bridge = null;
        }
        var core = Web.CoreWebView2;
        if (core is not null)
        {
            core.NavigationStarting -= OnNavigationStarting;
            core.NewWindowRequested -= OnNewWindowRequested;
            core.ProcessFailed -= OnProcessFailed;
        }
        Web.Close();
    }
}

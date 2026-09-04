using System.Text.Json;
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

    /// <summary>Raw CoreWebView2 for services that need browser APIs (PDF printing).</summary>
    public CoreWebView2? CoreWebView2 => Web.CoreWebView2;

    public event EventHandler<ChangedEvent>? Changed;
    public event EventHandler<SelectionContext>? SelectionChanged;
    public event EventHandler<ShortcutEvent>? ShortcutRequested;
    public event EventHandler<Editing.DroppedTextFile>? TextFileDropped;
    public event EventHandler<FindResult>? FindResultChanged;
    public event EventHandler<OutlineEvent>? OutlineChanged;
    /// <summary>Ctrl+click on a link; the href as written in the document.</summary>
    public event EventHandler<string>? LinkOpenRequested;
    public event EventHandler? RendererCrashed;

    /// <summary>Set by the document view model: stores pasted/dropped image bytes and returns the relative link (F-IMG-01).</summary>
    public Func<AssetSaveParams, Task<AssetSaveResult>>? AssetSaveHandler { get; set; }

    private readonly HashSet<char> _mappedDrives = [];

    /// <summary>Creates the CoreWebView2 (shared environment), applies security settings and navigates to the bundle.</summary>
    public async Task InitializeAsync()
    {
        if (_bridge is not null) return;

        var env = await WebViewEnvironment.GetAsync();
        await Web.EnsureCoreWebView2Async(env);
        var core = Web.CoreWebView2;

        var s = core.Settings;
        s.IsWebMessageEnabled = true;
        // Chromium's menu supplies cut/copy/paste (paste cannot be scripted); browser items are stripped and
        // "블록 소스 편집" (F-EDIT-12) is added in OnContextMenuRequested.
        s.AreDefaultContextMenusEnabled = true;
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
        core.ContextMenuRequested += OnContextMenuRequested;

        _bridge = new EditorBridge(core, DispatcherQueue, _log);
        _bridge.EventReceived += OnBridgeEvent;
        RegisterHostHandlers(_bridge);

        var devUrl = Environment.GetEnvironmentVariable("MARKPAD_EDITOR_URL");
        var target = string.IsNullOrWhiteSpace(devUrl) ? s_appUri : new Uri(devUrl);
        _log.LogInformation("editor: navigating to {Url}", target);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        core.Navigate(target.ToString());

        await _bridge.WhenReady;
        _log.LogInformation("editor: ready after {Ms} ms (process uptime {Uptime} ms)",
            sw.ElapsedMilliseconds, (long)(DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds);
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

    public async Task LoadAsync(string textLf, string? path, bool readOnly, EditorSettingsDto settings)
    {
        MapDocumentFolder(path is null ? null : Path.GetDirectoryName(path));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Bridge.CallAsync("doc.load", new DocLoadParams(textLf, path, readOnly, settings));
        _log.LogInformation("editor: doc.load {Chars} chars in {Ms} ms", textLf.Length, sw.ElapsedMilliseconds);
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
        // PRD F-VIEW-04: the shell decides (browser / new tab / shell execute) based on the document's folder.
        bridge.RegisterHandler("link.open", (p, _) =>
        {
            var href = p?.TryGetProperty("href", out var h) == true ? h.GetString() : null;
            if (string.IsNullOrWhiteSpace(href)) return Task.FromResult<object?>(new LinkOpenResult(false));
            LinkOpenRequested?.Invoke(this, href);
            return Task.FromResult<object?>(new LinkOpenResult(true));
        });

        // PRD F-VIEW-03 / ADR-05: absolute local paths are served through a per-drive virtual host, mapped lazily.
        bridge.RegisterHandler("image.resolve", (p, _) =>
        {
            var src = p?.TryGetProperty("src", out var s) == true ? s.GetString() ?? "" : "";
            return Task.FromResult<object?>(new ImageResolveResult(ResolveLocalImage(src) ?? src));
        });

        // HTML export with embedded images (F-EXP-01): read a local image relative to the document folder.
        bridge.RegisterHandler("asset.readBase64", async (p, ct) =>
        {
            var src = p?.TryGetProperty("src", out var s) == true ? s.GetString() ?? "" : "";
            var local = ResolveLocalPath(src);
            if (local is null || !File.Exists(local)) return new AssetReadResult(null);
            var bytes = await File.ReadAllBytesAsync(local, ct);
            var mime = Path.GetExtension(local).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".svg" => "image/svg+xml",
                ".bmp" => "image/bmp",
                ".avif" => "image/avif",
                _ => "application/octet-stream",
            };
            return new AssetReadResult($"data:{mime};base64,{Convert.ToBase64String(bytes)}");
        });

        bridge.RegisterHandler("asset.save", async (p, _) =>
        {
            if (p is null) throw new BridgeException("BAD_PARAM", "asset.save requires parameters");
            var handler = AssetSaveHandler ?? throw new BridgeException("NO_HANDLER", "no document attached");
            var request = p.Value.Deserialize(BridgeJsonContext.Default.AssetSaveParams)
                ?? throw new BridgeException("BAD_PARAM", "malformed asset.save");
            return await handler(request);
        });
    }

    /// <summary>Markdown image src → local file path (relative to the mapped document folder), or null for remote/data.</summary>
    private string? ResolveLocalPath(string src)
    {
        if (string.IsNullOrWhiteSpace(src)) return null;
        if (Uri.TryCreate(src, UriKind.Absolute, out var uri))
        {
            if (uri.IsFile) return uri.LocalPath;
            if (uri.Scheme is "http" or "https" or "data" or "blob") return null;
        }
        if (Path.IsPathRooted(src)) return src;
        if (_mappedDocDir is null) return null;
        var rel = Uri.UnescapeDataString(src.Split('#')[0].Split('?')[0]).Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(_mappedDocDir, rel));
    }

    /// <summary>`C:\x\a.png` or `file:///C:/x/a.png` → `https://drive-c.markpad/x/a.png`.</summary>
    private string? ResolveLocalImage(string src)
    {
        string local;
        if (Uri.TryCreate(src, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            local = uri.LocalPath;
        }
        else if (src.Length >= 3 && char.IsAsciiLetter(src[0]) && src[1] == ':' && (src[2] == '\\' || src[2] == '/'))
        {
            local = src;
        }
        else
        {
            return null;
        }

        var drive = char.ToLowerInvariant(local[0]);
        var core = Web.CoreWebView2;
        if (core is null) return null;
        if (_mappedDrives.Add(drive))
        {
            core.SetVirtualHostNameToFolderMapping($"drive-{drive}.markpad", $"{char.ToUpperInvariant(drive)}:\\", CoreWebView2HostResourceAccessKind.Allow);
        }
        var rest = local.Length > 3 ? local[3..].Replace('\\', '/') : string.Empty;
        var escaped = string.Join('/', rest.Split('/').Select(Uri.EscapeDataString));
        return $"https://drive-{drive}.markpad/{escaped}";
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
            case "find.result":
                if (e.PayloadAs<FindResult>() is { } fr) FindResultChanged?.Invoke(this, fr);
                break;
            case "outline":
                if (e.PayloadAs<OutlineEvent>() is { } ol) OutlineChanged?.Invoke(this, ol);
                break;
            case "files.dropped":
                if (e.Payload is { } p && p.TryGetProperty("files", out var files))
                {
                    foreach (var f in files.EnumerateArray())
                    {
                        var name = f.TryGetProperty("name", out var n) ? n.GetString() ?? "dropped.md" : "dropped.md";
                        var text = f.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                        TextFileDropped?.Invoke(this, new Editing.DroppedTextFile(name, text));
                    }
                }
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

    private static readonly HashSet<string> s_keepMenuItems = new(StringComparer.OrdinalIgnoreCase)
    {
        "cut", "copy", "paste", "pasteAndMatchStyle", "selectAll", "undo", "redo", "copyLinkText", "copyLinkLocation",
    };

    private CoreWebView2ContextMenuItem? _sourceMenuItem;

    /// <summary>Keep editing items, drop browser-only ones (back/reload/inspect/save as…), add block source editing.</summary>
    private void OnContextMenuRequested(CoreWebView2 sender, CoreWebView2ContextMenuRequestedEventArgs args)
    {
        var items = args.MenuItems;
        for (var i = items.Count - 1; i >= 0; i--)
        {
            var item = items[i];
            if (item.Kind == CoreWebView2ContextMenuItemKind.Separator) continue;
            if (!s_keepMenuItems.Contains(item.Name)) items.RemoveAt(i);
        }
        // collapse duplicate/leading separators
        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (items[i].Kind != CoreWebView2ContextMenuItemKind.Separator) continue;
            if (i == 0 || i == items.Count - 1 || items[i - 1].Kind == CoreWebView2ContextMenuItemKind.Separator) items.RemoveAt(i);
        }

        _sourceMenuItem ??= CreateSourceMenuItem(sender.Environment);
        if (items.Count > 0) items.Add(sender.Environment.CreateContextMenuItem(string.Empty, null, CoreWebView2ContextMenuItemKind.Separator));
        items.Add(_sourceMenuItem);
    }

    private CoreWebView2ContextMenuItem CreateSourceMenuItem(CoreWebView2Environment env)
    {
        var item = env.CreateContextMenuItem("블록 소스 편집", null, CoreWebView2ContextMenuItemKind.Command);
        item.CustomItemSelected += (_, _) =>
        {
            _ = Bridge.CallAsync("block.showSource", new BlockShowSourceParams(null));
        };
        return item;
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

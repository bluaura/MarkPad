using MarkPad.App.Bridge;
using MarkPad.App.Views;

namespace MarkPad.App.Editing;

/// <summary>WebView2 + Milkdown surface (wraps <see cref="EditorHost"/>).</summary>
public sealed class WebEditorSurface : IEditorSurface
{
    private readonly EditorHost _host;

    public WebEditorSurface(EditorHost host)
    {
        _host = host;
        _host.Changed += (_, e) => ContentChanged?.Invoke(this, e);
        _host.SelectionChanged += (_, e) => SelectionChanged?.Invoke(this, e);
        _host.ShortcutRequested += (_, e) => ShortcutRequested?.Invoke(this, e);
        _host.TextFileDropped += (_, e) => TextFileDropped?.Invoke(this, e);
    }

    public EditorHost Host => _host;

    public bool SupportsFormatting => true;

    public bool IsReady => _host.IsInitialized;

    public event EventHandler<ChangedEvent>? ContentChanged;
    public event EventHandler<SelectionContext>? SelectionChanged;
    public event EventHandler<ShortcutEvent>? ShortcutRequested;
    public event EventHandler<DroppedTextFile>? TextFileDropped;

    public Task InitializeAsync() => _host.InitializeAsync();

    public Task LoadAsync(string textLf, EditorLoadOptions options) => _host.LoadAsync(textLf, options.Path, options.ReadOnly, options.Settings);

    public Task<string> GetTextAsync() => _host.GetMarkdownAsync();

    public async Task<string> SerializeForSaveAsync(string originalLf) => (await _host.SerializeForSaveAsync(originalLf)).Text;

    public Task MarkSavedAsync() => _host.ExecuteAsync("doc.markSaved");

    public Task ExecuteAsync(string method, object? parameters = null) => _host.ExecuteAsync(method, parameters);

    public Task SetReadOnlyAsync(bool readOnly) => _host.ExecuteAsync("doc.setReadonly", new SetReadonlyParams(readOnly));

    public Task SetThemeAsync(EditorThemeDto theme) => _host.ExecuteAsync("view.setTheme", theme);

    public Task FocusAsync()
    {
        _host.FocusEditor();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();
}

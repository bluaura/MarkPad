using MarkPad.App.Bridge;

namespace MarkPad.App.Editing;

public sealed record EditorLoadOptions(string? Path, bool ReadOnly, EditorSettingsDto Settings);

/// <summary>
/// Tab surface abstraction (ARCHITECTURE.md §3.2): the shell, toolbar, status bar and save logic only see
/// this interface. <see cref="WebEditorSurface"/> wraps WebView2 + Milkdown, <see cref="PlainTextSurface"/> a TextBox.
/// </summary>
public interface IEditorSurface : IAsyncDisposable
{
    /// <summary>False for plain text: the format toolbar is disabled (PRD F-FILE-11).</summary>
    bool SupportsFormatting { get; }

    bool IsReady { get; }

    Task LoadAsync(string textLf, EditorLoadOptions options);

    /// <summary>Current content (markdown or plain), LF newlines.</summary>
    Task<string> GetTextAsync();

    /// <summary>Markdown: round-trip result against <paramref name="originalLf"/>; plain: current text.</summary>
    Task<string> SerializeForSaveAsync(string originalLf);

    /// <summary>Re-baseline dirty tracking after a successful save without reloading.</summary>
    Task MarkSavedAsync();

    Task ExecuteAsync(string method, object? parameters = null);

    Task SetReadOnlyAsync(bool readOnly);

    Task SetThemeAsync(EditorThemeDto theme);

    Task FocusAsync();

    event EventHandler<ChangedEvent>? ContentChanged;
    event EventHandler<SelectionContext>? SelectionChanged;
    event EventHandler<ShortcutEvent>? ShortcutRequested;
    event EventHandler<DroppedTextFile>? TextFileDropped;
}

/// <summary>A .md/.txt file dropped onto the editor page; WebView2 exposes content but no path.</summary>
public sealed record DroppedTextFile(string Name, string Text);

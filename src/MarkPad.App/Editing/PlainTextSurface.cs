using MarkPad.App.Bridge;
using MarkPad.App.Views;
using MarkPad.Core.Documents;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MarkPad.App.Editing;

/// <summary>Native TextBox surface for .txt (PRD F-FILE-11, ADR-08). No WebView2 is created.</summary>
public sealed class PlainTextSurface : IEditorSurface
{
    private readonly PlainTextHost _host;
    private string _baseline = string.Empty;
    private bool _loading;

    public PlainTextSurface(PlainTextHost host)
    {
        _host = host;
        _host.Box.TextChanged += OnTextChanged;
        _host.Box.SelectionChanged += OnSelectionChanged;
    }

    public bool SupportsFormatting => false;

    public bool IsReady => true;

    public event EventHandler<ChangedEvent>? ContentChanged;
    public event EventHandler<SelectionContext>? SelectionChanged;
#pragma warning disable CS0067 // shortcuts come from XAML accelerators; drops from the window (no WebView here)
    public event EventHandler<ShortcutEvent>? ShortcutRequested;
    public event EventHandler<DroppedTextFile>? TextFileDropped;
#pragma warning restore CS0067

    /// <summary>WinUI TextBox stores line breaks as '\r'; the rest of the app speaks LF.</summary>
    private static string ToLf(string boxText) => boxText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    public Task LoadAsync(string textLf, EditorLoadOptions options)
    {
        _loading = true;
        _baseline = textLf;
        _host.Box.Text = textLf.Replace("\n", "\r", StringComparison.Ordinal);
        _host.Box.IsReadOnly = options.ReadOnly;
        _host.Box.SelectionStart = 0;
        _loading = false;
        Emit();
        return Task.CompletedTask;
    }

    public Task<string> GetTextAsync() => Task.FromResult(ToLf(_host.Box.Text));

    public Task<string> SerializeForSaveAsync(string originalLf) => GetTextAsync();

    public Task MarkSavedAsync()
    {
        _baseline = ToLf(_host.Box.Text);
        Emit();
        return Task.CompletedTask;
    }

    public Task ExecuteAsync(string method, object? parameters = null)
    {
        switch (method)
        {
            case "history.undo": _host.Box.Undo(); break;
            case "history.redo": _host.Box.Redo(); break;
            case "edit.selectAll": _host.Box.SelectAll(); break;
            case "edit.cut": _host.Box.CutSelectionToClipboard(); break;
            case "edit.copy": _host.Box.CopySelectionToClipboard(); break;
            case "edit.paste": _host.Box.PasteFromClipboard(); break;
            case "view.focus": _host.Box.Focus(FocusState.Programmatic); break;
            case "insert.datetime":
                _host.Box.SelectedText = DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                break;
            default:
                break; // formatting commands are not applicable
        }
        return Task.CompletedTask;
    }

    public Task SetReadOnlyAsync(bool readOnly)
    {
        _host.Box.IsReadOnly = readOnly;
        return Task.CompletedTask;
    }

    public Task SetThemeAsync(EditorThemeDto theme)
    {
        _host.ApplyTheme(theme);
        return Task.CompletedTask;
    }

    public Task FocusAsync()
    {
        _host.Box.Focus(FocusState.Programmatic);
        return Task.CompletedTask;
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading) Emit();
    }

    private void OnSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading) Emit();
    }

    private void Emit()
    {
        var text = ToLf(_host.Box.Text);
        var caret = Math.Min(_host.Box.SelectionStart, _host.Box.Text.Length);
        var line = 1;
        for (var i = 0; i < caret && i < _host.Box.Text.Length; i++)
        {
            if (_host.Box.Text[i] == '\r') line++;
        }
        var words = 0;
        var inWord = false;
        var chars = 0;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else
            {
                chars++;
                if (!inWord) { words++; inWord = true; }
            }
        }
        ContentChanged?.Invoke(this, new ChangedEvent(!string.Equals(text, _baseline, StringComparison.Ordinal), words, chars, line));
        SelectionChanged?.Invoke(this, SelectionContext.Empty);
    }

    public ValueTask DisposeAsync()
    {
        _host.Box.TextChanged -= OnTextChanged;
        _host.Box.SelectionChanged -= OnSelectionChanged;
        return ValueTask.CompletedTask;
    }
}

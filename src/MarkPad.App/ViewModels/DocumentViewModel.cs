using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkPad.App.Bridge;
using MarkPad.App.Editing;
using MarkPad.App.Services;
using MarkPad.Core.Assets;
using MarkPad.Core.Documents;
using MarkPad.Core.Settings;

namespace MarkPad.App.ViewModels;

/// <summary>
/// One tab: a <see cref="Core.Documents.Document"/> plus its <see cref="IEditorSurface"/> (ARCHITECTURE.md §3.2).
/// The surface is attached by the view once its control exists; until then <see cref="IsLoaded"/> is false.
/// </summary>
public sealed partial class DocumentViewModel : ObservableObject, IAsyncDisposable
{
    private readonly DocumentIO _io;
    private readonly ThemeService _theme;
    private readonly SettingsStore _settings;
    private readonly AssetService _assets;
    private readonly string? _suggestedName;

    public DocumentViewModel(Document document, DocumentIO io, ThemeService theme, SettingsStore settings, AssetService assets, string? suggestedName = null)
    {
        Document = document;
        _io = io;
        _theme = theme;
        _settings = settings;
        _assets = assets;
        _suggestedName = suggestedName;
        IsDirty = document.IsDirty;
        IsReadOnly = document.IsReadOnly;
        RefreshLabels();
    }

    public Document Document { get; private set; }

    public IEditorSurface? Surface { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    public partial string Title { get; set; } = "Untitled";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    [NotifyPropertyChangedFor(nameof(SaveStateLabel))]
    public partial bool IsDirty { get; set; }

    [ObservableProperty] public partial int WordCount { get; set; }
    [ObservableProperty] public partial int CharCount { get; set; }
    [ObservableProperty] public partial int Line { get; set; } = 1;
    [ObservableProperty] public partial bool IsLoaded { get; set; }
    [ObservableProperty] public partial string EncodingLabel { get; set; } = "UTF-8";
    [ObservableProperty] public partial string EolLabel { get; set; } = "LF";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveStateLabel))]
    public partial bool IsReadOnly { get; set; }

    public string DisplayTitle => (IsDirty ? "● " : string.Empty) + Title;

    public string SaveStateLabel => IsReadOnly ? "읽기 전용" : IsDirty ? "● 수정됨" : "저장됨 ✓";

    public bool IsPlainText => Document.Kind == DocumentKind.PlainText;

    public string? Path => Document.Path;

    public string EncodingDescription => Document.Encoding.CodePage == 949 ? "EUC-KR (CP949)" : Document.Encoding.WebName.ToUpperInvariant();

    public bool SupportsFormatting => Surface?.SupportsFormatting ?? !IsPlainText;

    public event EventHandler? SurfaceAttached;
    public event EventHandler<ShortcutEvent>? ShortcutRequested;
    public event EventHandler<DroppedTextFile>? TextFileDropped;
    /// <summary>Ctrl+click on a link; the shell resolves it against <see cref="Document"/>'s folder (PRD F-VIEW-04).</summary>
    public event EventHandler<string>? LinkOpenRequested;

    public async Task AttachSurfaceAsync(IEditorSurface surface)
    {
        if (Surface is not null) throw new InvalidOperationException("surface already attached");
        Surface = surface;
        surface.ContentChanged += OnContentChanged;
        surface.ShortcutRequested += (_, e) => ShortcutRequested?.Invoke(this, e);
        surface.TextFileDropped += (_, e) => TextFileDropped?.Invoke(this, e);
        surface.LinkOpenRequested += (_, href) => LinkOpenRequested?.Invoke(this, href);
        if (surface is WebEditorSurface web)
        {
            web.Host.AssetSaveHandler = SaveAssetAsync;
        }
        await LoadIntoSurfaceAsync();
        SurfaceAttached?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoadIntoSurfaceAsync()
    {
        if (Surface is null) return;
        var options = new EditorLoadOptions(Document.Path, Document.IsReadOnly, _theme.BuildEditorSettings());
        await Surface.LoadAsync(Document.OriginalText, options);
        await Surface.SetDocumentPathAsync(Document.Path, _assets.DisplayRootFor(Document));
        IsLoaded = true;
        RefreshLabels();
        if (Document.IsDirty)
        {
            // Documents created from dropped text start dirty even though the surface baseline equals the text.
            IsDirty = true;
        }
    }

    /// <summary>Replace the underlying document (external change reload, T-52) and reload the surface.</summary>
    public async Task ReplaceDocumentAsync(Document document)
    {
        Document = document;
        IsReadOnly = document.IsReadOnly;
        IsDirty = false;
        await LoadIntoSurfaceAsync();
    }

    /// <summary>
    /// Serialize through the surface and write atomically. On the first save, pending images move next to the
    /// document first (PRD F-IMG-04) so the saved markdown already carries final links. Caller handles pickers/errors.
    /// </summary>
    public async Task SaveAsync(string? newPath = null)
    {
        if (Surface is null) throw new InvalidOperationException("no surface");
        if (Document.IsReadOnly) throw new InvalidOperationException("document is read-only");

        var target = newPath ?? Document.Path ?? throw new InvalidOperationException("no path");
        if (Document.Path is null || !string.Equals(System.IO.Path.GetDirectoryName(target), Document.Directory, StringComparison.OrdinalIgnoreCase))
        {
            var renamed = await _assets.RelocatePendingAsync(Document, target);
            if (renamed.Count > 0)
            {
                await Surface.ExecuteAsync("doc.rewriteAssetPaths", new RewriteAssetPathsParams(new Dictionary<string, string>(renamed)));
            }
        }

        var text = await Surface.SerializeForSaveAsync(Document.OriginalText);
        await _io.SaveAsync(Document, text, newPath, _settings.ToSaveOptions());
        await Surface.MarkSavedAsync();
        await Surface.SetDocumentPathAsync(Document.Path, _assets.DisplayRootFor(Document));
        IsDirty = false;
        RefreshLabels();
        OnPropertyChanged(nameof(Path));
    }

    [RelayCommand]
    private async Task ConvertToUtf8()
    {
        Document.ConvertToUtf8();
        IsReadOnly = false;
        IsDirty = true;
        RefreshLabels();
        if (Surface is not null) await Surface.SetReadOnlyAsync(false);
    }

    /// <summary>Toolbar image button / Ctrl+Shift+I (PRD F-IMG-02): copy into assets or reference the original path.</summary>
    public async Task InsertImageFromFileAsync(string filePath, bool copy)
    {
        if (Surface is null) return;
        var src = copy ? await _assets.CopyImageAsync(Document, filePath) : filePath;
        await Surface.ExecuteAsync("insert.image", new InsertImageParams(src, System.IO.Path.GetFileNameWithoutExtension(filePath)));
        await Surface.FocusAsync();
    }

    private async Task<AssetSaveResult> SaveAssetAsync(AssetSaveParams p)
    {
        var bytes = Convert.FromBase64String(p.BytesBase64);
        var rel = await _assets.SaveImageAsync(Document, bytes, p.Mime, p.SuggestedName);
        IsDirty = true;
        return new AssetSaveResult(rel);
    }

    public Task ApplyThemeAsync() => Surface?.SetThemeAsync(_theme.BuildEditorTheme()) ?? Task.CompletedTask;

    public Task FocusAsync() => Surface?.FocusAsync() ?? Task.CompletedTask;

    /// <summary>Default name for Save As: file name, else the first H1, else the dropped file name, else Untitled (T-17).</summary>
    public async Task<string> SuggestedFileNameAsync()
    {
        if (Document.Path is not null) return System.IO.Path.GetFileNameWithoutExtension(Document.Path);
        if (!IsPlainText && Surface is not null)
        {
            var text = await Surface.GetTextAsync();
            var m = FirstHeadingRegex().Match(text);
            if (m.Success)
            {
                var name = m.Groups[1].Value.Trim();
                foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, ' ');
                name = Regex.Replace(name, @"\s+", " ").Trim();
                if (name.Length > 60) name = name[..60].Trim();
                if (name.Length > 0) return name;
            }
        }
        if (_suggestedName is not null) return System.IO.Path.GetFileNameWithoutExtension(_suggestedName);
        return "Untitled";
    }

    private void OnContentChanged(object? sender, ChangedEvent e)
    {
        IsDirty = e.Dirty || (Document.Path is null && Document.IsDirty && !IsLoaded);
        Document.IsDirty = IsDirty;
        WordCount = e.Words;
        CharCount = e.Chars;
        Line = e.Line;
    }

    private void RefreshLabels()
    {
        Title = Document.Path is null
            ? (_suggestedName is not null ? $"{_suggestedName} (복사본)" : Document.Kind == DocumentKind.PlainText ? "Untitled.txt" : "Untitled")
            : Document.FileName;
        EncodingLabel = Document.Encoding.CodePage == 65001 ? (Document.HasBom ? "UTF-8 BOM" : "UTF-8") : EncodingDescription;
        EolLabel = Document.Eol == LineEnding.CrLf ? "CRLF" : "LF";
        OnPropertyChanged(nameof(EncodingDescription));
    }

    public async ValueTask DisposeAsync()
    {
        if (Document.Path is null) _assets.DiscardPending(Document);
        if (Surface is not null)
        {
            Surface.ContentChanged -= OnContentChanged;
            if (Surface is WebEditorSurface web) web.Host.AssetSaveHandler = null;
            await Surface.DisposeAsync();
            Surface = null;
        }
    }

    [GeneratedRegex(@"^\s*#\s+(.+?)\s*#*\s*$", RegexOptions.Multiline)]
    private static partial Regex FirstHeadingRegex();
}

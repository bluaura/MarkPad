using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkPad.App.Bridge;
using MarkPad.App.Editing;
using MarkPad.App.Services;
using MarkPad.Core.Assets;
using MarkPad.Core.Documents;
using MarkPad.Core.Recovery;
using MarkPad.Core.Settings;
using Microsoft.UI.Dispatching;

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
    private readonly RecoveryStore _recovery;
    private readonly string? _suggestedName;
    private string? _initialText;
    private string? _lastSnapshotText;
    private DispatcherQueueTimer? _snapshotTimer;
    private bool _snapshotBusy;

    private FileWatcher? _watcher;

    /// <summary>PRD F-FILE-08: the file changed on disk while this tab has unsaved edits.</summary>
    [ObservableProperty]
    public partial bool HasExternalChange { get; set; }

    /// <summary>PRD F-VIEW-09: user-toggled read-only (independent of the encoding read-only state).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveStateLabel))]
    public partial bool IsUserReadOnly { get; set; }

    partial void OnIsUserReadOnlyChanged(bool value)
    {
        _ = Surface?.SetReadOnlyAsync(value || IsReadOnly);
    }

    /// <summary>Snapshot cadence (PRD §5.5: 5초).</summary>
    public static TimeSpan SnapshotInterval { get; } = TimeSpan.FromSeconds(5);

    public DocumentViewModel(
        Document document,
        DocumentIO io,
        ThemeService theme,
        SettingsStore settings,
        AssetService assets,
        RecoveryStore recovery,
        string? suggestedName = null,
        string? initialText = null)
    {
        Document = document;
        _io = io;
        _theme = theme;
        _settings = settings;
        _assets = assets;
        _recovery = recovery;
        _suggestedName = suggestedName;
        _initialText = initialText;
        if (initialText is not null) document.IsDirty = true;
        IsDirty = document.IsDirty;
        IsReadOnly = document.IsReadOnly;
        RefreshLabels();
    }

    /// <summary>Recovery snapshot id: unique per process + document.</summary>
    public string SnapshotId => $"{Environment.ProcessId}-{Document.Id}";

    /// <summary>Messages for the shell InfoBar (e.g. renderer recovery).</summary>
    public event EventHandler<string>? Notification;

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

    public string SaveStateLabel => IsReadOnly ? "읽기 전용" : IsUserReadOnly ? "읽기 전용 (토글)" : IsDirty ? "● 수정됨" : "저장됨 ✓";

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
        RestartWatcher();
        SurfaceAttached?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoadIntoSurfaceAsync()
    {
        if (Surface is null) return;
        var options = new EditorLoadOptions(Document.Path, Document.IsReadOnly, _theme.BuildEditorSettings());
        var text = _initialText ?? Document.OriginalText;
        await Surface.LoadAsync(text, options);
        await Surface.SetDocumentPathAsync(Document.Path, _assets.DisplayRootFor(Document));
        IsLoaded = true;
        RefreshLabels();
        if (_initialText is not null || Document.IsDirty)
        {
            // Recovered/dropped content: the surface baseline equals the text, but it is unsaved relative to the file.
            _lastSnapshotText = text;
            _initialText = null;
            Document.IsDirty = true;
            IsDirty = true;
            EnsureSnapshotTimer();
        }
    }

    // ---------- external changes (PRD F-FILE-08, T-52) ----------

    private void RestartWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
        if (Document.Path is null) return;
        var queue = DispatcherQueue.GetForCurrentThread();
        try
        {
            _watcher = new FileWatcher(Document, change =>
            {
                if (queue is null) OnExternalChange(change);
                else queue.TryEnqueue(() => OnExternalChange(change));
            });
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _watcher = null;
        }
    }

    private void OnExternalChange(ExternalChange change)
    {
        if (change.Kind != ExternalChangeKind.Changed)
        {
            Notification?.Invoke(this, $"'{Title}' 파일이 외부에서 {(change.Kind == ExternalChangeKind.Deleted ? "삭제" : "이름 변경")}되었습니다. 저장하면 다시 만들어집니다.");
            IsDirty = true;
            Document.IsDirty = true;
            return;
        }
        if (!IsDirty)
        {
            _ = ReloadFromDiskAsync(); // ARCHITECTURE §6.4: quiet reload when there is nothing to lose
        }
        else
        {
            HasExternalChange = true;
        }
    }

    [RelayCommand]
    public async Task ReloadFromDiskAsync()
    {
        if (Document.Path is null || !File.Exists(Document.Path)) return;
        try
        {
            var fresh = await _io.OpenAsync(Document.Path);
            HasExternalChange = false;
            await ReplaceDocumentAsync(fresh);
            Notification?.Invoke(this, $"'{Title}'을(를) 디스크에서 다시 읽었습니다.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notification?.Invoke(this, $"다시 읽기 실패: {ex.Message}");
        }
    }

    [RelayCommand]
    private void IgnoreExternalChange() => HasExternalChange = false;

    // ---------- crash recovery (PRD §5.5, T-42) ----------

    private RecoverySnapshot SnapshotMeta => new(SnapshotId, Document.Path, Title, Document.Kind, DateTimeOffset.Now);

    private void EnsureSnapshotTimer()
    {
        if (_snapshotTimer is not null) return;
        var queue = DispatcherQueue.GetForCurrentThread();
        if (queue is null) return;
        _snapshotTimer = queue.CreateTimer();
        _snapshotTimer.Interval = SnapshotInterval;
        _snapshotTimer.IsRepeating = true;
        _snapshotTimer.Tick += (_, _) => _ = SnapshotAsync();
        _snapshotTimer.Start();
    }

    private void StopSnapshots(bool deleteFiles)
    {
        _snapshotTimer?.Stop();
        _snapshotTimer = null;
        if (deleteFiles) _recovery.Delete(SnapshotId);
    }

    private async Task SnapshotAsync()
    {
        if (_snapshotBusy || !IsDirty || Surface is null || !Surface.IsReady) return;
        _snapshotBusy = true;
        try
        {
            var text = await Surface.GetTextAsync();
            if (string.Equals(text, _lastSnapshotText, StringComparison.Ordinal)) return;
            _lastSnapshotText = text;
            await _recovery.SaveAsync(SnapshotMeta, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BridgeException)
        {
            // Snapshots are best effort; the next tick retries.
        }
        finally
        {
            _snapshotBusy = false;
        }
    }

    /// <summary>Unhandled-exception path: write the last known text synchronously (no bridge round trip possible).</summary>
    public void FlushSnapshotBlocking()
    {
        if (IsDirty && _lastSnapshotText is not null) _recovery.SaveBlocking(SnapshotMeta, _lastSnapshotText);
    }

    /// <summary>WebView2 renderer died: swap in a fresh surface and reload the last snapshot (or the file).</summary>
    public async Task RecoverSurfaceAsync(IEditorSurface replacement)
    {
        var old = Surface;
        Surface = null;
        IsLoaded = false;
        if (old is not null)
        {
            old.ContentChanged -= OnContentChanged;
            try { await old.DisposeAsync(); } catch (Exception ex) when (ex is BridgeException or InvalidOperationException) { }
        }
        if (IsDirty && _lastSnapshotText is not null) _initialText = _lastSnapshotText;
        await AttachSurfaceAsync(replacement);
        Notification?.Invoke(this, IsDirty
            ? "편집기 프로세스가 중단되어 마지막 스냅샷(최대 5초 전)으로 복구했습니다."
            : "편집기 프로세스가 중단되어 파일을 다시 불러왔습니다.");
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
        HasExternalChange = false;
        StopSnapshots(deleteFiles: true);
        RefreshLabels();
        OnPropertyChanged(nameof(Path));
        if (_watcher is null || newPath is not null) RestartWatcher();
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
        if (IsDirty) EnsureSnapshotTimer();
        else if (IsLoaded) StopSnapshots(deleteFiles: true);
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
        _watcher?.Dispose();
        _watcher = null;
        StopSnapshots(deleteFiles: true);
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

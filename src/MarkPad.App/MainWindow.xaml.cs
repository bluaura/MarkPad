using MarkPad.App.Bridge;
using MarkPad.Core.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;

namespace MarkPad.App;

/// <summary>
/// M0 shell: one document, toolbar, editor, status bar. Tabs/ShellViewModel arrive with T-13/T-14;
/// this window keeps the open/save/shortcut flows that those tasks will move into DocumentViewModel.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly DocumentIO _io = App.Current.Services.GetRequiredService<DocumentIO>();
    private readonly ILogger<MainWindow> _log = App.Current.Services.GetRequiredService<ILogger<MainWindow>>();
    private Document _doc = DocumentIO.CreateNew();
    private bool _dirty;
    private bool _editorReady;
    private string? _pendingOpenPath;
    private bool _closeConfirmed;
    private ChangedEvent _lastChanged = new(false, 0, 0, 1);

    public MainWindow()
    {
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        AppWindow.Closing += OnAppWindowClosing;
        UpdateTitle();
    }

    private EditorSettingsDto CurrentSettings => new(
        new EditorThemeDto(
            Mode: Root.ActualTheme == ElementTheme.Dark ? "dark" : "light",
            FontFamily: "'Segoe UI Variable Text', 'Segoe UI', 'Malgun Gothic', sans-serif",
            FontSize: 15,
            LineHeight: 1.7,
            MaxWidth: 800,
            Zoom: 1.0),
        new MarkdownStyleDto(),
        AllowRemoteImages: true);

    // ---------- lifecycle ----------

    private async void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await Editor.InitializeAsync();
            _editorReady = true;
            Editor.Changed += OnEditorChanged;
            Editor.ShortcutRequested += OnEditorShortcut;
            Editor.RendererCrashed += OnRendererCrashed;
            Toolbar.ViewModel.Attach(Editor);

            if (_pendingOpenPath is { } path)
            {
                _pendingOpenPath = null;
                await OpenFileAsync(path);
            }
            else
            {
                await LoadCurrentDocumentAsync();
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "editor initialization failed");
            StatusText.Text = $"편집기 초기화 실패: {ex.Message}";
        }
    }

    public async Task OpenFileAsync(string path)
    {
        if (!_editorReady)
        {
            _pendingOpenPath = path;
            return;
        }
        if (!await ConfirmDiscardAsync()) return;
        try
        {
            _doc = await _io.OpenAsync(path);
            await LoadCurrentDocumentAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogError(ex, "open failed: {Path}", path);
            StatusText.Text = $"열기 실패: {ex.Message}";
        }
    }

    private async Task LoadCurrentDocumentAsync()
    {
        await Editor.LoadAsync(_doc.OriginalText, _doc.Path, _doc.IsReadOnly, CurrentSettings);
        _dirty = false;
        UpdateTitle();
        UpdateStatus();
        Editor.FocusEditor();
    }

    private async Task NewDocumentAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        _doc = DocumentIO.CreateNew();
        await LoadCurrentDocumentAsync();
    }

    private async Task OpenWithPickerAsync()
    {
        var picker = new FileOpenPicker(AppWindow.Id)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            CommitButtonText = "열기",
        };
        picker.FileTypeFilter.Add(".md");
        picker.FileTypeFilter.Add(".markdown");
        picker.FileTypeFilter.Add(".txt");
        var result = await picker.PickSingleFileAsync();
        if (result?.Path is { Length: > 0 } path)
        {
            await OpenFileAsync(path);
        }
    }

    private async Task<bool> SaveAsync(bool saveAs)
    {
        if (!_editorReady) return false;
        var path = _doc.Path;
        if (saveAs || path is null)
        {
            var picker = new FileSavePicker(AppWindow.Id)
            {
                SuggestedFileName = Path.GetFileNameWithoutExtension(_doc.FileName) is { Length: > 0 } n && _doc.Path is not null ? n : "Untitled",
                DefaultFileExtension = _doc.Kind == DocumentKind.PlainText ? ".txt" : ".md",
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                CommitButtonText = "저장",
            };
            picker.FileTypeChoices.Add("Markdown", [".md", ".markdown"]);
            picker.FileTypeChoices.Add("텍스트", [".txt"]);
            var result = await picker.PickSaveFileAsync();
            if (result?.Path is not { Length: > 0 } chosen) return false;
            path = chosen;
        }

        try
        {
            if (_doc.IsReadOnly)
            {
                _doc.ConvertToUtf8(); // T-19 adds the explicit InfoBar flow; M0 converts on save.
            }
            var serialized = await Editor.SerializeForSaveAsync(_doc.OriginalText);
            await _io.SaveAsync(_doc, serialized.Text, path, SaveOptions.Default);
            Editor.MapDocumentFolder(_doc.Directory);
            // Re-baseline the editor's dirty tracking without reloading: the next `changed` event reports dirty=false
            // only when the document equals the loaded doc, so reload after Save As / first save (cheap for M0).
            await Editor.LoadAsync(_doc.OriginalText, _doc.Path, false, CurrentSettings);
            _dirty = false;
            UpdateTitle();
            UpdateStatus();
            _log.LogInformation("saved {Path} ({Changed} changed blocks)", _doc.Path, serialized.ChangedBlocks.Length);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BridgeException)
        {
            _log.LogError(ex, "save failed: {Path}", path);
            StatusText.Text = $"저장 실패: {ex.Message}";
            return false;
        }
    }

    /// <summary>PRD F-FILE-05: confirm before discarding unsaved changes. Returns false to cancel.</summary>
    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!_dirty) return true;
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "저장되지 않은 변경 사항",
            Content = $"'{_doc.FileName}'의 변경 사항을 저장할까요?",
            PrimaryButtonText = "저장",
            SecondaryButtonText = "저장 안 함",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Primary,
        };
        var r = await dialog.ShowAsync();
        return r switch
        {
            ContentDialogResult.Primary => await SaveAsync(saveAs: false),
            ContentDialogResult.Secondary => true,
            _ => false,
        };
    }

    private async void OnAppWindowClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed || !_dirty) return;
        args.Cancel = true;
        if (await ConfirmDiscardAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    // ---------- editor events ----------

    private void OnEditorChanged(object? sender, ChangedEvent e)
    {
        _lastChanged = e;
        if (_dirty != e.Dirty)
        {
            _dirty = e.Dirty;
            _doc.IsDirty = e.Dirty;
            UpdateTitle();
        }
        UpdateStatus();
    }

    private async void OnEditorShortcut(object? sender, ShortcutEvent e)
    {
        try
        {
            switch (e.Key)
            {
                case "ctrl+s": await SaveAsync(saveAs: false); break;
                case "ctrl+shift+s": await SaveAsync(saveAs: true); break;
                case "ctrl+o": await OpenWithPickerAsync(); break;
                case "ctrl+n": await NewDocumentAsync(); break;
                default:
                    _log.LogDebug("shortcut not handled yet: {Key}", e.Key);
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "shortcut {Key} failed", e.Key);
        }
    }

    private void OnRendererCrashed(object? sender, EventArgs e)
    {
        StatusText.Text = "편집기 프로세스가 중단되었습니다. (복구는 T-42)";
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        if (!_editorReady) return;
        _ = Editor.ExecuteAsync("view.setTheme", new { mode = Root.ActualTheme == ElementTheme.Dark ? "dark" : "light" });
    }

    // ---------- XAML handlers ----------

    private async void OnNewClick(object sender, RoutedEventArgs e) => await NewDocumentAsync();
    private async void OnOpenClick(object sender, RoutedEventArgs e) => await OpenWithPickerAsync();
    private async void OnSaveClick(object sender, RoutedEventArgs e) => await SaveAsync(saveAs: false);

    private async void OnAccelNew(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; await NewDocumentAsync(); }
    private async void OnAccelOpen(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; await OpenWithPickerAsync(); }
    private async void OnAccelSave(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; await SaveAsync(saveAs: false); }
    private async void OnAccelSaveAs(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; await SaveAsync(saveAs: true); }

    // ---------- presentation ----------

    private void UpdateTitle()
    {
        Title = $"{(_dirty ? "● " : string.Empty)}{_doc.FileName} - MarkPad";
    }

    private void UpdateStatus()
    {
        var enc = _doc.Encoding.CodePage == 65001 ? (_doc.HasBom ? "UTF-8 BOM" : "UTF-8") : _doc.Encoding.WebName.ToUpperInvariant();
        var eol = _doc.Eol == LineEnding.CrLf ? "CRLF" : "LF";
        StatusText.Text = $"단어 {_lastChanged.Words:N0} · 글자 {_lastChanged.Chars:N0} · {enc} · {eol} · 줄 {_lastChanged.Line}";
        SaveStateText.Text = _doc.IsReadOnly ? "읽기 전용 (저장 시 UTF-8 변환)" : _dirty ? "● 수정됨" : "저장됨 ✓";
    }
}

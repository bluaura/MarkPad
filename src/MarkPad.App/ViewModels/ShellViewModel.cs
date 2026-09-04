using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkPad.App.Bridge;
using MarkPad.App.Editing;
using MarkPad.App.Services;
using MarkPad.Core.Documents;
using MarkPad.Core.Mru;
using MarkPad.Core.Settings;
using Microsoft.Extensions.Logging;

namespace MarkPad.App.ViewModels;

/// <summary>Tabs, file commands and app shortcuts (ARCHITECTURE.md §3.2 ShellViewModel).</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly DocumentIO _io;
    private readonly SettingsStore _settings;
    private readonly RecentFilesStore _recent;
    private readonly JumpListService _jumpList;
    private readonly ThemeService _theme;
    private readonly IDialogService _dialogs;
    private readonly ILogger<ShellViewModel> _log;

    public ShellViewModel(
        DocumentIO io,
        SettingsStore settings,
        RecentFilesStore recent,
        JumpListService jumpList,
        ThemeService theme,
        IDialogService dialogs,
        ILogger<ShellViewModel> log)
    {
        _io = io;
        _settings = settings;
        _recent = recent;
        _jumpList = jumpList;
        _theme = theme;
        _dialogs = dialogs;
        _log = log;
        ShowToolbar = settings.Current.Ui.ShowToolbar;
        Tabs.CollectionChanged += OnTabsChanged;
        _recent.Changed += (_, _) => OnPropertyChanged(nameof(RecentFiles));
        _theme.ActualThemeChanged += async (_, _) =>
        {
            foreach (var t in Tabs) await t.ApplyThemeAsync();
        };
    }

    public ObservableCollection<DocumentViewModel> Tabs { get; } = [];

    public ToolbarViewModel Toolbar { get; } = new();

    public IReadOnlyList<RecentFile> RecentFiles => _recent.Items;

    public bool HasTabs => Tabs.Count > 0;

    [ObservableProperty]
    public partial DocumentViewModel? SelectedTab { get; set; }

    [ObservableProperty]
    public partial bool ShowToolbar { get; set; }

    partial void OnSelectedTabChanged(DocumentViewModel? oldValue, DocumentViewModel? newValue)
    {
        if (oldValue is not null) oldValue.SurfaceAttached -= OnSelectedSurfaceAttached;
        if (newValue is not null) newValue.SurfaceAttached += OnSelectedSurfaceAttached;
        AttachToolbar(newValue);
    }

    partial void OnShowToolbarChanged(bool value)
    {
        _ = _settings.UpdateAsync(s => s.Ui.ShowToolbar = value);
    }

    private void OnSelectedSurfaceAttached(object? sender, EventArgs e) => AttachToolbar(sender as DocumentViewModel);

    private void AttachToolbar(DocumentViewModel? tab)
    {
        var surface = tab?.Surface;
        Toolbar.Attach(surface is { SupportsFormatting: true } ? surface : null);
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnPropertyChanged(nameof(HasTabs));

    // ---------- tab creation ----------

    private DocumentViewModel CreateTab(Document document, string? suggestedName = null)
    {
        var vm = new DocumentViewModel(document, _io, _theme, _settings, suggestedName);
        vm.ShortcutRequested += (_, e) => _ = HandleShortcutAsync(e.Key);
        vm.TextFileDropped += (_, e) => OpenDroppedText(e);
        Tabs.Add(vm);
        SelectedTab = vm;
        return vm;
    }

    [RelayCommand]
    private void NewDocument()
    {
        var eol = _settings.Current.Save.Eol == "crlf" ? LineEnding.CrLf : LineEnding.Lf;
        CreateTab(DocumentIO.CreateNew(DocumentKind.Markdown, eol));
    }

    private void OpenDroppedText(DroppedTextFile file)
    {
        var kind = Document.KindFromPath(file.Name);
        var doc = DocumentIO.CreateNew(kind, initialText: file.Text);
        CreateTab(doc, file.Name);
        _dialogs.ShowInfo($"'{file.Name}'의 내용을 새 탭으로 열었습니다. 편집기 위에 끌어다 놓으면 경로를 알 수 없어 복사본으로 열립니다. 탭 줄이나 툴바에 놓으면 원본 파일을 엽니다.");
    }

    [RelayCommand]
    public async Task OpenFilesAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var full = Path.GetFullPath(path);
            var existing = Tabs.FirstOrDefault(t => string.Equals(t.Path, full, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                SelectedTab = existing;
                continue;
            }
            try
            {
                var doc = await _io.OpenAsync(full);
                CreateTab(doc);
                await _recent.AddAsync(full);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.LogError(ex, "open failed: {Path}", full);
                _dialogs.ShowInfo($"열기 실패: {full} — {ex.Message}", isError: true);
                if (!File.Exists(full)) await _recent.RemoveAsync(full);
            }
        }
        await _jumpList.UpdateAsync(_recent.Items);
    }

    [RelayCommand]
    private async Task OpenWithPickerAsync()
    {
        var files = await _dialogs.PickOpenFilesAsync();
        if (files.Count > 0) await OpenFilesAsync(files);
    }

    [RelayCommand]
    private async Task OpenRecentAsync(RecentFile? file)
    {
        if (file is null) return;
        if (!File.Exists(file.Path))
        {
            _dialogs.ShowInfo($"파일을 찾을 수 없어 최근 목록에서 제거했습니다: {file.Path}");
            await _recent.RemoveAsync(file.Path);
            return;
        }
        await OpenFilesAsync([file.Path]);
    }

    [RelayCommand]
    private Task RemoveRecentAsync(RecentFile? file) => file is null ? Task.CompletedTask : _recent.RemoveAsync(file.Path);

    // ---------- save ----------

    [RelayCommand]
    private Task SaveAsync() => SaveTabAsync(SelectedTab, saveAs: false);

    [RelayCommand]
    private Task SaveAsAsync() => SaveTabAsync(SelectedTab, saveAs: true);

    public async Task<bool> SaveTabAsync(DocumentViewModel? tab, bool saveAs)
    {
        if (tab is null || tab.Surface is null) return false;
        if (tab.Document.IsReadOnly)
        {
            _dialogs.ShowInfo("읽기 전용 문서입니다. 상단의 'UTF-8로 변환하여 편집'을 누른 뒤 저장하세요.");
            return false;
        }

        string? path = tab.Path;
        if (saveAs || path is null)
        {
            var suggested = await tab.SuggestedFileNameAsync();
            path = await _dialogs.PickSavePathAsync(suggested, tab.Document.Kind, tab.Document.Directory);
            if (path is null) return false;
        }

        try
        {
            await tab.SaveAsync(path);
            await _recent.AddAsync(path);
            await _jumpList.UpdateAsync(_recent.Items);
            _log.LogInformation("saved {Path}", path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BridgeException or InvalidOperationException)
        {
            _log.LogError(ex, "save failed: {Path}", path);
            _dialogs.ShowInfo($"저장 실패: {ex.Message}", isError: true);
            return false;
        }
    }

    // ---------- close ----------

    [RelayCommand]
    private Task CloseTabAsync(DocumentViewModel? tab) => TryCloseTabAsync(tab ?? SelectedTab);

    [RelayCommand]
    private async Task CloseOtherTabsAsync(DocumentViewModel? keep)
    {
        keep ??= SelectedTab;
        foreach (var t in Tabs.Where(t => t != keep).ToList())
        {
            if (!await TryCloseTabAsync(t)) return;
        }
    }

    /// <summary>PRD F-FILE-05: confirm before discarding. Returns false when the user cancelled.</summary>
    public async Task<bool> TryCloseTabAsync(DocumentViewModel? tab)
    {
        if (tab is null) return true;
        if (tab.IsDirty)
        {
            SelectedTab = tab;
            switch (await _dialogs.ConfirmDiscardAsync(tab.Title))
            {
                case DiscardChoice.Save:
                    if (!await SaveTabAsync(tab, saveAs: false)) return false;
                    break;
                case DiscardChoice.Cancel:
                    return false;
            }
        }

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        if (SelectedTab == tab)
        {
            SelectedTab = Tabs.Count == 0 ? null : Tabs[Math.Min(index, Tabs.Count - 1)];
        }
        await tab.DisposeAsync();
        return true;
    }

    /// <summary>Window close: walk dirty tabs. Returns false to keep the app open.</summary>
    public async Task<bool> RequestExitAsync()
    {
        foreach (var t in Tabs.Where(t => t.IsDirty).ToList())
        {
            SelectedTab = t;
            switch (await _dialogs.ConfirmDiscardAsync(t.Title))
            {
                case DiscardChoice.Save:
                    if (!await SaveTabAsync(t, saveAs: false)) return false;
                    break;
                case DiscardChoice.Cancel:
                    return false;
            }
        }
        return true;
    }

    // ---------- navigation ----------

    [RelayCommand]
    private void NextTab() => CycleTab(+1);

    [RelayCommand]
    private void PreviousTab() => CycleTab(-1);

    private void CycleTab(int delta)
    {
        if (Tabs.Count == 0) return;
        var i = SelectedTab is null ? 0 : Tabs.IndexOf(SelectedTab);
        SelectedTab = Tabs[((i + delta) % Tabs.Count + Tabs.Count) % Tabs.Count];
    }

    [RelayCommand]
    private void SelectTabByIndex(int oneBased)
    {
        if (oneBased >= 1 && oneBased <= Tabs.Count) SelectedTab = Tabs[oneBased - 1];
    }

    [RelayCommand]
    private void ToggleToolbar() => ShowToolbar = !ShowToolbar;

    // ---------- shortcuts (PRD Appendix B) ----------

    public async Task HandleShortcutAsync(string key)
    {
        try
        {
            switch (key)
            {
                case "ctrl+n": NewDocument(); break;
                case "ctrl+o": await OpenWithPickerAsync(); break;
                case "ctrl+s": await SaveAsync(); break;
                case "ctrl+shift+s": await SaveAsAsync(); break;
                case "ctrl+w": await CloseTabAsync(null); break;
                case "ctrl+tab": NextTab(); break;
                case "ctrl+shift+tab": PreviousTab(); break;
                case "ctrl+shift+t": ToggleToolbar(); break;
                case "f5":
                    if (SelectedTab?.Surface is { } s) await s.ExecuteAsync("insert.datetime");
                    break;
                default:
                    if (key.StartsWith("ctrl+alt+", StringComparison.Ordinal) && int.TryParse(key.AsSpan(9), out var n))
                    {
                        SelectTabByIndex(n);
                    }
                    else
                    {
                        _log.LogDebug("shortcut not implemented yet: {Key}", key);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "shortcut {Key} failed", key);
            _dialogs.ShowInfo($"명령 실행 실패: {ex.Message}", isError: true);
        }
    }
}

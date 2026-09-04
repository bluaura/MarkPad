using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkPad.App.Bridge;
using MarkPad.App.Editing;
using MarkPad.App.Services;
using MarkPad.Core.Assets;
using MarkPad.Core.Documents;
using MarkPad.Core.Mru;
using MarkPad.Core.Recovery;
using MarkPad.Core.Settings;
using Microsoft.Extensions.Logging;
using Windows.System;

namespace MarkPad.App.ViewModels;

/// <summary>Tabs, file commands and app shortcuts (ARCHITECTURE.md §3.2 ShellViewModel).</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly DocumentIO _io;
    private readonly SettingsStore _settings;
    private readonly RecentFilesStore _recent;
    private readonly JumpListService _jumpList;
    private readonly ThemeService _theme;
    private readonly AssetService _assets;
    private readonly ExportService _export;
    private readonly RecoveryStore _recovery;
    private readonly IDialogService _dialogs;
    private readonly ILogger<ShellViewModel> _log;

    public ShellViewModel(
        DocumentIO io,
        SettingsStore settings,
        RecentFilesStore recent,
        JumpListService jumpList,
        ThemeService theme,
        AssetService assets,
        ExportService export,
        RecoveryStore recovery,
        IDialogService dialogs,
        ILogger<ShellViewModel> log)
    {
        _io = io;
        _settings = settings;
        _recent = recent;
        _jumpList = jumpList;
        _theme = theme;
        _assets = assets;
        _export = export;
        _recovery = recovery;
        _dialogs = dialogs;
        _log = log;
        ShowToolbar = settings.Current.Ui.ShowToolbar;
        ShowOutline = settings.Current.Ui.ShowOutline;
        ShowSidebar = settings.Current.Ui.ShowFileSidebar;
        Find = new FindViewModel(() => SelectedTab?.Surface);
        Sidebar.OpenRequested += (_, path) => _ = OpenFilesAsync([path]);
        ConfigureAutosave();
        _settings.Changed += (_, _) => ConfigureAutosave();
        Toolbar.ShowHighlight = settings.Current.Markdown.ExtHighlight;
        Toolbar.ImageInsertRequested += (_, _) => _ = InsertImageAsync();
        Tabs.CollectionChanged += OnTabsChanged;
        _recent.Changed += (_, _) => OnPropertyChanged(nameof(RecentFiles));
        _theme.ActualThemeChanged += async (_, _) =>
        {
            foreach (var t in Tabs) await t.ApplyThemeAsync();
        };
        _settings.Changed += (_, _) => Toolbar.ShowHighlight = _settings.Current.Markdown.ExtHighlight;
    }

    public ObservableCollection<DocumentViewModel> Tabs { get; } = [];

    public ToolbarViewModel Toolbar { get; } = new();

    public FindViewModel Find { get; }

    public OutlineViewModel Outline { get; } = new();

    public FileSidebarViewModel Sidebar { get; } = new();

    [ObservableProperty]
    public partial bool ShowOutline { get; set; }

    [ObservableProperty]
    public partial bool ShowSidebar { get; set; }

    partial void OnShowOutlineChanged(bool value)
    {
        Outline.IsOpen = value;
        _ = _settings.UpdateAsync(s => s.Ui.ShowOutline = value);
    }

    partial void OnShowSidebarChanged(bool value)
    {
        Sidebar.IsOpen = value;
        _ = _settings.UpdateAsync(s => s.Ui.ShowFileSidebar = value);
    }

    [RelayCommand]
    private void ToggleOutline() => ShowOutline = !ShowOutline;

    [RelayCommand]
    private void ToggleSidebar() => ShowSidebar = !ShowSidebar;

    // ---------- autosave (PRD F-FILE-07, T-51) ----------

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _autosave;

    private void ConfigureAutosave()
    {
        _autosave?.Stop();
        _autosave = null;
        var s = _settings.Current.Save;
        if (!s.Autosave) return;
        var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (queue is null) return;
        _autosave = queue.CreateTimer();
        _autosave.Interval = TimeSpan.FromSeconds(Math.Max(5, s.AutosaveIntervalSec));
        _autosave.IsRepeating = true;
        _autosave.Tick += async (_, _) => await AutosaveTickAsync();
        _autosave.Start();
    }

    private async Task AutosaveTickAsync()
    {
        foreach (var t in Tabs.Where(t => t.IsDirty && t.Path is not null && !t.Document.IsReadOnly && !t.IsUserReadOnly && !t.HasExternalChange).ToList())
        {
            try
            {
                await t.SaveAsync();
                _log.LogInformation("autosaved {Path}", t.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BridgeException)
            {
                _log.LogWarning(ex, "autosave failed: {Path}", t.Path);
                _dialogs.ShowInfo($"자동 저장 실패: {t.Title} — {ex.Message}", isError: true);
            }
        }
    }

    // ---------- settings (PRD F-SET, T-50) ----------

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        if (await _dialogs.ShowSettingsAsync()) ApplySettings();
    }

    /// <summary>Live-apply what can change without reloading; markdown extensions apply to newly opened tabs.</summary>
    public void ApplySettings()
    {
        ShowToolbar = _settings.Current.Ui.ShowToolbar;
        Toolbar.ShowHighlight = _settings.Current.Markdown.ExtHighlight;
        ConfigureAutosave();
        foreach (var t in Tabs) _ = t.ApplyThemeAsync();
    }

    // ---------- help / default app (PRD 4.1 [도움말], F-FILE-10 / T-54) ----------

    [RelayCommand]
    private Task ShowAboutAsync() => _dialogs.ShowAboutAsync();

    /// <summary>Windows lets only the user pick default apps; open the Settings page for .md (PRD F-FILE-10).</summary>
    [RelayCommand]
    private async Task OpenDefaultAppsAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("ms-settings:defaultapps"));
        _dialogs.ShowInfo("설정 › 기본 앱에서 'MarkPad'를 선택하거나, '.md' 파일 형식에 MarkPad를 지정하세요. (설치 형태가 MSIX일 때만 목록에 나타납니다)");
    }

    /// <summary>First run of a packaged install: point at the default-app setting once (T-54).</summary>
    public async Task OfferDefaultAppOnceAsync()
    {
        if (_settings.Current.Ui.DefaultAppPromptShown || !IsPackaged()) return;
        await _settings.UpdateAsync(s => s.Ui.DefaultAppPromptShown = true);
        _dialogs.ShowInfo("MarkPad를 .md 기본 앱으로 쓰려면 메뉴 › 'Markdown 기본 앱 설정…'을 여세요.");
    }

    private static bool IsPackaged()
    {
        try
        {
            return Windows.ApplicationModel.Package.Current is not null;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    // ---------- zoom (PRD 4.3: Ctrl+= / Ctrl+- / Ctrl+0, 50%~200%) ----------

    [RelayCommand]
    private Task ZoomIn() => SetZoomAsync(_settings.Current.Editor.Zoom + 0.1);

    [RelayCommand]
    private Task ZoomOut() => SetZoomAsync(_settings.Current.Editor.Zoom - 0.1);

    [RelayCommand]
    private Task ZoomReset() => SetZoomAsync(1.0);

    private async Task SetZoomAsync(double zoom)
    {
        zoom = Math.Round(Math.Clamp(zoom, 0.5, 2.0), 2);
        if (Math.Abs(zoom - _settings.Current.Editor.Zoom) < 0.001) return;
        await _settings.UpdateAsync(s => s.Editor.Zoom = zoom);
        foreach (var t in Tabs) await t.ApplyThemeAsync();
        _dialogs.ShowInfo($"확대/축소 {zoom * 100:0}%");
    }

    // ---------- read-only toggle (PRD F-VIEW-09, T-58) ----------

    [RelayCommand]
    private void ToggleReadOnly()
    {
        if (SelectedTab is { } t) t.IsUserReadOnly = !t.IsUserReadOnly;
        AttachSurfaces(SelectedTab);
    }

    // ---------- rich copy (PRD F-EXP-04, T-57) ----------

    [RelayCommand]
    private async Task CopyRichAsync()
    {
        if (SelectedTab?.Surface is not WebEditorSurface web) return;
        try
        {
            var r = await web.Host.Bridge.CallAsync<CopyRichResult>("edit.copyRich", timeout: TimeSpan.FromSeconds(30));
            if (r is null) return;
            _dialogs.SetClipboardRich(r.Html, r.Text);
            _dialogs.ShowInfo("서식 있는 텍스트로 복사했습니다. Word·메일에 붙여넣을 수 있습니다.");
        }
        catch (BridgeException ex)
        {
            _dialogs.ShowInfo($"복사 실패: {ex.Message}", isError: true);
        }
    }

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
        AttachSurfaces(newValue);
    }

    partial void OnShowToolbarChanged(bool value)
    {
        _ = _settings.UpdateAsync(s => s.Ui.ShowToolbar = value);
    }

    private void OnSelectedSurfaceAttached(object? sender, EventArgs e) => AttachSurfaces(sender as DocumentViewModel);

    private void AttachSurfaces(DocumentViewModel? tab)
    {
        var surface = tab?.Surface;
        var editable = tab is { IsUserReadOnly: false, IsReadOnly: false };
        Toolbar.Attach(surface is { SupportsFormatting: true } && editable ? surface : null);
        Find.Reattach(surface);
        Outline.Attach(surface is { SupportsFormatting: true } ? surface : null);
        Sidebar.SetRoot(tab?.Document.Directory ?? Sidebar.RootPath);
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnPropertyChanged(nameof(HasTabs));

    // ---------- tab creation ----------

    private DocumentViewModel CreateTab(Document document, string? suggestedName = null, string? initialText = null)
    {
        var vm = new DocumentViewModel(document, _io, _theme, _settings, _assets, _recovery, suggestedName, initialText);
        vm.ShortcutRequested += (_, e) => _ = HandleShortcutAsync(e.Key);
        vm.TextFileDropped += (_, e) => OpenDroppedText(e);
        vm.LinkOpenRequested += (s, href) => _ = OpenLinkAsync((DocumentViewModel)s!, href);
        vm.Notification += (_, msg) => _dialogs.ShowInfo(msg);
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(DocumentViewModel.Path) && s == SelectedTab) Sidebar.SetRoot(vm.Document.Directory ?? Sidebar.RootPath);
        };
        Tabs.Add(vm);
        SelectedTab = vm;
        return vm;
    }

    // ---------- crash recovery (PRD §5.5, T-42) ----------

    /// <summary>Startup: offer snapshots left by a crashed session. Declined snapshots are discarded.</summary>
    public async Task OfferRecoveryAsync()
    {
        var snapshots = _recovery.List().Where(s => !s.Id.StartsWith($"{Environment.ProcessId}-", StringComparison.Ordinal)).ToList();
        if (snapshots.Count == 0) return;
        var names = string.Join("\n", snapshots.Take(8).Select(s => $"• {s.Title}{(s.Path is null ? " (저장 안 됨)" : $" — {s.Path}")}"));
        var restore = await _dialogs.ConfirmAsync(
            "저장되지 않은 문서 복구",
            $"이전 세션이 비정상 종료되어 저장되지 않은 문서 {snapshots.Count}개가 남아 있습니다.\n\n{names}\n\n복구할까요? (복구하지 않으면 스냅샷은 삭제됩니다)",
            "복구");
        foreach (var s in snapshots)
        {
            if (restore)
            {
                try
                {
                    var text = await _recovery.ReadTextAsync(s);
                    Document doc;
                    if (s.Path is not null && File.Exists(s.Path)) doc = await _io.OpenAsync(s.Path);
                    else doc = DocumentIO.CreateNew(s.Kind);
                    CreateTab(doc, doc.Path is null ? s.Title : null, initialText: text);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _log.LogError(ex, "recovery failed for {Id}", s.Id);
                    _dialogs.ShowInfo($"복구 실패: {s.Title} — {ex.Message}", isError: true);
                }
            }
            _recovery.Delete(s.Id);
        }
    }

    /// <summary>Unhandled exception: persist every dirty tab synchronously before the process dies.</summary>
    public void FlushSnapshotsBlocking()
    {
        foreach (var t in Tabs) t.FlushSnapshotBlocking();
    }

    /// <summary>Normal exit after <see cref="RequestExitAsync"/>: drop snapshots and pending assets.</summary>
    public async Task DisposeAllAsync()
    {
        foreach (var t in Tabs.ToList())
        {
            try { await t.DisposeAsync(); } catch (Exception ex) when (ex is BridgeException or InvalidOperationException) { }
        }
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
                var size = new FileInfo(full).Length;
                if (size > 1_000_000)
                {
                    _dialogs.ShowInfo($"큰 문서입니다 ({size / 1024 / 1024.0:0.0} MB). 렌더링에 몇 초 걸릴 수 있습니다."); // PRD §5.4 / T-43
                }
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

    // ---------- links (PRD F-VIEW-04, T-25) ----------

    private async Task OpenLinkAsync(DocumentViewModel tab, string href)
    {
        try
        {
            if (href.StartsWith('#')) return; // in-document anchor: no-op until outline (T-45)
            if (Uri.TryCreate(href, UriKind.Absolute, out var uri) && !uri.IsFile)
            {
                if (uri.Scheme is "http" or "https" or "mailto") await Launcher.LaunchUriAsync(uri);
                return;
            }

            var local = uri is { IsFile: true } ? uri.LocalPath : Uri.UnescapeDataString(href.Split('#')[0]);
            if (!Path.IsPathRooted(local))
            {
                var baseDir = tab.Document.Directory ?? Environment.CurrentDirectory;
                local = Path.GetFullPath(Path.Combine(baseDir, local.Replace('/', Path.DirectorySeparatorChar)));
            }

            if (!File.Exists(local))
            {
                _dialogs.ShowInfo($"파일을 찾을 수 없습니다: {local}");
                return;
            }
            if (ActivationService.IsOpenable(local))
            {
                await OpenFilesAsync([local]);
                return;
            }
            if (await _dialogs.ConfirmAsync("파일 열기", $"'{Path.GetFileName(local)}'을(를) 연결된 프로그램으로 열까요?\n{local}", "열기"))
            {
                Process.Start(new ProcessStartInfo(local) { UseShellExecute = true });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _log.LogError(ex, "link open failed: {Href}", href);
            _dialogs.ShowInfo($"링크를 열 수 없습니다: {ex.Message}", isError: true);
        }
    }

    // ---------- images (PRD F-IMG-02) ----------

    public async Task InsertImageAsync()
    {
        var tab = SelectedTab;
        if (tab?.Surface is not { SupportsFormatting: true }) return;
        var path = await _dialogs.PickImageAsync();
        if (path is null) return;
        var mode = await _dialogs.AskImageInsertModeAsync(Path.GetFileName(path));
        if (mode == ImageInsertMode.Cancel) return;
        try
        {
            await tab.InsertImageFromFileAsync(path, copy: mode == ImageInsertMode.Copy);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BridgeException)
        {
            _log.LogError(ex, "image insert failed: {Path}", path);
            _dialogs.ShowInfo($"이미지 삽입 실패: {ex.Message}", isError: true);
        }
    }

    // ---------- export / print (PRD F-EXP-01~03) ----------

    [RelayCommand]
    private Task ExportAsync() => ExportAsync(ExportFormat.Html);

    public async Task ExportAsync(ExportFormat initialFormat)
    {
        var tab = SelectedTab;
        if (tab?.Surface is not WebEditorSurface web)
        {
            _dialogs.ShowInfo("내보내기는 Markdown 탭에서만 가능합니다.");
            return;
        }
        var options = await _dialogs.ShowExportOptionsAsync(initialFormat);
        if (options is null) return;
        var ext = options.Format == ExportFormat.Pdf ? ".pdf" : ".html";
        var suggested = await tab.SuggestedFileNameAsync();
        var path = await _dialogs.PickExportPathAsync(suggested, ext, tab.Document.Directory);
        if (path is null) return;
        try
        {
            if (options.Format == ExportFormat.Pdf) await _export.ExportPdfAsync(web, path, options);
            else await _export.ExportHtmlAsync(web, suggested, path, options);
            _dialogs.ShowInfo($"내보내기 완료: {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BridgeException)
        {
            _log.LogError(ex, "export failed: {Path}", path);
            _dialogs.ShowInfo($"내보내기 실패: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private async Task PrintAsync()
    {
        if (SelectedTab?.Surface is not WebEditorSurface web)
        {
            _dialogs.ShowInfo("인쇄는 Markdown 탭에서만 가능합니다.");
            return;
        }
        try
        {
            await _export.PrintAsync(web);
        }
        catch (BridgeException ex)
        {
            _dialogs.ShowInfo($"인쇄 실패: {ex.Message}", isError: true);
        }
    }

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
                case "ctrl+f": Find.Open(replace: false); break;
                case "ctrl+h": Find.Open(replace: true); break;
                case "ctrl+k": Toolbar.RequestLinkFlyout(); break;
                case "ctrl+shift+i": await InsertImageAsync(); break;
                case "ctrl+shift+e": await ExportAsync(ExportFormat.Html); break;
                case "ctrl+p": await PrintAsync(); break;
                case "ctrl+shift+o": ToggleOutline(); break;
                case "ctrl+shift+b": ToggleSidebar(); break;
                case "ctrl+,": await OpenSettingsAsync(); break;
                case "ctrl+=": await ZoomIn(); break;
                case "ctrl+-": await ZoomOut(); break;
                case "ctrl+0": await ZoomReset(); break;
                case "f5":
                    if (SelectedTab?.Surface is { } s) await s.ExecuteAsync("insert.datetime", new InsertTextParams(DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)));
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

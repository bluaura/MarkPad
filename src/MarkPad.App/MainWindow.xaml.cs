using System.Collections.Specialized;
using MarkPad.App.Services;
using MarkPad.App.ViewModels;
using MarkPad.App.Views;
using MarkPad.Core.Documents;
using MarkPad.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;

namespace MarkPad.App;

/// <summary>Shell window (PRD 4.1): tabs in the title bar, toolbar, content host, status bar.</summary>
public sealed partial class MainWindow : Window, IDialogService
{
    private readonly ILogger<MainWindow> _log = App.Current.Services.GetRequiredService<ILogger<MainWindow>>();
    private readonly SettingsStore _settings = App.Current.Services.GetRequiredService<SettingsStore>();
    private readonly ThemeService _theme = App.Current.Services.GetRequiredService<ThemeService>();
    private readonly Dictionary<DocumentViewModel, (TabViewItem Item, DocumentTab View)> _tabViews = [];
    private StartPage? _startPage;
    private bool _syncingSelection;
    private bool _closeConfirmed;

    public MainWindow()
    {
        ViewModel = new ShellViewModel(
            App.Current.Services.GetRequiredService<DocumentIO>(),
            _settings,
            App.Current.Services.GetRequiredService<Core.Mru.RecentFilesStore>(),
            App.Current.Services.GetRequiredService<JumpListService>(),
            _theme,
            App.Current.Services.GetRequiredService<Core.Assets.AssetService>(),
            App.Current.Services.GetRequiredService<ExportService>(),
            App.Current.Services.GetRequiredService<Core.Recovery.RecoveryStore>(),
            this,
            App.Current.Services.GetRequiredService<ILogger<ShellViewModel>>());

        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        Root.RequestedTheme = _theme.RequestedTheme;

        Toolbar.ViewModel = ViewModel.Toolbar;
        FindBarControl.ViewModel = ViewModel.Find;
        OutlinePaneControl.ViewModel = ViewModel.Outline;
        SidebarPane.ViewModel = ViewModel.Sidebar;
        ViewModel.Toolbar.LinkFlyoutRequested += (_, _) => Toolbar.ShowLinkFlyout();
        // settings.json edited by hand (PRD F-SET-05): re-apply on the UI thread.
        _settings.ExternalChange += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            Root.RequestedTheme = _theme.RequestedTheme;
            ViewModel.ApplySettings();
            ShowInfo("settings.json 변경을 반영했습니다.");
        });
        ViewModel.Tabs.CollectionChanged += OnTabsCollectionChanged;
        ViewModel.PropertyChanged += OnShellPropertyChanged;
        AppWindow.Closing += OnAppWindowClosing;
        RestoreWindowPlacement();
        UpdateTitle();
        ShowStartPageIfEmpty();
    }

    public ShellViewModel ViewModel { get; }

    public Task OpenFilesAsync(IEnumerable<string> paths) => ViewModel.OpenFilesAsync(paths);

    // ---------- lifecycle ----------

    private async void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        _theme.NotifyActualTheme(Root.ActualTheme);
        // ContentDialog needs a live XamlRoot, so the crash-recovery offer waits for Loaded (PRD §5.5).
        try
        {
            await ViewModel.OfferRecoveryAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "recovery offer failed");
        }
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        _theme.NotifyActualTheme(Root.ActualTheme);
    }

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed) return;
        args.Cancel = true;
        if (!await ViewModel.RequestExitAsync()) return;
        _closeConfirmed = true;
        await SaveWindowPlacementAsync();
        await ViewModel.DisposeAllAsync(); // drops recovery snapshots + pending assets (normal exit)
        Close();
    }

    private void RestoreWindowPlacement()
    {
        var w = _settings.Current.Ui.Window;
        try
        {
            var rect = new RectInt32(w.X, w.Y, w.W, w.H);
            var area = DisplayArea.GetFromRect(rect, DisplayAreaFallback.Nearest).WorkArea;
            var visible = rect.X < area.X + area.Width - 100 && rect.X + rect.Width > area.X + 100 && rect.Y >= area.Y - 20 && rect.Y < area.Y + area.Height - 100;
            if (!visible)
            {
                rect = new RectInt32(area.X + (area.Width - w.W) / 2, area.Y + (area.Height - w.H) / 2, Math.Min(w.W, area.Width), Math.Min(w.H, area.Height));
            }
            AppWindow.MoveAndResize(rect);
            if (w.Maximized && AppWindow.Presenter is OverlappedPresenter p) p.Maximize();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _log.LogWarning(ex, "window placement restore failed");
        }
    }

    private Task SaveWindowPlacementAsync()
    {
        var maximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        return _settings.UpdateAsync(s =>
        {
            s.Ui.Window.Maximized = maximized;
            if (!maximized)
            {
                s.Ui.Window.X = AppWindow.Position.X;
                s.Ui.Window.Y = AppWindow.Position.Y;
                s.Ui.Window.W = AppWindow.Size.Width;
                s.Ui.Window.H = AppWindow.Size.Height;
            }
        });
    }

    // ---------- tabs ⇄ TabView ----------

    private void OnTabsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                foreach (DocumentViewModel vm in e.NewItems!) AddTabView(vm, e.NewStartingIndex);
                break;
            case NotifyCollectionChangedAction.Remove:
                foreach (DocumentViewModel vm in e.OldItems!) RemoveTabView(vm);
                break;
            case NotifyCollectionChangedAction.Reset:
                foreach (var vm in _tabViews.Keys.ToList()) RemoveTabView(vm);
                break;
        }
        ShowStartPageIfEmpty();
    }

    private void AddTabView(DocumentViewModel vm, int index)
    {
        var view = new DocumentTab(vm) { Visibility = Visibility.Collapsed };
        var item = new TabViewItem
        {
            Header = vm,
            HeaderTemplate = (DataTemplate)Root.Resources["TabHeaderTemplate"],
            IconSource = new FontIconSource { Glyph = vm.IsPlainText ? "" : "" },
            Tag = vm,
        };
        ToolTipService.SetToolTip(item, vm.Path ?? vm.Title);
        _tabViews[vm] = (item, view);
        ContentHost.Children.Add(view);
        Tabs.TabItems.Insert(Math.Min(index, Tabs.TabItems.Count), item);
        vm.PropertyChanged += OnDocumentPropertyChanged;
    }

    private void RemoveTabView(DocumentViewModel vm)
    {
        if (!_tabViews.Remove(vm, out var entry)) return;
        vm.PropertyChanged -= OnDocumentPropertyChanged;
        Tabs.TabItems.Remove(entry.Item);
        ContentHost.Children.Remove(entry.View);
    }

    private void ShowStartPageIfEmpty()
    {
        if (ViewModel.Tabs.Count == 0)
        {
            _startPage ??= new StartPage(ViewModel);
            if (!ContentHost.Children.Contains(_startPage)) ContentHost.Children.Add(_startPage);
            _startPage.Visibility = Visibility.Visible;
        }
        else if (_startPage is not null)
        {
            _startPage.Visibility = Visibility.Collapsed;
        }
    }

    private void OnShellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.SelectedTab))
        {
            SyncSelectedTab();
            UpdateTitle();
        }
    }

    private void OnDocumentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender == ViewModel.SelectedTab && e.PropertyName is nameof(DocumentViewModel.DisplayTitle) or nameof(DocumentViewModel.Path))
        {
            UpdateTitle();
            if (sender is DocumentViewModel vm && _tabViews.TryGetValue(vm, out var entry))
            {
                ToolTipService.SetToolTip(entry.Item, vm.Path ?? vm.Title);
            }
        }
    }

    private void SyncSelectedTab()
    {
        var selected = ViewModel.SelectedTab;
        foreach (var (vm, entry) in _tabViews)
        {
            entry.View.Visibility = vm == selected ? Visibility.Visible : Visibility.Collapsed;
        }
        if (selected is not null && _tabViews.TryGetValue(selected, out var sel) && !ReferenceEquals(Tabs.SelectedItem, sel.Item))
        {
            _syncingSelection = true;
            Tabs.SelectedItem = sel.Item;
            _syncingSelection = false;
        }
        _ = selected?.FocusAsync();
    }

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection) return;
        if (Tabs.SelectedItem is TabViewItem { Tag: DocumentViewModel vm })
        {
            ViewModel.SelectedTab = vm;
        }
    }

    private void OnAddTabClick(TabView sender, object args) => ViewModel.NewDocumentCommand.Execute(null);

    private async void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Item is TabViewItem { Tag: DocumentViewModel vm })
        {
            await ViewModel.TryCloseTabAsync(vm);
        }
    }

    private void UpdateTitle()
    {
        var tab = ViewModel.SelectedTab;
        Title = tab is null ? "MarkPad" : $"{tab.DisplayTitle} - MarkPad";
    }

    // ---------- shortcuts outside the WebView ----------

    private async void OnAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        var parts = new List<string>();
        if (sender.Modifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control)) parts.Add("ctrl");
        if (sender.Modifiers.HasFlag(Windows.System.VirtualKeyModifiers.Menu)) parts.Add("alt");
        if (sender.Modifiers.HasFlag(Windows.System.VirtualKeyModifiers.Shift)) parts.Add("shift");
        var key = sender.Key switch
        {
            >= Windows.System.VirtualKey.Number0 and <= Windows.System.VirtualKey.Number9 => ((int)sender.Key - (int)Windows.System.VirtualKey.Number0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            (Windows.System.VirtualKey)188 => ",", // VK_OEM_COMMA
            _ => sender.Key.ToString().ToLowerInvariant(),
        };
        parts.Add(key);
        await ViewModel.HandleShortcutAsync(string.Join('+', parts));
    }

    // ---------- drag & drop (PRD F-FILE-01) ----------

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems) ? DataPackageOperation.Copy : DataPackageOperation.None;
        e.DragUIOverride.Caption = "MarkPad에서 열기";
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        var paths = items.OfType<StorageFile>().Select(f => f.Path).Where(ActivationService.IsOpenable).ToList();
        if (paths.Count > 0) await ViewModel.OpenFilesAsync(paths);
    }

    // ---------- IDialogService ----------

    public async Task<IReadOnlyList<string>> PickOpenFilesAsync()
    {
        var picker = new FileOpenPicker(AppWindow.Id)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            CommitButtonText = "열기",
        };
        picker.FileTypeFilter.Add(".md");
        picker.FileTypeFilter.Add(".markdown");
        picker.FileTypeFilter.Add(".txt");
        var results = await picker.PickMultipleFilesAsync();
        return results?.Select(r => r.Path).Where(p => !string.IsNullOrEmpty(p)).ToList() ?? [];
    }

    public async Task<string?> PickSavePathAsync(string suggestedName, DocumentKind kind, string? initialDirectory)
    {
        var picker = new FileSavePicker(AppWindow.Id)
        {
            SuggestedFileName = suggestedName,
            DefaultFileExtension = kind == DocumentKind.PlainText ? ".txt" : ".md",
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            CommitButtonText = "저장",
        };
        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory)) picker.SuggestedFolder = initialDirectory;
        if (kind == DocumentKind.PlainText)
        {
            picker.FileTypeChoices.Add("텍스트", [".txt"]);
            picker.FileTypeChoices.Add("Markdown", [".md", ".markdown"]);
        }
        else
        {
            picker.FileTypeChoices.Add("Markdown", [".md", ".markdown"]);
            picker.FileTypeChoices.Add("텍스트", [".txt"]);
        }
        var result = await picker.PickSaveFileAsync();
        return result?.Path is { Length: > 0 } p ? p : null;
    }

    public async Task<string?> PickImageAsync()
    {
        var picker = new FileOpenPicker(AppWindow.Id)
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            CommitButtonText = "삽입",
            ViewMode = PickerViewMode.Thumbnail,
        };
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg", ".avif" }) picker.FileTypeFilter.Add(ext);
        var result = await picker.PickSingleFileAsync();
        return result?.Path is { Length: > 0 } p ? p : null;
    }

    public async Task<ImageInsertMode> AskImageInsertModeAsync(string fileName)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "이미지 삽입",
            Content = $"'{fileName}'을(를) 문서의 '{_settings.Current.Images.FolderName}' 폴더로 복사할까요, 아니면 원본 경로를 그대로 참조할까요?",
            PrimaryButtonText = "복사",
            SecondaryButtonText = "원본 경로 참조",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => ImageInsertMode.Copy,
            ContentDialogResult.Secondary => ImageInsertMode.Reference,
            _ => ImageInsertMode.Cancel,
        };
    }

    public async Task<ExportOptions?> ShowExportOptionsAsync(ExportFormat initialFormat)
    {
        var dialog = new Dialogs.ExportDialog(initialFormat) { XamlRoot = Root.XamlRoot };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? dialog.Options : null;
    }

    public async Task<string?> PickExportPathAsync(string suggestedName, string extension, string? initialDirectory)
    {
        var picker = new FileSavePicker(AppWindow.Id)
        {
            SuggestedFileName = suggestedName,
            DefaultFileExtension = extension,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            CommitButtonText = "내보내기",
        };
        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory)) picker.SuggestedFolder = initialDirectory;
        picker.FileTypeChoices.Add(extension == ".pdf" ? "PDF" : "HTML", [extension]);
        var result = await picker.PickSaveFileAsync();
        return result?.Path is { Length: > 0 } p ? p : null;
    }

    public async Task<bool> ShowSettingsAsync()
    {
        var dialog = new Dialogs.SettingsDialog(_settings.Current, _settings.Path) { XamlRoot = Root.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return false;
        await _settings.UpdateAsync(dialog.ApplyTo);
        Root.RequestedTheme = _theme.RequestedTheme;
        return true;
    }

    public void SetClipboardRich(string html, string text)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(text);
        package.SetHtmlFormat(HtmlFormatHelper.CreateHtmlFormat(html));
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    public async Task<bool> ConfirmAsync(string title, string message, string primaryText)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = primaryText,
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task<DiscardChoice> ConfirmDiscardAsync(string fileName)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "저장되지 않은 변경 사항",
            Content = $"'{fileName}'의 변경 사항을 저장할까요?",
            PrimaryButtonText = "저장",
            SecondaryButtonText = "저장 안 함",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => DiscardChoice.Save,
            ContentDialogResult.Secondary => DiscardChoice.Discard,
            _ => DiscardChoice.Cancel,
        };
    }

    public void ShowInfo(string message, bool isError = false)
    {
        Notice.Severity = isError ? InfoBarSeverity.Error : InfoBarSeverity.Informational;
        Notice.Message = message;
        Notice.IsOpen = true;
    }
}

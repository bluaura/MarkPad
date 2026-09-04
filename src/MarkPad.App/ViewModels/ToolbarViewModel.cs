using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkPad.App.Bridge;
using MarkPad.App.Editing;

namespace MarkPad.App.ViewModels;

/// <summary>
/// Toolbar state + commands (ARCHITECTURE.md §3.2 FormatToolbar, PRD Appendix A). Toggle state comes from the
/// surface's <c>selection</c> event; commands are forwarded to the attached surface. Null surface = disabled (.txt tabs).
/// </summary>
public sealed partial class ToolbarViewModel : ObservableObject
{
    private IEditorSurface? _surface;

    [ObservableProperty] public partial bool IsBold { get; set; }
    [ObservableProperty] public partial bool IsItalic { get; set; }
    [ObservableProperty] public partial bool IsStrike { get; set; }
    [ObservableProperty] public partial bool IsCode { get; set; }
    [ObservableProperty] public partial bool IsHighlight { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeadingLabel))]
    public partial int HeadingLevel { get; set; }

    [ObservableProperty] public partial bool IsBulletList { get; set; }
    [ObservableProperty] public partial bool IsOrderedList { get; set; }
    [ObservableProperty] public partial bool IsTaskList { get; set; }
    [ObservableProperty] public partial bool IsBlockquote { get; set; }
    [ObservableProperty] public partial bool InTable { get; set; }
    [ObservableProperty] public partial bool InCode { get; set; }
    [ObservableProperty] public partial bool HasSelection { get; set; }
    [ObservableProperty] public partial string? CurrentLink { get; set; }
    [ObservableProperty] public partial bool IsEnabled { get; set; }

    /// <summary>PRD Appendix A: the highlight button exists only when the `==` extension is on (default OFF).</summary>
    [ObservableProperty] public partial bool ShowHighlight { get; set; }

    public string HeadingLabel => HeadingLevel == 0 ? "본문" : $"H{HeadingLevel}";

    /// <summary>Native UI that needs the window: link flyout (Ctrl+K) and image picker (Ctrl+Shift+I).</summary>
    public event EventHandler? LinkFlyoutRequested;
    public event EventHandler? ImageInsertRequested;

    /// <summary>Switches the toolbar to a tab's surface (null = no formatting, e.g. a .txt tab).</summary>
    public void Attach(IEditorSurface? surface)
    {
        if (_surface is not null) _surface.SelectionChanged -= OnSelection;
        _surface = surface;
        if (_surface is not null) _surface.SelectionChanged += OnSelection;
        IsEnabled = surface is not null;
        Apply(SelectionContext.Empty);
    }

    public void RequestLinkFlyout() => LinkFlyoutRequested?.Invoke(this, EventArgs.Empty);

    private void OnSelection(object? sender, SelectionContext ctx) => Apply(ctx);

    private void Apply(SelectionContext ctx)
    {
        IsBold = ctx.Bold;
        IsItalic = ctx.Italic;
        IsStrike = ctx.Strike;
        IsCode = ctx.Code;
        IsHighlight = ctx.Highlight;
        IsBlockquote = ctx.Blockquote;
        IsBulletList = ctx.List == "bullet";
        IsOrderedList = ctx.List == "ordered";
        IsTaskList = ctx.List == "task";
        InTable = ctx.InTable;
        InCode = ctx.InCode;
        HasSelection = ctx.HasSelection;
        CurrentLink = ctx.Link;
        HeadingLevel = ctx.Heading;
    }

    private async Task RunAsync(string method, object? p = null)
    {
        if (_surface is null || !_surface.IsReady) return;
        try
        {
            await _surface.ExecuteAsync(method, p);
            await _surface.FocusAsync();
        }
        catch (BridgeException)
        {
            // Surfaced through the InfoBar in T-42; the toolbar itself stays quiet.
        }
    }

    // 문단
    [RelayCommand] private Task SetHeading(string level) => RunAsync("format.heading", new FormatHeadingParams(int.Parse(level, System.Globalization.CultureInfo.InvariantCulture)));

    // 인라인
    [RelayCommand] private Task ToggleBold() => RunAsync("format.toggle", new FormatToggleParams("bold"));
    [RelayCommand] private Task ToggleItalic() => RunAsync("format.toggle", new FormatToggleParams("italic"));
    [RelayCommand] private Task ToggleStrike() => RunAsync("format.toggle", new FormatToggleParams("strike"));
    [RelayCommand] private Task ToggleCode() => RunAsync("format.toggle", new FormatToggleParams("code"));
    [RelayCommand] private Task ToggleHighlight() => RunAsync("format.toggle", new FormatToggleParams("highlight"));

    // 목록
    [RelayCommand] private Task ToggleBulletList() => RunAsync("format.list", new FormatListParams("bullet"));
    [RelayCommand] private Task ToggleOrderedList() => RunAsync("format.list", new FormatListParams("ordered"));
    [RelayCommand] private Task ToggleTaskList() => RunAsync("format.list", new FormatListParams("task"));
    [RelayCommand] private Task Indent() => RunAsync("format.indent");
    [RelayCommand] private Task Outdent() => RunAsync("format.outdent");

    // 블록
    [RelayCommand] private Task ToggleBlockquote() => RunAsync("format.blockquote");
    [RelayCommand] private Task InsertCodeBlock() => RunAsync("insert.codeBlock", new InsertCodeBlockParams(null));
    [RelayCommand] private Task InsertHr() => RunAsync("insert.hr");

    // 삽입
    [RelayCommand] private Task InsertLink(LinkRequest? link) => link is null || string.IsNullOrWhiteSpace(link.Href) ? Task.CompletedTask : RunAsync("insert.link", new InsertLinkParams(link.Href.Trim(), string.IsNullOrWhiteSpace(link.Text) ? null : link.Text));
    [RelayCommand] private void RequestImage() => ImageInsertRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private Task InsertTable() => RunAsync("insert.table", new InsertTableParams(3, 3));
    [RelayCommand] private Task InsertTableSized(TableSize? size) => size is null ? Task.CompletedTask : RunAsync("insert.table", new InsertTableParams(size.Rows, size.Cols));
    [RelayCommand] private Task InsertMathInline() => RunAsync("insert.math", new InsertMathParams(false));
    [RelayCommand] private Task InsertMathBlock() => RunAsync("insert.math", new InsertMathParams(true));
    [RelayCommand] private Task InsertFootnote() => RunAsync("insert.footnote", new InsertTextParams(string.Empty));
    [RelayCommand] private Task InsertDateTime() => RunAsync("insert.datetime", new InsertTextParams(DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)));

    // 편집
    [RelayCommand] private Task ClearFormat() => RunAsync("format.clear");
    [RelayCommand] private Task Undo() => RunAsync("history.undo");
    [RelayCommand] private Task Redo() => RunAsync("history.redo");
}

public sealed record LinkRequest(string Href, string? Text);

public sealed record TableSize(int Rows, int Cols);

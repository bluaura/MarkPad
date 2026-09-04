using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkPad.App.Bridge;
using MarkPad.App.Editing;

namespace MarkPad.App.ViewModels;

/// <summary>
/// Toolbar state + commands (ARCHITECTURE.md §3.2 FormatToolbar). Toggle state comes from the surface's
/// <c>selection</c> event; commands are forwarded to the attached surface. Null surface = disabled (.txt tabs).
/// </summary>
public sealed partial class ToolbarViewModel : ObservableObject
{
    private IEditorSurface? _surface;

    [ObservableProperty]
    public partial bool IsBold { get; set; }

    [ObservableProperty]
    public partial bool IsItalic { get; set; }

    [ObservableProperty]
    public partial bool IsStrike { get; set; }

    [ObservableProperty]
    public partial bool IsCode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeadingLabel))]
    public partial int HeadingLevel { get; set; }

    [ObservableProperty]
    public partial bool IsBulletList { get; set; }

    [ObservableProperty]
    public partial bool IsOrderedList { get; set; }

    [ObservableProperty]
    public partial bool IsTaskList { get; set; }

    [ObservableProperty]
    public partial bool IsBlockquote { get; set; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    public string HeadingLabel => HeadingLevel == 0 ? "본문" : $"H{HeadingLevel}";

    /// <summary>Switches the toolbar to a tab's surface (null = no formatting, e.g. a .txt tab).</summary>
    public void Attach(IEditorSurface? surface)
    {
        if (_surface is not null) _surface.SelectionChanged -= OnSelection;
        _surface = surface;
        if (_surface is not null) _surface.SelectionChanged += OnSelection;
        IsEnabled = surface is not null;
        Apply(SelectionContext.Empty);
    }

    private void OnSelection(object? sender, SelectionContext ctx) => Apply(ctx);

    private void Apply(SelectionContext ctx)
    {
        IsBold = ctx.Bold;
        IsItalic = ctx.Italic;
        IsStrike = ctx.Strike;
        IsCode = ctx.Code;
        IsBlockquote = ctx.Blockquote;
        IsBulletList = ctx.List == "bullet";
        IsOrderedList = ctx.List == "ordered";
        IsTaskList = ctx.List == "task";
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

    [RelayCommand] private Task ToggleBold() => RunAsync("format.toggle", new FormatToggleParams("bold"));
    [RelayCommand] private Task ToggleItalic() => RunAsync("format.toggle", new FormatToggleParams("italic"));
    [RelayCommand] private Task ToggleStrike() => RunAsync("format.toggle", new FormatToggleParams("strike"));
    [RelayCommand] private Task ToggleCode() => RunAsync("format.toggle", new FormatToggleParams("code"));
    [RelayCommand] private Task SetHeading(string level) => RunAsync("format.heading", new FormatHeadingParams(int.Parse(level, System.Globalization.CultureInfo.InvariantCulture)));
    [RelayCommand] private Task ToggleBulletList() => RunAsync("format.list", new FormatListParams("bullet"));
    [RelayCommand] private Task ToggleOrderedList() => RunAsync("format.list", new FormatListParams("ordered"));
    [RelayCommand] private Task ToggleTaskList() => RunAsync("format.list", new FormatListParams("task"));
    [RelayCommand] private Task ToggleBlockquote() => RunAsync("format.blockquote");
    [RelayCommand] private Task InsertCodeBlock() => RunAsync("insert.codeBlock", new InsertCodeBlockParams(null));
    [RelayCommand] private Task InsertHr() => RunAsync("insert.hr");
    [RelayCommand] private Task InsertTable() => RunAsync("insert.table", new InsertTableParams(3, 3));
    [RelayCommand] private Task Undo() => RunAsync("history.undo");
    [RelayCommand] private Task Redo() => RunAsync("history.redo");
}

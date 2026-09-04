using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkPad.App.Bridge;
using MarkPad.App.Editing;

namespace MarkPad.App.ViewModels;

/// <summary>Find/replace bar state (PRD F-EDIT-11). The surface owns matching; this mirrors its results.</summary>
public sealed partial class FindViewModel : ObservableObject
{
    private readonly Func<IEditorSurface?> _surface;
    private IEditorSurface? _attached;

    public FindViewModel(Func<IEditorSurface?> surface)
    {
        _surface = surface;
    }

    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty] public partial bool ShowReplace { get; set; }
    [ObservableProperty] public partial string Query { get; set; } = string.Empty;
    [ObservableProperty] public partial string Replacement { get; set; } = string.Empty;
    [ObservableProperty] public partial bool CaseSensitive { get; set; }
    [ObservableProperty] public partial bool WholeWord { get; set; }
    [ObservableProperty] public partial string ResultLabel { get; set; } = string.Empty;
    [ObservableProperty] public partial int Count { get; set; }
    [ObservableProperty] public partial int Index { get; set; } = -1;

    /// <summary>The bar should take keyboard focus (Ctrl+F / Ctrl+H).</summary>
    public event EventHandler? FocusRequested;

    public void Open(bool replace)
    {
        IsOpen = true;
        if (replace) ShowReplace = true;
        FocusRequested?.Invoke(this, EventArgs.Empty);
        if (Query.Length > 0) _ = ApplyAsync();
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        IsOpen = false;
        ShowReplace = false;
        ResultLabel = string.Empty;
        var s = _surface();
        if (s is not null)
        {
            await SafeAsync(() => s.FindAsync("find.clear"));
            await s.FocusAsync();
        }
    }

    [RelayCommand]
    private Task ToggleReplace()
    {
        ShowReplace = !ShowReplace;
        return Task.CompletedTask;
    }

    /// <summary>Follow the active tab: results come from whichever surface is current.</summary>
    public void Reattach(IEditorSurface? surface)
    {
        if (_attached is not null) _attached.FindResultChanged -= OnResult;
        _attached = surface;
        if (_attached is not null) _attached.FindResultChanged += OnResult;
        if (IsOpen && Query.Length > 0) _ = ApplyAsync();
    }

    partial void OnQueryChanged(string value) => _ = ApplyAsync();
    partial void OnCaseSensitiveChanged(bool value) => _ = ApplyAsync();
    partial void OnWholeWordChanged(bool value) => _ = ApplyAsync();

    private async Task ApplyAsync()
    {
        var s = _surface();
        if (s is null || !IsOpen) return;
        if (Query.Length == 0)
        {
            await SafeAsync(() => s.FindAsync("find.clear"));
            ResultLabel = string.Empty;
            return;
        }
        await SafeAsync(() => s.FindAsync("find.set", new FindParams(Query, CaseSensitive, WholeWord)));
    }

    [RelayCommand]
    private Task Next() => Step("find.next");

    [RelayCommand]
    private Task Previous() => Step("find.prev");

    private async Task Step(string method)
    {
        var s = _surface();
        if (s is null || Query.Length == 0) return;
        await SafeAsync(() => s.FindAsync(method));
    }

    [RelayCommand]
    private async Task Replace()
    {
        var s = _surface();
        if (s is null || Query.Length == 0) return;
        await SafeAsync(() => s.FindAsync("find.replace", new FindReplaceParams(Replacement)));
    }

    [RelayCommand]
    private async Task ReplaceAll()
    {
        var s = _surface();
        if (s is null || Query.Length == 0) return;
        await SafeAsync(() => s.FindAsync("find.replaceAll", new FindReplaceParams(Replacement)));
    }

    private void OnResult(object? sender, FindResult r)
    {
        Count = r.Count;
        Index = r.Index;
        ResultLabel = r.Count == 0 ? (Query.Length == 0 ? string.Empty : "결과 없음") : $"{r.Index + 1}/{r.Count}";
    }

    private static async Task SafeAsync(Func<Task<FindResult>> op)
    {
        try
        {
            await op();
        }
        catch (BridgeException)
        {
            // editor not ready yet; the next keystroke retries
        }
    }
}

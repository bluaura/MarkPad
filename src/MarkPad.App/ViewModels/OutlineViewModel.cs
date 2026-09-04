using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkPad.App.Bridge;
using MarkPad.App.Editing;

namespace MarkPad.App.ViewModels;

public sealed partial class OutlineEntry : ObservableObject
{
    public OutlineEntry(OutlineItem item)
    {
        Level = item.Level;
        Text = string.IsNullOrWhiteSpace(item.Text) ? "(제목 없음)" : item.Text;
        Pos = item.Pos;
    }

    public int Level { get; }
    public string Text { get; }
    public int Pos { get; }
    public double Indent => (Level - 1) * 14;
    public Microsoft.UI.Xaml.Thickness IndentMargin => new(Indent, 0, 0, 0);
    public Windows.UI.Text.FontWeight Weight => Level <= 2 ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;

    [ObservableProperty]
    public partial bool IsActive { get; set; }
}

/// <summary>Heading tree of the active tab (PRD F-VIEW-05, T-45).</summary>
public sealed partial class OutlineViewModel : ObservableObject
{
    private IEditorSurface? _surface;

    public ObservableCollection<OutlineEntry> Items { get; } = [];

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    public bool IsEmpty => Items.Count == 0;

    public void Attach(IEditorSurface? surface)
    {
        if (_surface is not null) _surface.OutlineChanged -= OnOutline;
        _surface = surface;
        Items.Clear();
        OnPropertyChanged(nameof(IsEmpty));
        if (_surface is not null)
        {
            _surface.OutlineChanged += OnOutline;
            _ = _surface.ExecuteAsync("outline.refresh").ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    private void OnOutline(object? sender, OutlineEvent e)
    {
        // Rebuild only when the structure changed; otherwise just move the active marker.
        var same = Items.Count == e.Items.Length && Items.Zip(e.Items).All(p => p.First.Pos == p.Second.Pos && p.First.Text == p.Second.Text && p.First.Level == p.Second.Level);
        if (!same)
        {
            Items.Clear();
            foreach (var i in e.Items) Items.Add(new OutlineEntry(i));
            OnPropertyChanged(nameof(IsEmpty));
        }
        for (var i = 0; i < Items.Count; i++) Items[i].IsActive = i == e.Active;
    }

    [RelayCommand]
    private async Task Goto(OutlineEntry? entry)
    {
        if (entry is null || _surface is null) return;
        try
        {
            await _surface.ExecuteAsync("outline.goto", new OutlineGotoParams(entry.Pos));
        }
        catch (BridgeException)
        {
        }
    }
}

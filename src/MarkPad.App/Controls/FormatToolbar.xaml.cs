using MarkPad.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace MarkPad.App.Controls;

public sealed partial class FormatToolbar : UserControl
{
    private const int GridMax = 8;
    private ToolbarViewModel _viewModel = new();
    private readonly Border[,] _cells = new Border[GridMax, GridMax];

    public FormatToolbar()
    {
        InitializeComponent();
        BuildTableGrid();
    }

    /// <summary>Set by the shell so one toolbar follows the active tab (ARCHITECTURE §3.2).</summary>
    public ToolbarViewModel ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = value;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            Bindings.Update();
        }
    }

    public Visibility LinkTextVisibility => ViewModel.HasSelection ? Visibility.Collapsed : Visibility.Visible;

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ToolbarViewModel.HasSelection)) Bindings.Update();
    }

    // ---------- link flyout (PRD F-EDIT-10, Ctrl+K) ----------

    public void ShowLinkFlyout()
    {
        if (!ViewModel.IsEnabled) return;
        LinkFlyout.ShowAt(LinkButton);
    }

    private void OnLinkFlyoutOpened(object? sender, object e)
    {
        LinkUrlBox.Text = ViewModel.CurrentLink ?? string.Empty;
        LinkTextBox.Text = string.Empty;
        Bindings.Update();
        LinkUrlBox.Focus(FocusState.Programmatic);
        LinkUrlBox.SelectAll();
    }

    private void OnLinkKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            CommitLink();
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            LinkFlyout.Hide();
        }
    }

    private void OnLinkInsertClick(object sender, RoutedEventArgs e) => CommitLink();

    private void CommitLink()
    {
        var href = LinkUrlBox.Text.Trim();
        if (href.Length == 0) return;
        LinkFlyout.Hide();
        ViewModel.InsertLinkCommand.Execute(new LinkRequest(href, ViewModel.HasSelection ? null : LinkTextBox.Text));
    }

    // ---------- table size picker (PRD Appendix A: 행×열 그리드, 기본 3×3) ----------

    private void BuildTableGrid()
    {
        for (var i = 0; i < GridMax; i++)
        {
            TableGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
            TableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        }
        for (var r = 0; r < GridMax; r++)
        {
            for (var c = 0; c < GridMax; c++)
            {
                var cell = new Border
                {
                    Margin = new Thickness(1),
                    CornerRadius = new CornerRadius(2),
                    BorderThickness = new Thickness(1),
                    Tag = (r, c),
                };
                cell.PointerEntered += OnTableCellHover;
                cell.Tapped += OnTableCellTapped;
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                TableGrid.Children.Add(cell);
                _cells[r, c] = cell;
            }
        }
        HighlightTableCells(3, 3);
    }

    private void HighlightTableCells(int rows, int cols)
    {
        var on = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        var off = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"];
        var border = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"];
        for (var r = 0; r < GridMax; r++)
        {
            for (var c = 0; c < GridMax; c++)
            {
                _cells[r, c].Background = r < rows && c < cols ? on : off;
                _cells[r, c].BorderBrush = border;
            }
        }
        TableSizeLabel.Text = $"{rows} × {cols}";
    }

    private void OnTableCellHover(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border { Tag: (int r, int c) }) HighlightTableCells(r + 1, c + 1);
    }

    private void OnTableCellTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Border { Tag: (int r, int c) })
        {
            TableFlyout.Hide();
            ViewModel.InsertTableSizedCommand.Execute(new TableSize(r + 1, c + 1));
        }
    }
}

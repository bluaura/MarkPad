using MarkPad.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace MarkPad.App.Views;

public sealed partial class OutlinePane : UserControl
{
    private OutlineViewModel _viewModel = new();

    public OutlinePane()
    {
        InitializeComponent();
    }

    public OutlineViewModel ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value;
            Bindings.Update();
        }
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is OutlineEntry entry) ViewModel.GotoCommand.Execute(entry);
    }
}

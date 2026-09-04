using MarkPad.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace MarkPad.App.Controls;

public sealed partial class FormatToolbar : UserControl
{
    private ToolbarViewModel _viewModel = new();

    public FormatToolbar()
    {
        InitializeComponent();
    }

    /// <summary>Set by the shell so one toolbar follows the active tab (ARCHITECTURE §3.2).</summary>
    public ToolbarViewModel ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value;
            Bindings.Update();
        }
    }
}

using MarkPad.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace MarkPad.App.Controls;

public sealed partial class FormatToolbar : UserControl
{
    public FormatToolbar()
    {
        InitializeComponent();
    }

    public ToolbarViewModel ViewModel { get; } = new();
}

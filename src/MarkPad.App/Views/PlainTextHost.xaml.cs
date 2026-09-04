using MarkPad.App.Bridge;
using Microsoft.UI.Xaml.Controls;

namespace MarkPad.App.Views;

public sealed partial class PlainTextHost : UserControl
{
    public PlainTextHost()
    {
        InitializeComponent();
    }

    public TextBox Box => TextBoxControl;

    public void ApplyTheme(EditorThemeDto theme)
    {
        Box.FontSize = Math.Max(12, theme.FontSize - 1) * theme.Zoom;
    }
}

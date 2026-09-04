using MarkPad.App.Services;
using Microsoft.UI.Xaml.Controls;

namespace MarkPad.App.Dialogs;

/// <summary>Export options (PRD F-EXP-01/02): format, embedded images, theme, page size and margins.</summary>
public sealed partial class ExportDialog : ContentDialog
{
    public ExportDialog(ExportFormat initialFormat)
    {
        InitializeComponent();
        FormatChoice.SelectedIndex = initialFormat == ExportFormat.Pdf ? 1 : 0;
    }

    public ExportOptions Options => new(
        FormatChoice.SelectedIndex == 1 ? ExportFormat.Pdf : ExportFormat.Html,
        InlineImages: InlineImagesBox.IsChecked == true,
        DarkTheme: DarkThemeBox.IsChecked == true,
        PageSize: PageSizeBox.SelectedIndex == 1 ? "Letter" : "A4",
        MarginMm: MarginBox.SelectedItem is ComboBoxItem { Tag: string t } && double.TryParse(t, System.Globalization.CultureInfo.InvariantCulture, out var mm) ? mm : 20);
}

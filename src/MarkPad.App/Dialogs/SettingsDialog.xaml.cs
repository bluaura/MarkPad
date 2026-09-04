using MarkPad.Core.Settings;
using Microsoft.UI.Xaml.Controls;

namespace MarkPad.App.Dialogs;

/// <summary>Settings page (PRD F-SET-01~05, T-50). Edits a copy; <see cref="ApplyTo"/> writes back on OK.</summary>
public sealed partial class SettingsDialog : ContentDialog
{
    public SettingsDialog(AppSettings s, string settingsPath)
    {
        InitializeComponent();
        SelectTag(ThemeBox, s.Theme);
        FontFamilyBox.Text = s.Editor.FontFamily;
        FontSizeBox.Value = s.Editor.FontSize;
        LineHeightBox.Value = s.Editor.LineHeight;
        MaxWidthBox.Value = s.Editor.MaxWidth;
        ImageFolderBox.Text = s.Images.FolderName;
        AllowRemoteBox.IsOn = s.Images.AllowRemote;
        SelectTag(EolBox, s.Save.Eol);
        TrailingNewlineBox.IsOn = s.Save.EnsureTrailingNewline;
        AutosaveBox.IsOn = s.Save.Autosave;
        AutosaveIntervalBox.Value = s.Save.AutosaveIntervalSec;
        SelectTag(EmphasisBox, s.Markdown.Emphasis);
        SelectTag(BulletBox, s.Markdown.Bullet);
        SelectTag(IndentBox, s.Markdown.ListIndent.ToString(System.Globalization.CultureInfo.InvariantCulture));
        HighlightBox.IsOn = s.Markdown.ExtHighlight;
        MathBox.IsOn = s.Markdown.ExtMath;
        MermaidBox.IsOn = s.Markdown.ExtMermaid;
        FrontMatterBox.IsOn = s.Markdown.FrontMatter;
        PathText.Text = $"설정 파일: {settingsPath} (직접 편집하면 즉시 반영됩니다)";
    }

    public void ApplyTo(AppSettings s)
    {
        s.Theme = TagOf(ThemeBox, "system");
        s.Editor.FontFamily = string.IsNullOrWhiteSpace(FontFamilyBox.Text) ? EditorSettings.DefaultFontFamily : FontFamilyBox.Text.Trim();
        s.Editor.FontSize = double.IsNaN(FontSizeBox.Value) ? 15 : FontSizeBox.Value;
        s.Editor.LineHeight = double.IsNaN(LineHeightBox.Value) ? 1.7 : LineHeightBox.Value;
        s.Editor.MaxWidth = double.IsNaN(MaxWidthBox.Value) ? 800 : MaxWidthBox.Value;
        s.Images.FolderName = string.IsNullOrWhiteSpace(ImageFolderBox.Text) ? "assets" : ImageFolderBox.Text.Trim();
        s.Images.AllowRemote = AllowRemoteBox.IsOn;
        s.Save.Eol = TagOf(EolBox, "preserve");
        s.Save.EnsureTrailingNewline = TrailingNewlineBox.IsOn;
        s.Save.Autosave = AutosaveBox.IsOn;
        s.Save.AutosaveIntervalSec = double.IsNaN(AutosaveIntervalBox.Value) ? 30 : (int)AutosaveIntervalBox.Value;
        s.Markdown.Emphasis = TagOf(EmphasisBox, "*");
        s.Markdown.Bullet = TagOf(BulletBox, "-");
        s.Markdown.ListIndent = int.Parse(TagOf(IndentBox, "2"), System.Globalization.CultureInfo.InvariantCulture);
        s.Markdown.ExtHighlight = HighlightBox.IsOn;
        s.Markdown.ExtMath = MathBox.IsOn;
        s.Markdown.ExtMermaid = MermaidBox.IsOn;
        s.Markdown.FrontMatter = FrontMatterBox.IsOn;
        s.Validate();
    }

    private static void SelectTag(ComboBox box, string tag)
    {
        for (var i = 0; i < box.Items.Count; i++)
        {
            if (box.Items[i] is ComboBoxItem { Tag: string t } && t == tag)
            {
                box.SelectedIndex = i;
                return;
            }
        }
        box.SelectedIndex = 0;
    }

    private static string TagOf(ComboBox box, string fallback) => box.SelectedItem is ComboBoxItem { Tag: string t } ? t : fallback;
}

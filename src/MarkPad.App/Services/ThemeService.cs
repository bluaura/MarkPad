using MarkPad.App.Bridge;
using MarkPad.Core.Settings;
using Microsoft.UI.Xaml;

namespace MarkPad.App.Services;

/// <summary>Maps settings.theme (light|dark|system) to XAML and editor themes (PRD F-VIEW-08, T-33).</summary>
public sealed class ThemeService
{
    private readonly SettingsStore _settings;

    public ThemeService(SettingsStore settings)
    {
        _settings = settings;
    }

    /// <summary>The theme currently rendered by XAML (system-resolved). Updated by the window.</summary>
    public ElementTheme ActualTheme { get; set; } = ElementTheme.Light;

    public event EventHandler? ActualThemeChanged;

    public ElementTheme RequestedTheme => _settings.Current.Theme switch
    {
        "light" => ElementTheme.Light,
        "dark" => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    public void NotifyActualTheme(ElementTheme actual)
    {
        if (ActualTheme == actual) return;
        ActualTheme = actual;
        ActualThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public EditorThemeDto BuildEditorTheme()
    {
        var e = _settings.Current.Editor;
        var family = e.FontFamily.Contains(',') ? e.FontFamily : $"'{e.FontFamily}', 'Segoe UI Variable Text', 'Segoe UI', 'Malgun Gothic', sans-serif";
        return new EditorThemeDto(
            Mode: ActualTheme == ElementTheme.Dark ? "dark" : "light",
            FontFamily: family,
            FontSize: e.FontSize,
            LineHeight: e.LineHeight,
            MaxWidth: e.MaxWidth,
            Zoom: e.Zoom);
    }

    public EditorSettingsDto BuildEditorSettings()
    {
        var m = _settings.Current.Markdown;
        return new EditorSettingsDto(
            BuildEditorTheme(),
            new MarkdownStyleDto(m.Emphasis, m.Bullet, m.ListIndent, m.ExtHighlight, m.ExtMath, m.ExtMermaid, m.FrontMatter),
            _settings.Current.Images.AllowRemote);
    }
}

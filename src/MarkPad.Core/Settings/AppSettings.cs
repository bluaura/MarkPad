using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarkPad.Core.Settings;

/// <summary>
/// settings.json model (ARCHITECTURE.md §3.1). Unknown keys are preserved through <see cref="Extra"/>
/// so hand-edited or newer-version files survive a round trip.
/// </summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>light | dark | system</summary>
    public string Theme { get; set; } = "system";

    public EditorSettings Editor { get; set; } = new();
    public ImageSettings Images { get; set; } = new();
    public SaveSettings Save { get; set; } = new();
    public MarkdownSettings Markdown { get; set; } = new();
    public UiSettings Ui { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Clamp/normalize values so a corrupt file cannot break the UI.</summary>
    public void Validate()
    {
        if (Theme is not ("light" or "dark" or "system")) Theme = "system";
        Editor.FontSize = Math.Clamp(Editor.FontSize, 9, 40);
        Editor.LineHeight = Math.Clamp(Editor.LineHeight, 1.0, 3.0);
        Editor.MaxWidth = Math.Clamp(Editor.MaxWidth, 400, 4000);
        Editor.Zoom = Math.Clamp(Editor.Zoom, 0.5, 2.0);
        if (string.IsNullOrWhiteSpace(Editor.FontFamily)) Editor.FontFamily = EditorSettings.DefaultFontFamily;
        if (string.IsNullOrWhiteSpace(Images.FolderName) || Images.FolderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) Images.FolderName = "assets";
        if (Save.Eol is not ("preserve" or "lf" or "crlf")) Save.Eol = "preserve";
        Save.AutosaveIntervalSec = Math.Clamp(Save.AutosaveIntervalSec, 5, 3600);
        if (Markdown.Emphasis is not ("*" or "_")) Markdown.Emphasis = "*";
        if (Markdown.Bullet is not ("-" or "*" or "+")) Markdown.Bullet = "-";
        if (Markdown.ListIndent is not (2 or 4)) Markdown.ListIndent = 2;
        Ui.Window.W = Math.Max(Ui.Window.W, 400);
        Ui.Window.H = Math.Max(Ui.Window.H, 300);
    }
}

public sealed class EditorSettings
{
    public const string DefaultFontFamily = "Segoe UI Variable";
    public string FontFamily { get; set; } = DefaultFontFamily;
    public double FontSize { get; set; } = 15;
    public double LineHeight { get; set; } = 1.7;
    public double MaxWidth { get; set; } = 800;
    public double Zoom { get; set; } = 1.0;
}

public sealed class ImageSettings
{
    public string FolderName { get; set; } = "assets";
    public bool AllowRemote { get; set; } = true;
}

public sealed class SaveSettings
{
    /// <summary>preserve | lf | crlf</summary>
    public string Eol { get; set; } = "preserve";
    public bool EnsureTrailingNewline { get; set; } = true;
    public bool Autosave { get; set; }
    public int AutosaveIntervalSec { get; set; } = 30;
}

public sealed class MarkdownSettings
{
    public string Emphasis { get; set; } = "*";
    public string Bullet { get; set; } = "-";
    public int ListIndent { get; set; } = 2;
    public bool ExtHighlight { get; set; }
    public bool ExtMath { get; set; } = true;
    public bool ExtMermaid { get; set; } = true;
    public bool FrontMatter { get; set; } = true;
}

public sealed class UiSettings
{
    public bool ShowToolbar { get; set; } = true;
    public bool ShowOutline { get; set; }
    public bool ShowFileSidebar { get; set; }
    /// <summary>T-54: the one-time "set as default app" hint was shown (packaged installs only).</summary>
    public bool DefaultAppPromptShown { get; set; }
    public WindowPlacement Window { get; set; } = new();
}

public sealed class WindowPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; } = 1200;
    public int H { get; set; } = 800;
    public bool Maximized { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
public sealed partial class SettingsJsonContext : JsonSerializerContext;

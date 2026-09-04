using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarkPad.App.Bridge;

// DTOs for the C# ↔ JS protocol (ARCHITECTURE.md §4). Keep in sync with MarkPad.Editor.Web/src/bridge-types.ts.

/// <summary>Wire envelope: t = req | res | evt.</summary>
public sealed record BridgeEnvelope
{
    [JsonPropertyName("t")] public string T { get; init; } = "";
    [JsonPropertyName("id")] public int? Id { get; init; }
    [JsonPropertyName("m")] public string? M { get; init; }
    [JsonPropertyName("p")] public JsonElement? P { get; init; }
    [JsonPropertyName("ok")] public bool? Ok { get; init; }
    [JsonPropertyName("r")] public JsonElement? R { get; init; }
    [JsonPropertyName("e")] public BridgeError? E { get; init; }
}

public sealed record BridgeError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("msg")] string Msg);

// ---- settings ----
public sealed record EditorThemeDto(
    string Mode,
    string FontFamily,
    double FontSize,
    double LineHeight,
    double MaxWidth,
    double Zoom,
    string? Accent = null);

public sealed record MarkdownStyleDto(
    string Emphasis = "*",
    string Bullet = "-",
    int ListIndent = 2,
    bool ExtHighlight = false,
    bool ExtMath = true,
    bool ExtMermaid = true,
    bool FrontMatter = true);

public sealed record EditorSettingsDto(EditorThemeDto Theme, MarkdownStyleDto Markdown, bool AllowRemoteImages = true);

// ---- host → web ----
public sealed record DocLoadParams(string Text, string? Path, bool Readonly, EditorSettingsDto Settings);
public sealed record DocSerializeParams(string Original);
public sealed record DocSerializeResult(string Text, int[] ChangedBlocks);
public sealed record DocGetMarkdownResult(string Text);
public sealed record DocIsDirtyResult(bool Dirty);
public sealed record FormatToggleParams(string Mark);
public sealed record FormatHeadingParams(int Level);
public sealed record FormatListParams(string Type);
public sealed record InsertCodeBlockParams(string? Lang);
public sealed record InsertTableParams(int Rows, int Cols);
public sealed record InsertLinkParams(string Href, string? Text);
public sealed record InsertImageParams(string Src, string? Alt);
public sealed record InsertTextParams(string Text);
public sealed record InsertMathParams(bool Display);
public sealed record SetReadonlyParams(bool Value);
public sealed record FindParams(string Query, bool CaseSensitive, bool WholeWord);
public sealed record FindReplaceParams(string Replacement);
public sealed record FindResult(int Count, int Index);
public sealed record ExportRenderHtmlParams(bool InlineImages, string Theme);
public sealed record ExportRenderHtmlResult(string Html, string Css, bool HasMath);
public sealed record ExportPreparePrintParams(string Theme, string PageSize);
public sealed record AssetReadParams(string Src);
public sealed record AssetReadResult(string? DataUrl);
public sealed record RewriteAssetPathsParams(Dictionary<string, string> Map);
public sealed record RewriteAssetPathsResult(int Count);

// ---- web → host ----
public sealed record ReadyEvent(string Version);
public sealed record ChangedEvent(bool Dirty, int Words, int Chars, int Line);
public sealed record SelectionContext(
    bool Bold,
    bool Italic,
    bool Strike,
    bool Code,
    bool Highlight,
    int Heading,
    string? List,
    bool Blockquote,
    bool InTable,
    bool InCode,
    string? CodeLang,
    string? Link,
    bool HasSelection = false)
{
    public static SelectionContext Empty { get; } = new(false, false, false, false, false, 0, null, false, false, false, null, null);
}
public sealed record ShortcutEvent(string Key);
public sealed record LogEvent(string Level, string Msg);
public sealed record AssetSaveParams(string BytesBase64, string Mime, string? SuggestedName);
public sealed record AssetSaveResult(string RelPath);
public sealed record LinkOpenParams(string Href);
public sealed record LinkOpenResult(bool Ok);
public sealed record ImageResolveParams(string Src);
public sealed record ImageResolveResult(string Url);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(BridgeEnvelope))]
[JsonSerializable(typeof(BridgeError))]
[JsonSerializable(typeof(EditorThemeDto))]
[JsonSerializable(typeof(MarkdownStyleDto))]
[JsonSerializable(typeof(EditorSettingsDto))]
[JsonSerializable(typeof(DocLoadParams))]
[JsonSerializable(typeof(DocSerializeParams))]
[JsonSerializable(typeof(DocSerializeResult))]
[JsonSerializable(typeof(DocGetMarkdownResult))]
[JsonSerializable(typeof(DocIsDirtyResult))]
[JsonSerializable(typeof(FormatToggleParams))]
[JsonSerializable(typeof(FormatHeadingParams))]
[JsonSerializable(typeof(FormatListParams))]
[JsonSerializable(typeof(InsertCodeBlockParams))]
[JsonSerializable(typeof(InsertTableParams))]
[JsonSerializable(typeof(InsertLinkParams))]
[JsonSerializable(typeof(InsertImageParams))]
[JsonSerializable(typeof(InsertTextParams))]
[JsonSerializable(typeof(SetReadonlyParams))]
[JsonSerializable(typeof(InsertMathParams))]
[JsonSerializable(typeof(FindParams))]
[JsonSerializable(typeof(FindReplaceParams))]
[JsonSerializable(typeof(FindResult))]
[JsonSerializable(typeof(ExportRenderHtmlParams))]
[JsonSerializable(typeof(ExportRenderHtmlResult))]
[JsonSerializable(typeof(ExportPreparePrintParams))]
[JsonSerializable(typeof(AssetReadParams))]
[JsonSerializable(typeof(AssetReadResult))]
[JsonSerializable(typeof(RewriteAssetPathsParams))]
[JsonSerializable(typeof(RewriteAssetPathsResult))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(ReadyEvent))]
[JsonSerializable(typeof(ChangedEvent))]
[JsonSerializable(typeof(SelectionContext))]
[JsonSerializable(typeof(ShortcutEvent))]
[JsonSerializable(typeof(LogEvent))]
[JsonSerializable(typeof(AssetSaveParams))]
[JsonSerializable(typeof(AssetSaveResult))]
[JsonSerializable(typeof(LinkOpenParams))]
[JsonSerializable(typeof(LinkOpenResult))]
[JsonSerializable(typeof(ImageResolveParams))]
[JsonSerializable(typeof(ImageResolveResult))]
[JsonSerializable(typeof(JsonElement))]
public sealed partial class BridgeJsonContext : JsonSerializerContext;

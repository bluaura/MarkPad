using System.Text;

namespace MarkPad.Core.Export;

public sealed record HtmlExportOptions(string Title, bool DarkTheme, bool HasMath, string Language = "ko");

/// <summary>
/// Assembles the single-file HTML export (PRD F-EXP-01): body HTML and CSS come from the editor bundle
/// (`export.renderHtml`); this adds the document skeleton. KaTeX needs its own CSS + fonts, which cannot be
/// inlined sensibly, so math documents link the KaTeX stylesheet from a CDN.
/// </summary>
public static class HtmlExportBuilder
{
    public const string KatexCssUrl = "https://cdn.jsdelivr.net/npm/katex@0.16.22/dist/katex.min.css";

    public static string Build(string bodyHtml, string css, HtmlExportOptions options)
    {
        var sb = new StringBuilder(bodyHtml.Length + css.Length + 1024);
        sb.Append("<!doctype html>\n<html lang=\"").Append(options.Language).Append("\">\n<head>\n");
        sb.Append("<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("<meta name=\"generator\" content=\"MarkPad\">\n");
        sb.Append("<title>").Append(Escape(options.Title)).Append("</title>\n");
        if (options.HasMath)
        {
            sb.Append("<link rel=\"stylesheet\" href=\"").Append(KatexCssUrl).Append("\">\n");
        }
        sb.Append("<style>\n").Append(css).Append("\n</style>\n</head>\n");
        sb.Append("<body class=\"").Append(options.DarkTheme ? "markpad-dark" : "markpad-light").Append("\">\n");
        sb.Append("<article class=\"markpad-body\">\n").Append(bodyHtml).Append("\n</article>\n</body>\n</html>\n");
        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

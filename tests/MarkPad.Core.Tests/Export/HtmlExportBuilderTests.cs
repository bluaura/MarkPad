using MarkPad.Core.Export;

namespace MarkPad.Core.Tests.Export;

public class HtmlExportBuilderTests
{
    [Fact]
    public void Build_ProducesSingleFileDocument()
    {
        var html = HtmlExportBuilder.Build("<h1>Hi</h1>", "body{color:red}", new HtmlExportOptions("A <b>", DarkTheme: false, HasMath: false));
        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("<title>A &lt;b&gt;</title>", html);
        Assert.Contains("<style>\nbody{color:red}", html);
        Assert.Contains("class=\"markpad-light\"", html);
        Assert.Contains("<h1>Hi</h1>", html);
        Assert.DoesNotContain("katex", html);
    }

    [Fact]
    public void Build_LinksKatexOnlyWhenMathPresent()
    {
        var html = HtmlExportBuilder.Build("x", "", new HtmlExportOptions("t", DarkTheme: true, HasMath: true));
        Assert.Contains(HtmlExportBuilder.KatexCssUrl, html);
        Assert.Contains("markpad-dark", html);
    }
}

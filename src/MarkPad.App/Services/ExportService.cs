using MarkPad.App.Bridge;
using MarkPad.App.Editing;
using MarkPad.Core.Documents;
using MarkPad.Core.Export;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace MarkPad.App.Services;

public enum ExportFormat
{
    Html,
    Pdf,
}

public sealed record ExportOptions(
    ExportFormat Format,
    bool InlineImages = true,
    bool DarkTheme = false,
    string PageSize = "A4",
    double MarginMm = 20);

/// <summary>HTML (F-EXP-01) and PDF (F-EXP-02) export plus printing (F-EXP-03) on top of a web surface.</summary>
public sealed class ExportService
{
    private readonly ILogger<ExportService> _log;

    public ExportService(ILogger<ExportService> log)
    {
        _log = log;
    }

    public async Task ExportHtmlAsync(WebEditorSurface surface, string title, string targetPath, ExportOptions options, CancellationToken ct = default)
    {
        var r = await surface.Host.Bridge.CallAsync<ExportRenderHtmlResult>(
            "export.renderHtml",
            new ExportRenderHtmlParams(options.InlineImages, options.DarkTheme ? "dark" : "light"),
            TimeSpan.FromSeconds(60),
            ct) ?? throw new BridgeException("EXPORT", "renderHtml returned nothing");
        var html = HtmlExportBuilder.Build(r.Html, r.Css, new HtmlExportOptions(title, options.DarkTheme, r.HasMath));
        await AtomicWriter.WriteAsync(targetPath, System.Text.Encoding.UTF8.GetBytes(html), ct);
        _log.LogInformation("exported HTML {Path} ({Bytes} bytes)", targetPath, html.Length);
    }

    public async Task ExportPdfAsync(WebEditorSurface surface, string targetPath, ExportOptions options, CancellationToken ct = default)
    {
        var core = surface.Host.CoreWebView2 ?? throw new BridgeException(BridgeException.NotReady, "WebView2 not initialized");
        var bridge = surface.Host.Bridge;
        await bridge.CallAsync("export.preparePrint", new ExportPreparePrintParams(options.DarkTheme ? "dark" : "light", options.PageSize), ct: ct);
        try
        {
            await Task.Delay(150, ct); // let layout settle after the print CSS lands
            var settings = core.Environment.CreatePrintSettings();
            var (w, h) = options.PageSize == "Letter" ? (8.5, 11.0) : (8.27, 11.69);
            var marginIn = options.MarginMm / 25.4;
            settings.PageWidth = w;
            settings.PageHeight = h;
            settings.MarginTop = marginIn;
            settings.MarginBottom = marginIn;
            settings.MarginLeft = marginIn;
            settings.MarginRight = marginIn;
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            settings.ScaleFactor = 1.0;

            var tmp = AtomicWriter.TempPathFor(targetPath);
            var ok = await core.PrintToPdfAsync(tmp, settings);
            if (!ok) throw new IOException("PrintToPdfAsync failed");
            if (File.Exists(targetPath)) File.Replace(tmp, targetPath, null, ignoreMetadataErrors: true);
            else File.Move(tmp, targetPath);
            _log.LogInformation("exported PDF {Path}", targetPath);
        }
        finally
        {
            try { await bridge.CallAsync("export.restore", ct: ct); }
            catch (BridgeException ex) { _log.LogWarning(ex, "export.restore failed"); }
        }
    }

    /// <summary>PRD F-EXP-03: system print dialog over the same print CSS.</summary>
    public async Task PrintAsync(WebEditorSurface surface, CancellationToken ct = default)
    {
        var core = surface.Host.CoreWebView2 ?? throw new BridgeException(BridgeException.NotReady, "WebView2 not initialized");
        var bridge = surface.Host.Bridge;
        await bridge.CallAsync("export.preparePrint", new ExportPreparePrintParams("light", "A4"), ct: ct);
        try
        {
            await Task.Delay(150, ct);
            core.ShowPrintUI(CoreWebView2PrintDialogKind.System);
            // The system dialog is modal to the WebView; give it a moment before restoring the screen styles.
            await Task.Delay(1500, ct);
        }
        finally
        {
            try { await bridge.CallAsync("export.restore", ct: ct); }
            catch (BridgeException ex) { _log.LogWarning(ex, "export.restore failed"); }
        }
    }
}

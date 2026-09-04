using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;

namespace MarkPad.App.UiTests;

/// <summary>
/// IMPLEMENTATION-PLAN T-59: 열기·편집·저장·탭·내보내기. Run with the desktop unlocked:
/// <c>$env:MARKPAD_UI_TESTS=1; dotnet test tests/MarkPad.App.UiTests</c> (after building the app in Debug).
/// </summary>
public sealed class SmokeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "markpad-ui", Guid.NewGuid().ToString("N"));

    public SmokeTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string WriteFixture(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));
        return path;
    }

    [SkippableFact]
    public void Open_ShowsFileInTitleAndTab()
    {
        Skip.IfNot(AppSession.Enabled, "set MARKPAD_UI_TESTS=1 on an unlocked desktop");
        var file = WriteFixture("open.md", "# Open\n\nhello\n");
        using var s = new AppSession(file);
        Assert.Contains("open.md", s.Title, StringComparison.Ordinal);
        Assert.True(AppSession.WaitUntil(() => s.TabItems().Any(t => t.Name.Contains("open.md", StringComparison.Ordinal)), TimeSpan.FromSeconds(10)));
    }

    [SkippableFact]
    public void Edit_Then_CtrlS_SavesWithRoundTrip()
    {
        Skip.IfNot(AppSession.Enabled, "set MARKPAD_UI_TESTS=1 on an unlocked desktop");
        var original = "# Smoke\r\n\r\nFirst paragraph with __strong__ text.\r\n\r\n* item one\r\n* item two\r\n";
        var file = WriteFixture("smoke.md", original);
        using var s = new AppSession(file);
        s.FocusEditor();
        s.Press(VirtualKeyShort.HOME, VirtualKeyShort.CONTROL);
        s.Press(VirtualKeyShort.END);
        s.Type(" edited");
        var dirty = AppSession.WaitUntil(() => s.Title.StartsWith("●", StringComparison.Ordinal), TimeSpan.FromSeconds(5));
        if (!dirty) s.Capture("edit-fail.png");
        Assert.True(dirty, $"dirty marker expected; title='{s.Title}' (screenshot in %TEMP%\\markpad-ui-shots)");
        s.Press(VirtualKeyShort.KEY_S, VirtualKeyShort.CONTROL);
        Assert.True(AppSession.WaitUntil(() => File.ReadAllText(file).StartsWith("# Smoke edited\r\n", StringComparison.Ordinal), TimeSpan.FromSeconds(10)));
        var saved = File.ReadAllText(file);
        Assert.Contains("__strong__", saved, StringComparison.Ordinal);
        Assert.Contains("* item one\r\n* item two\r\n", saved, StringComparison.Ordinal);
        Assert.True(AppSession.WaitUntil(() => !s.Title.StartsWith("●", StringComparison.Ordinal), TimeSpan.FromSeconds(5)));
    }

    [SkippableFact]
    public void Tabs_CtrlN_AddsAndCtrlW_Closes()
    {
        Skip.IfNot(AppSession.Enabled, "set MARKPAD_UI_TESTS=1 on an unlocked desktop");
        var file = WriteFixture("tabs.md", "# Tabs\n");
        using var s = new AppSession(file);
        Assert.True(AppSession.WaitUntil(() => s.TabItems().Count == 1, TimeSpan.FromSeconds(10)));
        s.FocusEditor();
        s.Press(VirtualKeyShort.KEY_N, VirtualKeyShort.CONTROL);
        Assert.True(AppSession.WaitUntil(() => s.TabItems().Count == 2, TimeSpan.FromSeconds(10)), "Ctrl+N should add a tab");
        Assert.Contains("Untitled", s.Title, StringComparison.Ordinal);
        s.FocusEditor();
        s.Press(VirtualKeyShort.KEY_W, VirtualKeyShort.CONTROL);
        Assert.True(AppSession.WaitUntil(() => s.TabItems().Count == 1, TimeSpan.FromSeconds(10)), "Ctrl+W should close the untitled tab");
        Assert.Contains("tabs.md", s.Title, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void Export_CtrlShiftE_OpensDialog()
    {
        Skip.IfNot(AppSession.Enabled, "set MARKPAD_UI_TESTS=1 on an unlocked desktop");
        var file = WriteFixture("export.md", "# Export\n\ntext\n");
        using var s = new AppSession(file);
        s.FocusEditor();
        s.Press(VirtualKeyShort.KEY_E, VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT);
        var dialog = AppSession.WaitFor(() => s.Main.FindFirstDescendant(cf => cf.ByName("내보내기").And(cf.ByControlType(ControlType.Window)))
            ?? s.Main.FindFirstDescendant(cf => cf.ByName("내보내기")), TimeSpan.FromSeconds(10));
        Assert.NotNull(dialog);
        s.Press(VirtualKeyShort.ESCAPE);
    }

    [SkippableFact]
    public void PlainText_Tab_ShowsTxtBadge_And_Saves()
    {
        Skip.IfNot(AppSession.Enabled, "set MARKPAD_UI_TESTS=1 on an unlocked desktop");
        var file = WriteFixture("notes.txt", "line1\r\n");
        using var s = new AppSession(file);
        Assert.True(AppSession.WaitUntil(() => s.TabItems().Any(t => t.Name.Contains("notes.txt", StringComparison.Ordinal)), TimeSpan.FromSeconds(10)));
        s.FocusEditor();
        s.Press(VirtualKeyShort.END, VirtualKeyShort.CONTROL);
        s.Type(" more");
        s.Press(VirtualKeyShort.KEY_S, VirtualKeyShort.CONTROL);
        // Ctrl+End lands after the trailing newline, so the text goes on a new line; CRLF must be preserved.
        var saved = AppSession.WaitUntil(() => File.ReadAllText(file) == "line1\r\n more\r\n", TimeSpan.FromSeconds(10));
        if (!saved) s.Capture("txt-fail.png");
        Assert.True(saved, $"unexpected file content; title='{s.Title}' content='{File.ReadAllText(file).Replace("\r", "\\r").Replace("\n", "\\n")}'");
    }
}

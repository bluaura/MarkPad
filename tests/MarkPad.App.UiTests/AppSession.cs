using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace MarkPad.App.UiTests;

/// <summary>
/// Launches the unpackaged Debug build (single instance: one app per test) and exposes UIA helpers.
/// UI tests need an interactive, unlocked desktop; they are skipped unless MARKPAD_UI_TESTS=1 (T-59).
/// </summary>
public sealed class AppSession : IDisposable
{
    public static bool Enabled => Environment.GetEnvironmentVariable("MARKPAD_UI_TESTS") == "1";

    public static string ExePath
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("MARKPAD_EXE");
            if (!string.IsNullOrEmpty(env)) return env;
            var root = FindRepoRoot();
            return Path.Combine(root, "src", "MarkPad.App", "bin", "x64", "Debug", "net10.0-windows10.0.19041.0", "win-x64", "MarkPad.exe");
        }
    }

    public Application App { get; }
    public UIA3Automation Automation { get; }
    public Window Main { get; }

    public AppSession(params string[] args)
    {
        foreach (var p in Process.GetProcessesByName("MarkPad"))
        {
            try { p.Kill(); p.WaitForExit(3000); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        var psi = new ProcessStartInfo(ExePath, string.Join(' ', args.Select(a => $"\"{a}\""))) { UseShellExecute = false };
        App = Application.Launch(psi);
        Automation = new UIA3Automation();
        Main = WaitFor(() =>
        {
            var w = App.GetMainWindow(Automation, TimeSpan.FromSeconds(2));
            return w is not null && w.Title.Contains("MarkPad", StringComparison.Ordinal) ? w : null;
        }, TimeSpan.FromSeconds(20)) ?? throw new TimeoutException("main window not found");
        Thread.Sleep(2500); // WebView2 + editor bundle
        BringToFront();
    }

    public void BringToFront()
    {
        // A background process may only take the foreground while a key is down (same trick as build/run-dev.ps1).
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        SetForegroundWindow(Main.Properties.NativeWindowHandle.Value);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
        Thread.Sleep(300);
        if (GetForegroundWindow() != Main.Properties.NativeWindowHandle.Value)
        {
            throw new InvalidOperationException("MarkPad is not the foreground window; refusing to send input elsewhere.");
        }
    }

    /// <summary>
    /// Click into the document body so keyboard input reaches the editor. Side panels (outline/sidebar) may be
    /// open from persisted settings, so the WebView2 render widget (or the TextBox for .txt) is located via UIA
    /// instead of assuming the window centre.
    /// </summary>
    public void FocusEditor()
    {
        var target = WaitFor(() =>
            Main.FindFirstDescendant(cf => cf.ByClassName("Chrome_RenderWidgetHostHWND"))
            ?? Main.FindFirstDescendant(cf => cf.ByControlType(ControlType.Document))
            ?? Main.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit).And(cf.ByName("텍스트 편집기"))), TimeSpan.FromSeconds(10));
        var r = target?.BoundingRectangle ?? Main.BoundingRectangle;
        var x = target is null ? (int)(r.Right - 200) : (int)(r.Left + r.Width / 2);
        var y = target is null ? (int)(r.Top + 320) : (int)Math.Min(r.Top + 200, r.Top + r.Height / 2);
        Mouse.Click(new System.Drawing.Point(x, y));
        Thread.Sleep(400);
    }

    public void Type(string text)
    {
        Keyboard.Type(text);
        Thread.Sleep(200);
    }

    public void Press(VirtualKeyShort key, params VirtualKeyShort[] modifiers)
    {
        foreach (var m in modifiers) Keyboard.Press(m);
        Keyboard.Press(key);
        Keyboard.Release(key);
        foreach (var m in modifiers.Reverse()) Keyboard.Release(m);
        Thread.Sleep(400);
    }

    public IReadOnlyList<AutomationElement> TabItems()
        => Main.FindAllDescendants(cf => cf.ByControlType(ControlType.TabItem)).ToList();

    public string Title => Main.Title;

    /// <summary>Saves a screenshot of the main window (kept in the scratch folder; the test folder is deleted).</summary>
    public void Capture(string path)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "markpad-ui-shots");
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, Path.GetFileName(path));
            Main.Capture().Save(target, System.Drawing.Imaging.ImageFormat.Png);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or IOException)
        {
        }
    }

    public static T? WaitFor<T>(Func<T?> probe, TimeSpan timeout) where T : class
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            try
            {
                var v = probe();
                if (v is not null) return v;
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or TimeoutException)
            {
            }
            Thread.Sleep(250);
        }
        return null;
    }

    public static bool WaitUntil(Func<bool> probe, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            try
            {
                if (probe()) return true;
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or TimeoutException)
            {
            }
            Thread.Sleep(250);
        }
        return false;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MarkPad.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("MarkPad.sln not found above " + AppContext.BaseDirectory);
    }

    public void Dispose()
    {
        try { App.Close(); } catch (Exception ex) when (ex is InvalidOperationException or COMException) { }
        if (!App.HasExited) App.Kill();
        Automation.Dispose();
        App.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr h);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}

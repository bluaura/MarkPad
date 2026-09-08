using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace MarkPad.App.Services;

/// <summary>
/// Single-instance activation (ARCHITECTURE.md §2.2 / ADR-10). A second launch (Explorer double-click,
/// command line) redirects its arguments to the main instance, which opens them as tabs.
/// Command line, file association (packaged) and drag & drop all end in <see cref="FilesActivated"/>.
/// </summary>
public static class ActivationService
{
    public const string InstanceKey = "MarkPad.Main";
    private static readonly string[] s_openableExtensions = [".md", ".markdown", ".txt"];

    /// <summary>Raised on every later activation; the list is empty when no openable file came with it.</summary>
    public static event Action<IReadOnlyList<string>>? Activated;

    /// <summary>Call first thing in Main. Returns true when this process redirected and must exit.</summary>
    public static bool TryRedirectToMainInstance()
    {
        var current = AppInstance.GetCurrent();
        var args = current.GetActivatedEventArgs();
        var main = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (main.IsCurrent)
        {
            return false;
        }
        RedirectActivationTo(args, main);
        return true;
    }

    /// <summary>Subscribe to later activations; callbacks are marshalled to <paramref name="dispatcher"/>.</summary>
    public static void Start(DispatcherQueue dispatcher)
    {
        AppInstance.GetCurrent().Activated += (_, e) =>
        {
            var files = ExtractFiles(e);
            dispatcher.TryEnqueue(() => Activated?.Invoke(files));
        };
    }

    /// <summary>Files from this process's own activation (command line or file association).</summary>
    public static IReadOnlyList<string> GetInitialFiles()
    {
        var files = ExtractFiles(AppInstance.GetCurrent().GetActivatedEventArgs());
        if (files.Count > 0) return files;
        return FilterOpenable(Environment.GetCommandLineArgs().Skip(1));
    }

    public static IReadOnlyList<string> ExtractFiles(AppActivationArguments args)
    {
        try
        {
            switch (args.Kind)
            {
                case ExtendedActivationKind.File when args.Data is IFileActivatedEventArgs fa:
                    return FilterOpenable(fa.Files.Select(f => f.Path));
                case ExtendedActivationKind.Launch when args.Data is ILaunchActivatedEventArgs la:
                    return FilterOpenable(ParseCommandLine(la.Arguments));
                default:
                    return [];
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return [];
        }
    }

    public static bool IsOpenable(string path)
    {
        var ext = Path.GetExtension(path);
        return s_openableExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> FilterOpenable(IEnumerable<string> candidates)
    {
        var result = new List<string>();
        foreach (var c in candidates)
        {
            var p = c.Trim();
            if (p.Length == 0 || p.StartsWith('-')) continue;
            if (!IsOpenable(p) || !File.Exists(p)) continue;
            result.Add(Path.GetFullPath(p));
        }
        return result;
    }

    /// <summary>Splits a Win32 command line (quotes aware). The first token is the executable when present.</summary>
    public static IReadOnlyList<string> ParseCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return [];
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        foreach (var ch in commandLine)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
            }
            else
            {
                current.Append(ch);
            }
        }
        if (current.Length > 0) result.Add(current.ToString());
        if (result.Count > 0 && result[0].EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) result.RemoveAt(0);
        return result;
    }

    // Redirect must not block the STA thread on the async operation (see Windows App SDK sample).
    private static void RedirectActivationTo(AppActivationArguments args, AppInstance target)
    {
        // Hand our foreground right over to the main instance, otherwise its Activate() is ignored
        // (Windows only lets the foreground process, or one it named, raise a window).
        AllowSetForegroundWindow(target.ProcessId);
        var handle = CreateEvent(IntPtr.Zero, true, false, null);
        _ = Task.Run(() =>
        {
            try
            {
                target.RedirectActivationToAsync(args).AsTask().Wait();
            }
            finally
            {
                SetEvent(handle);
            }
        });
        _ = CoWaitForMultipleObjects(0, 0xFFFFFFFF, 1, [handle], out _);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetEvent(IntPtr hEvent);

    [DllImport("ole32.dll")]
    private static extern uint CoWaitForMultipleObjects(uint dwFlags, uint dwMilliseconds, uint nHandles, IntPtr[] pHandles, out uint dwIndex);
}

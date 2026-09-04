using Serilog;

namespace MarkPad.App.Services;

/// <summary>Serilog file sink at %LOCALAPPDATA%\MarkPad\logs (ARCHITECTURE.md §1). Document content is never logged (§7.4).</summary>
internal static class LoggingSetup
{
    public static Serilog.ILogger CreateLogger()
    {
        var dir = Path.Combine(App.LocalDataDir, "logs");
        Directory.CreateDirectory(dir);
        return new LoggerConfiguration()
#if DEBUG
            .MinimumLevel.Debug()
#else
            .MinimumLevel.Information()
#endif
            .WriteTo.File(
                Path.Combine(dir, "markpad-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}

using System.Text.Json;
using MarkPad.Core.Documents;

namespace MarkPad.Core.Settings;

/// <summary>Loads/saves <c>%LOCALAPPDATA%\MarkPad\settings.json</c>; tolerant of missing or broken files (PRD F-SET-05).</summary>
public sealed class SettingsStore : IDisposable
{
    private readonly string _path;
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private DateTime _lastOwnWriteUtc;

    public SettingsStore(string path)
    {
        _path = path;
    }

    public string Path => _path;

    public AppSettings Current { get; private set; } = new();

    public event EventHandler? Changed;

    /// <summary>Raised on a thread-pool thread when settings.json was edited outside the app (PRD F-SET-05).</summary>
    public event EventHandler? ExternalChange;

    /// <summary>Watch settings.json so manual edits apply without a restart (T-50 DoD).</summary>
    public void StartWatching()
    {
        if (_watcher is not null) return;
        var dir = System.IO.Path.GetDirectoryName(_path);
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        _debounce = new Timer(_ => OnExternalChange(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(dir, System.IO.Path.GetFileName(_path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        _watcher.Changed += (_, _) => _debounce.Change(TimeSpan.FromMilliseconds(400), Timeout.InfiniteTimeSpan);
        _watcher.Created += (_, _) => _debounce.Change(TimeSpan.FromMilliseconds(400), Timeout.InfiniteTimeSpan);
        _watcher.Renamed += (_, _) => _debounce.Change(TimeSpan.FromMilliseconds(400), Timeout.InfiniteTimeSpan);
        _watcher.EnableRaisingEvents = true;
    }

    private void OnExternalChange()
    {
        try
        {
            if (!File.Exists(_path)) return;
            if ((File.GetLastWriteTimeUtc(_path) - _lastOwnWriteUtc).Duration() < TimeSpan.FromMilliseconds(200)) return;
            Load();
            ExternalChange?.Invoke(this, EventArgs.Empty);
        }
        catch (IOException)
        {
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
        _watcher = null;
        _debounce = null;
    }

    public AppSettings Load()
    {
        AppSettings settings;
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
            }
            else
            {
                settings = new AppSettings();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            settings = new AppSettings();
        }
        settings.Validate();
        Current = settings;
        return settings;
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        Current.Validate();
        var json = JsonSerializer.Serialize(Current, SettingsJsonContext.Default.AppSettings);
        await AtomicWriter.WriteAsync(_path, System.Text.Encoding.UTF8.GetBytes(json), ct).ConfigureAwait(false);
        _lastOwnWriteUtc = File.GetLastWriteTimeUtc(_path);
    }

    /// <summary>Apply a mutation, persist, and notify listeners.</summary>
    public async Task UpdateAsync(Action<AppSettings> mutate, CancellationToken ct = default)
    {
        mutate(Current);
        await SaveAsync(ct).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public SaveOptions ToSaveOptions() => new(
        Current.Save.Eol switch { "lf" => EolPolicy.Lf, "crlf" => EolPolicy.CrLf, _ => EolPolicy.Preserve },
        Current.Save.EnsureTrailingNewline);
}

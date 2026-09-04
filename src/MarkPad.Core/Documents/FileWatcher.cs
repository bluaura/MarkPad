namespace MarkPad.Core.Documents;

public enum ExternalChangeKind
{
    Changed,
    Deleted,
    Renamed,
}

public sealed record ExternalChange(ExternalChangeKind Kind, string Path, DateTimeOffset? LastWriteTime);

/// <summary>
/// Watches one file for outside modifications (PRD F-FILE-08, ARCHITECTURE §3.1 DocumentIO.Watch):
/// FileSystemWatcher on the folder filtered to the file name, 500ms debounce, and our own saves are
/// ignored by comparing the write time against <see cref="Document.LastWriteTimeOnLoad"/>.
/// </summary>
public sealed class FileWatcher : IDisposable
{
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);

    private readonly Document _doc;
    private readonly Action<ExternalChange> _onChange;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounce;
    private ExternalChange? _pending;
    private bool _disposed;

    public FileWatcher(Document doc, Action<ExternalChange> onChange)
    {
        if (doc.Path is null) throw new ArgumentException("document has no path", nameof(doc));
        _doc = doc;
        _onChange = onChange;
        var dir = Path.GetDirectoryName(doc.Path)!;
        _watcher = new FileSystemWatcher(dir, Path.GetFileName(doc.Path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            IncludeSubdirectories = false,
        };
        _watcher.Changed += (_, e) => Queue(ExternalChangeKind.Changed, e.FullPath);
        _watcher.Created += (_, e) => Queue(ExternalChangeKind.Changed, e.FullPath);
        _watcher.Deleted += (_, e) => Queue(ExternalChangeKind.Deleted, e.FullPath);
        // An atomic replace shows up as Renamed(tmp → our path): that is a content change, not a rename away.
        _watcher.Renamed += (_, e) => Queue(
            string.Equals(e.FullPath, doc.Path, StringComparison.OrdinalIgnoreCase) ? ExternalChangeKind.Changed : ExternalChangeKind.Renamed,
            e.FullPath);
        _debounce = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher.EnableRaisingEvents = true;
    }

    private void Queue(ExternalChangeKind kind, string path)
    {
        if (_disposed) return;
        DateTimeOffset? write = null;
        if (kind != ExternalChangeKind.Deleted && File.Exists(path))
        {
            write = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        }
        _pending = new ExternalChange(kind, path, write);
        _debounce.Change(Debounce, Timeout.InfiniteTimeSpan);
    }

    private void Fire()
    {
        var change = _pending;
        _pending = null;
        if (change is null || _disposed) return;
        if (change.Kind == ExternalChangeKind.Changed)
        {
            // Re-read after the debounce: an atomic replace raises events before the final file is in place.
            if (!File.Exists(change.Path)) return;
            change = change with { LastWriteTime = new DateTimeOffset(File.GetLastWriteTimeUtc(change.Path), TimeSpan.Zero) };
            if (IsOurOwnWrite(change)) return;
        }
        _onChange(change);
    }

    /// <summary>Our AtomicWriter updates <see cref="Document.LastWriteTimeOnLoad"/> right after writing.</summary>
    private bool IsOurOwnWrite(ExternalChange change)
    {
        if (change.LastWriteTime is null || _doc.LastWriteTimeOnLoad is null) return false;
        var delta = (change.LastWriteTime.Value - _doc.LastWriteTimeOnLoad.Value).Duration();
        return delta < TimeSpan.FromMilliseconds(50);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _debounce.Dispose();
    }
}

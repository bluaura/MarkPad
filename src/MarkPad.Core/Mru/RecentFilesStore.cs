using System.Text.Json;
using System.Text.Json.Serialization;
using MarkPad.Core.Documents;

namespace MarkPad.Core.Mru;

public sealed record RecentFile(string Path, DateTimeOffset LastOpened)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
}

/// <summary>Most-recently-used list, max 20, persisted as JSON (PRD F-FILE-04). Missing files are pruned lazily.</summary>
public sealed class RecentFilesStore
{
    public const int MaxEntries = 20;
    private readonly string _path;
    private readonly List<RecentFile> _items = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RecentFilesStore(string path)
    {
        _path = path;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<RecentFile> Items => _items;

    public void Load()
    {
        _items.Clear();
        try
        {
            if (!File.Exists(_path)) return;
            var list = JsonSerializer.Deserialize(File.ReadAllText(_path), MruJsonContext.Default.ListRecentFile);
            if (list is null) return;
            _items.AddRange(list
                .Where(x => !string.IsNullOrWhiteSpace(x.Path))
                .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.LastOpened).First())
                .OrderByDescending(x => x.LastOpened)
                .Take(MaxEntries));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _items.Clear();
        }
    }

    public async Task AddAsync(string path, CancellationToken ct = default)
    {
        var full = Path.GetFullPath(path);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _items.RemoveAll(x => string.Equals(x.Path, full, StringComparison.OrdinalIgnoreCase));
            _items.Insert(0, new RecentFile(full, DateTimeOffset.Now));
            if (_items.Count > MaxEntries) _items.RemoveRange(MaxEntries, _items.Count - MaxEntries);
            await PersistAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task RemoveAsync(string path, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_items.RemoveAll(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase)) == 0) return;
            await PersistAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Drops entries whose file no longer exists. Returns the removed paths.</summary>
    public async Task<IReadOnlyList<string>> PruneMissingAsync(CancellationToken ct = default)
    {
        var missing = _items.Where(x => !File.Exists(x.Path)).Select(x => x.Path).ToList();
        if (missing.Count == 0) return missing;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _items.RemoveAll(x => missing.Contains(x.Path, StringComparer.OrdinalIgnoreCase));
            await PersistAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return missing;
    }

    private Task PersistAsync(CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(_items, MruJsonContext.Default.ListRecentFile);
        return AtomicWriter.WriteAsync(_path, System.Text.Encoding.UTF8.GetBytes(json), ct);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<RecentFile>))]
internal sealed partial class MruJsonContext : JsonSerializerContext;

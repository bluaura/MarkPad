using System.Text.Json;
using System.Text.Json.Serialization;
using MarkPad.Core.Documents;

namespace MarkPad.Core.Recovery;

public sealed record RecoverySnapshot(
    string Id,
    string? Path,
    string Title,
    DocumentKind Kind,
    DateTimeOffset SavedAt)
{
    [JsonIgnore]
    public string TextFile => $"{Id}.md";
}

/// <summary>
/// Crash-recovery snapshots (PRD §5.5, ARCHITECTURE §7.2): <c>%LOCALAPPDATA%\MarkPad\recovery\{id}.md + .json</c>
/// per dirty tab, written every few seconds by the view model and deleted on save/close/normal exit.
/// Whatever remains at startup belongs to a crashed session.
/// </summary>
public sealed class RecoveryStore
{
    private readonly string _dir;

    public RecoveryStore(string dir)
    {
        _dir = dir;
    }

    public string Directory => _dir;

    public async Task SaveAsync(RecoverySnapshot meta, string textLf, CancellationToken ct = default)
    {
        System.IO.Directory.CreateDirectory(_dir);
        await AtomicWriter.WriteAsync(System.IO.Path.Combine(_dir, meta.TextFile), System.Text.Encoding.UTF8.GetBytes(textLf), ct).ConfigureAwait(false);
        var json = JsonSerializer.Serialize(meta, RecoveryJsonContext.Default.RecoverySnapshot);
        await AtomicWriter.WriteAsync(System.IO.Path.Combine(_dir, $"{meta.Id}.json"), System.Text.Encoding.UTF8.GetBytes(json), ct).ConfigureAwait(false);
    }

    /// <summary>Synchronous best-effort write for the unhandled-exception path.</summary>
    public void SaveBlocking(RecoverySnapshot meta, string textLf)
    {
        try
        {
            System.IO.Directory.CreateDirectory(_dir);
            File.WriteAllText(System.IO.Path.Combine(_dir, meta.TextFile), textLf, new System.Text.UTF8Encoding(false));
            File.WriteAllText(System.IO.Path.Combine(_dir, $"{meta.Id}.json"), JsonSerializer.Serialize(meta, RecoveryJsonContext.Default.RecoverySnapshot));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Delete(string id)
    {
        foreach (var f in new[] { $"{id}.md", $"{id}.json" })
        {
            try
            {
                var p = System.IO.Path.Combine(_dir, f);
                if (File.Exists(p)) File.Delete(p);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public IReadOnlyList<RecoverySnapshot> List()
    {
        if (!System.IO.Directory.Exists(_dir)) return [];
        var result = new List<RecoverySnapshot>();
        foreach (var json in System.IO.Directory.GetFiles(_dir, "*.json"))
        {
            try
            {
                var meta = JsonSerializer.Deserialize(File.ReadAllText(json), RecoveryJsonContext.Default.RecoverySnapshot);
                if (meta is not null && File.Exists(System.IO.Path.Combine(_dir, meta.TextFile))) result.Add(meta);
            }
            catch (JsonException) { }
            catch (IOException) { }
        }
        return result.OrderByDescending(s => s.SavedAt).ToList();
    }

    public Task<string> ReadTextAsync(RecoverySnapshot meta, CancellationToken ct = default)
        => File.ReadAllTextAsync(System.IO.Path.Combine(_dir, meta.TextFile), ct);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(RecoverySnapshot))]
internal sealed partial class RecoveryJsonContext : JsonSerializerContext;

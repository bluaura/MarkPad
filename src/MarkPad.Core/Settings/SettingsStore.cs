using System.Text.Json;
using MarkPad.Core.Documents;

namespace MarkPad.Core.Settings;

/// <summary>Loads/saves <c>%LOCALAPPDATA%\MarkPad\settings.json</c>; tolerant of missing or broken files (PRD F-SET-05).</summary>
public sealed class SettingsStore
{
    private readonly string _path;

    public SettingsStore(string path)
    {
        _path = path;
    }

    public string Path => _path;

    public AppSettings Current { get; private set; } = new();

    public event EventHandler? Changed;

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

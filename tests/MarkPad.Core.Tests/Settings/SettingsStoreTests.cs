using MarkPad.Core.Documents;
using MarkPad.Core.Settings;

namespace MarkPad.Core.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "markpad-tests", Guid.NewGuid().ToString("N"));

    public SettingsStoreTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        var s = store.Load();
        Assert.Equal("system", s.Theme);
        Assert.Equal("assets", s.Images.FolderName);
        Assert.Equal(15, s.Editor.FontSize);
        Assert.True(s.Markdown.FrontMatter);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsAndKeepsUnknownKeys()
    {
        var path = Path.Combine(_dir, "settings.json");
        await File.WriteAllTextAsync(path, """{ "theme": "dark", "futureKey": { "x": 1 }, "editor": { "fontSize": 18 } }""");
        var store = new SettingsStore(path);
        store.Load();
        Assert.Equal("dark", store.Current.Theme);
        Assert.Equal(18, store.Current.Editor.FontSize);

        await store.UpdateAsync(s => s.Save.Eol = "crlf");
        var json = await File.ReadAllTextAsync(path);
        Assert.Contains("\"futureKey\"", json);
        Assert.Contains("\"crlf\"", json);

        var again = new SettingsStore(path);
        again.Load();
        Assert.Equal(EolPolicy.CrLf, again.ToSaveOptions().Eol);
    }

    [Fact]
    public void Load_CorruptFile_FallsBackToDefaults()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ not json");
        var s = new SettingsStore(path).Load();
        Assert.Equal("system", s.Theme);
    }

    [Fact]
    public void Validate_ClampsOutOfRangeValues()
    {
        var s = new AppSettings { Theme = "neon" };
        s.Editor.FontSize = 200;
        s.Markdown.Bullet = "•";
        s.Save.Eol = "mac";
        s.Validate();
        Assert.Equal("system", s.Theme);
        Assert.Equal(40, s.Editor.FontSize);
        Assert.Equal("-", s.Markdown.Bullet);
        Assert.Equal("preserve", s.Save.Eol);
    }
}

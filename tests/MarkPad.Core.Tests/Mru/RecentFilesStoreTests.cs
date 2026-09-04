using MarkPad.Core.Mru;

namespace MarkPad.Core.Tests.Mru;

public sealed class RecentFilesStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "markpad-tests", Guid.NewGuid().ToString("N"));

    public RecentFilesStoreTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Add_MovesToFront_DedupesCaseInsensitively_AndCapsAt20()
    {
        var store = new RecentFilesStore(Path.Combine(_dir, "recent.json"));
        for (var i = 0; i < 25; i++) await store.AddAsync(Path.Combine(_dir, $"f{i}.md"));
        Assert.Equal(RecentFilesStore.MaxEntries, store.Items.Count);
        Assert.EndsWith("f24.md", store.Items[0].Path);

        await store.AddAsync(Path.Combine(_dir, "F10.MD"));
        Assert.Equal(RecentFilesStore.MaxEntries, store.Items.Count);
        Assert.EndsWith("F10.MD", store.Items[0].Path);
        Assert.Single(store.Items, x => x.Path.EndsWith("10.md", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Persists_AndPrunesMissingFiles()
    {
        var jsonPath = Path.Combine(_dir, "recent.json");
        var existing = Path.Combine(_dir, "exists.md");
        await File.WriteAllTextAsync(existing, "x");
        var store = new RecentFilesStore(jsonPath);
        await store.AddAsync(existing);
        await store.AddAsync(Path.Combine(_dir, "gone.md"));

        var reloaded = new RecentFilesStore(jsonPath);
        reloaded.Load();
        Assert.Equal(2, reloaded.Items.Count);
        var removed = await reloaded.PruneMissingAsync();
        Assert.Single(removed);
        Assert.Single(reloaded.Items);
        Assert.Equal(existing, reloaded.Items[0].Path);
    }
}

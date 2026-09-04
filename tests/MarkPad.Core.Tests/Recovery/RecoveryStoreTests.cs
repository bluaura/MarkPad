using MarkPad.Core.Documents;
using MarkPad.Core.Recovery;

namespace MarkPad.Core.Tests.Recovery;

public sealed class RecoveryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "markpad-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task SaveListReadDelete_RoundTrip()
    {
        var store = new RecoveryStore(_dir);
        Assert.Empty(store.List());

        var meta = new RecoverySnapshot("1-7", @"C:\docs\a.md", "a.md", DocumentKind.Markdown, DateTimeOffset.Now);
        await store.SaveAsync(meta, "# draft\n");
        var list = store.List();
        Assert.Single(list);
        Assert.Equal(meta.Id, list[0].Id);
        Assert.Equal(meta.Path, list[0].Path);
        Assert.Equal("# draft\n", await store.ReadTextAsync(list[0]));

        store.Delete(meta.Id);
        Assert.Empty(store.List());
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void SaveBlocking_WritesBothFiles()
    {
        var store = new RecoveryStore(_dir);
        store.SaveBlocking(new RecoverySnapshot("x", null, "Untitled", DocumentKind.PlainText, DateTimeOffset.Now), "text");
        Assert.Single(store.List());
        Assert.Equal(DocumentKind.PlainText, store.List()[0].Kind);
    }
}

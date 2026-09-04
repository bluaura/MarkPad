using MarkPad.Core.Assets;
using MarkPad.Core.Documents;

namespace MarkPad.Core.Tests.Assets;

public sealed class AssetServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "markpad-tests", Guid.NewGuid().ToString("N"));
    private readonly AssetService _assets;

    public AssetServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _assets = new AssetService(Path.Combine(_dir, "pending"), () => "assets");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Naming_FollowsDocTimestampRule()
    {
        var ts = new DateTime(2026, 9, 4, 14, 30, 5);
        Assert.Equal("notes-20260904-143005.png", AssetNaming.BuildFileName("notes", ts, ".png"));
        Assert.Equal("notes-20260904-143005-2.png", AssetNaming.BuildFileName("notes", ts, ".png", 2));
        Assert.Equal("image", AssetNaming.BaseNameFor(null));
        Assert.Equal("my doc", AssetNaming.BaseNameFor(@"C:\x\my doc.md"));
        Assert.Equal(".jpg", AssetNaming.ExtensionForMime("image/jpeg"));
        Assert.Equal(".png", AssetNaming.ExtensionForMime("application/octet-stream"));
    }

    [Fact]
    public async Task SaveImage_SavedDocument_WritesNextToDocumentWithRelativeLink()
    {
        var docPath = Path.Combine(_dir, "notes.md");
        await File.WriteAllTextAsync(docPath, "# x");
        var doc = DocumentIO.FromBytes("# x"u8, docPath);

        var rel = await _assets.SaveImageAsync(doc, new byte[] { 1, 2, 3 }, "image/png");
        Assert.StartsWith("assets/notes-", rel);
        Assert.EndsWith(".png", rel);
        Assert.DoesNotContain('\\', rel);
        Assert.True(File.Exists(Path.Combine(_dir, rel.Replace('/', Path.DirectorySeparatorChar))));

        var rel2 = await _assets.SaveImageAsync(doc, new byte[] { 4 }, "image/png");
        Assert.NotEqual(rel, rel2); // same second → -1 suffix
    }

    [Fact]
    public async Task UnsavedDocument_UsesPendingFolder_ThenRelocatesOnFirstSave()
    {
        var doc = DocumentIO.CreateNew();
        var rel = await _assets.SaveImageAsync(doc, new byte[] { 9 }, "image/png");
        Assert.StartsWith("assets/image-", rel);
        var pendingFile = Path.Combine(_assets.DisplayRootFor(doc), rel.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(pendingFile));

        // Simulate a collision at the destination.
        var targetDir = Path.Combine(_dir, "docs");
        Directory.CreateDirectory(Path.Combine(targetDir, "assets"));
        var fileName = Path.GetFileName(pendingFile);
        await File.WriteAllBytesAsync(Path.Combine(targetDir, "assets", fileName), new byte[] { 0 });

        var map = await _assets.RelocatePendingAsync(doc, Path.Combine(targetDir, "new.md"));
        Assert.Single(map);
        Assert.Equal(rel, map.Keys.Single());
        var renamed = map.Values.Single();
        Assert.True(File.Exists(Path.Combine(targetDir, renamed.Replace('/', Path.DirectorySeparatorChar))));
        Assert.False(Directory.Exists(_assets.PendingRootFor(doc)));
    }

    [Fact]
    public async Task Relocate_NoCollision_ReturnsEmptyMap_AndFilesMoved()
    {
        var doc = DocumentIO.CreateNew();
        var rel = await _assets.SaveImageAsync(doc, new byte[] { 9 }, "image/png");
        var target = Path.Combine(_dir, "out", "n.md");
        var map = await _assets.RelocatePendingAsync(doc, target);
        Assert.Empty(map);
        Assert.True(File.Exists(Path.Combine(_dir, "out", rel.Replace('/', Path.DirectorySeparatorChar))));
    }
}

using System.Text;
using MarkPad.Core.Documents;

namespace MarkPad.Core.Tests.Documents;

public sealed class AtomicWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "markpad-tests", Guid.NewGuid().ToString("N"));

    public AtomicWriterTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task CreatesNewFile_AndLeavesNoTemp()
    {
        var target = Path.Combine(_dir, "new.md");
        await AtomicWriter.WriteAsync(target, Encoding.UTF8.GetBytes("hello"));
        Assert.Equal("hello", await File.ReadAllTextAsync(target));
        Assert.False(File.Exists(AtomicWriter.TempPathFor(target)));
    }

    [Fact]
    public async Task ReplacesExistingFile_AndLeavesNoTemp()
    {
        var target = Path.Combine(_dir, "existing.md");
        await File.WriteAllTextAsync(target, "old");
        await AtomicWriter.WriteAsync(target, Encoding.UTF8.GetBytes("new"));
        Assert.Equal("new", await File.ReadAllTextAsync(target));
        Assert.False(File.Exists(AtomicWriter.TempPathFor(target)));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task CreatesMissingDirectories()
    {
        var target = Path.Combine(_dir, "sub", "deeper", "x.md");
        await AtomicWriter.WriteAsync(target, Encoding.UTF8.GetBytes("x"));
        Assert.True(File.Exists(target));
    }

    [Fact]
    public async Task WhenTargetLocked_OriginalIsUntouchedAndTempRemoved()
    {
        var target = Path.Combine(_dir, "locked.md");
        await File.WriteAllTextAsync(target, "original");
        using (File.Open(target, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => AtomicWriter.WriteAsync(target, Encoding.UTF8.GetBytes("new")));
        }
        Assert.Equal("original", await File.ReadAllTextAsync(target));
        Assert.False(File.Exists(AtomicWriter.TempPathFor(target)));
    }
}

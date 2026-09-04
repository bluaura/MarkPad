using System.Text;
using MarkPad.Core.Documents;

namespace MarkPad.Core.Tests.Documents;

public sealed class FileWatcherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "markpad-tests", Guid.NewGuid().ToString("N"));

    public FileWatcherTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task ExternalWrite_RaisesChanged_ButOwnSaveDoesNot()
    {
        var io = new DocumentIO();
        var path = Path.Combine(_dir, "w.md");
        await File.WriteAllTextAsync(path, "# a\n");
        var doc = await io.OpenAsync(path);

        var changes = new List<ExternalChange>();
        var signal = new SemaphoreSlim(0);
        using var watcher = new FileWatcher(doc, c => { changes.Add(c); signal.Release(); });

        // Our own save: LastWriteTimeOnLoad is updated by SaveAsync → filtered out.
        await io.SaveAsync(doc, "# a\n\nsaved by us\n");
        var ownFired = await signal.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(ownFired, "own save must not be reported");

        // External editor writes → reported.
        await Task.Delay(100);
        await File.WriteAllTextAsync(path, "# a\n\nchanged outside\n", Encoding.UTF8);
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Contains(changes, c => c.Kind == ExternalChangeKind.Changed);
    }

    [Fact]
    public async Task Delete_IsReported()
    {
        var path = Path.Combine(_dir, "d.md");
        await File.WriteAllTextAsync(path, "x");
        var doc = await new DocumentIO().OpenAsync(path);
        var signal = new SemaphoreSlim(0);
        ExternalChange? seen = null;
        using var watcher = new FileWatcher(doc, c => { seen = c; signal.Release(); });
        File.Delete(path);
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(ExternalChangeKind.Deleted, seen!.Kind);
    }
}

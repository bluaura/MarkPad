using MarkPad.Core.Documents;

namespace MarkPad.Core.Assets;

/// <summary>
/// Image storage rules (ARCHITECTURE.md §3.1 AssetService, PRD F-IMG-01/04). Saved documents keep images in
/// <c>{doc folder}/{folderName}/</c>; unsaved documents use a pending folder whose layout mirrors it, so the
/// relative link (<c>assets/x.png</c>) stays valid before and after the first save.
/// </summary>
public sealed class AssetService
{
    private readonly string _pendingRoot;
    private readonly Func<string> _folderName;

    public AssetService(string pendingRoot, Func<string> folderName)
    {
        _pendingRoot = pendingRoot;
        _folderName = folderName;
    }

    public string FolderName => _folderName();

    /// <summary>Folder that <c>doc.markpad</c> should map to: the document's folder, or its pending folder.</summary>
    public string DisplayRootFor(Document doc) => doc.Directory ?? PendingRootFor(doc);

    public string AssetsDirFor(Document doc) => Path.Combine(DisplayRootFor(doc), FolderName);

    public string PendingRootFor(Document doc) => Path.Combine(_pendingRoot, $"{Environment.ProcessId}-{doc.Id}");

    /// <summary>Writes the image and returns the markdown-relative path (<c>assets/x.png</c>, forward slashes).</summary>
    public async Task<string> SaveImageAsync(Document doc, ReadOnlyMemory<byte> bytes, string? mime, string? suggestedName = null, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(suggestedName);
        if (!AssetNaming.IsImageExtension(ext)) ext = AssetNaming.ExtensionForMime(mime);
        var dir = AssetsDirFor(doc);
        Directory.CreateDirectory(dir);
        var name = AssetNaming.UniqueFileName(dir, AssetNaming.BaseNameFor(doc.Path), DateTime.Now, ext!.ToLowerInvariant());
        await AtomicWriter.WriteAsync(Path.Combine(dir, name), bytes, ct).ConfigureAwait(false);
        return $"{FolderName}/{name}";
    }

    /// <summary>Copies an existing image file into the document's asset folder (toolbar "복사" option, F-IMG-02).</summary>
    public async Task<string> CopyImageAsync(Document doc, string sourcePath, CancellationToken ct = default)
    {
        var bytes = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
        return await SaveImageAsync(doc, bytes, null, Path.GetFileName(sourcePath), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// First save of a previously unsaved document: move pending assets next to <paramref name="newDocumentPath"/>.
    /// Returns old→new relative paths for files that had to be renamed (collisions); unchanged files are not listed.
    /// </summary>
    public Task<IReadOnlyDictionary<string, string>> RelocatePendingAsync(Document doc, string newDocumentPath, CancellationToken ct = default)
    {
        var pendingAssets = Path.Combine(PendingRootFor(doc), FolderName);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(pendingAssets)) return Task.FromResult<IReadOnlyDictionary<string, string>>(result);

        var targetDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(newDocumentPath))!, FolderName);
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(pendingAssets))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(file);
            var targetName = AssetNaming.UniqueExistingName(targetDir, name);
            File.Move(file, Path.Combine(targetDir, targetName));
            if (!string.Equals(name, targetName, StringComparison.Ordinal))
            {
                result[$"{FolderName}/{name}"] = $"{FolderName}/{targetName}";
            }
        }
        TryDeleteDirectory(PendingRootFor(doc));
        return Task.FromResult<IReadOnlyDictionary<string, string>>(result);
    }

    /// <summary>Drops the pending folder of a document that was closed without saving.</summary>
    public void DiscardPending(Document doc) => TryDeleteDirectory(PendingRootFor(doc));

    /// <summary>Removes pending folders left behind by crashed sessions (older than <paramref name="maxAge"/>).</summary>
    public void CleanupStale(TimeSpan maxAge)
    {
        if (!Directory.Exists(_pendingRoot)) return;
        var cutoff = DateTime.UtcNow - maxAge;
        foreach (var dir in Directory.GetDirectories(_pendingRoot))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(dir) < cutoff) Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

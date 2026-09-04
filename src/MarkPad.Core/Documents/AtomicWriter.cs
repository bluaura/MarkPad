namespace MarkPad.Core.Documents;

/// <summary>
/// Write-to-temp then atomic replace so a crash mid-save never corrupts the target (PRD §5.5).
/// The temp file lives next to the target (<c>.{name}.markpad-tmp</c>) so <see cref="File.Replace"/> stays on one volume.
/// </summary>
public static class AtomicWriter
{
    public const string TempSuffix = ".markpad-tmp";

    public static string TempPathFor(string targetPath)
    {
        var dir = Path.GetDirectoryName(targetPath) ?? string.Empty;
        var name = Path.GetFileName(targetPath);
        return Path.Combine(dir, $".{name}{TempSuffix}");
    }

    public static async Task WriteAsync(string targetPath, ReadOnlyMemory<byte> bytes, CancellationToken ct = default)
    {
        var fullPath = Path.GetFullPath(targetPath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = TempPathFor(fullPath);
        try
        {
            await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1 << 16, FileOptions.WriteThrough))
            {
                await fs.WriteAsync(bytes, ct).ConfigureAwait(false);
                fs.Flush(flushToDisk: true);
            }

            if (File.Exists(fullPath))
            {
                File.Replace(tmp, fullPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tmp, fullPath);
            }
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // best effort
        }
        catch (UnauthorizedAccessException)
        {
            // best effort
        }
    }
}

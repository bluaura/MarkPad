namespace MarkPad.Core.Assets;

/// <summary>File-name rules for pasted/inserted images (PRD F-IMG-01): <c>{doc}-{yyyyMMdd-HHmmss}[-{n}].{ext}</c>.</summary>
public static class AssetNaming
{
    private static readonly HashSet<string> s_imageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg", ".avif", ".ico", ".tif", ".tiff",
    };

    public static bool IsImageExtension(string? ext) => ext is not null && s_imageExtensions.Contains(ext);

    public static string ExtensionForMime(string? mime) => mime?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/bmp" => ".bmp",
        "image/svg+xml" => ".svg",
        "image/avif" => ".avif",
        _ => ".png",
    };

    /// <summary>Base name: document file name without extension, sanitized; "image" for unsaved documents.</summary>
    public static string BaseNameFor(string? documentPath)
    {
        if (string.IsNullOrEmpty(documentPath)) return "image";
        var name = Path.GetFileNameWithoutExtension(documentPath);
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim();
        return name.Length == 0 ? "image" : name;
    }

    public static string BuildFileName(string baseName, DateTime timestamp, string ext, int collisionIndex = 0)
    {
        var stamp = timestamp.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var suffix = collisionIndex > 0 ? $"-{collisionIndex}" : string.Empty;
        return $"{baseName}-{stamp}{suffix}{ext}";
    }

    /// <summary>First name in <paramref name="directory"/> that does not exist yet.</summary>
    public static string UniqueFileName(string directory, string baseName, DateTime timestamp, string ext)
    {
        for (var n = 0; n < 10_000; n++)
        {
            var candidate = BuildFileName(baseName, timestamp, ext, n);
            if (!File.Exists(Path.Combine(directory, candidate))) return candidate;
        }
        throw new IOException("Could not find a free asset file name.");
    }

    /// <summary>Unique target for an existing file name (relocation): keeps the name, appends -n on collision.</summary>
    public static string UniqueExistingName(string directory, string fileName)
    {
        if (!File.Exists(Path.Combine(directory, fileName))) return fileName;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var n = 1; n < 10_000; n++)
        {
            var candidate = $"{stem}-{n}{ext}";
            if (!File.Exists(Path.Combine(directory, candidate))) return candidate;
        }
        throw new IOException("Could not find a free asset file name.");
    }
}

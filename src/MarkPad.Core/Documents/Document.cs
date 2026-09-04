using System.Text;

namespace MarkPad.Core.Documents;

public enum DocumentKind
{
    Markdown,
    PlainText,
}

public enum LineEnding
{
    Lf,
    CrLf,
}

/// <summary>
/// In-memory state of one open file (ARCHITECTURE.md §3.1). Text is always held LF-normalized;
/// <see cref="Eol"/>, <see cref="HasBom"/> and <see cref="EndsWithNewline"/> describe how it is written back.
/// </summary>
public sealed class Document
{
    private static int s_nextId;

    public Document()
    {
        Id = Interlocked.Increment(ref s_nextId);
    }

    /// <summary>Process-unique id used for pending assets and recovery snapshots.</summary>
    public int Id { get; }

    /// <summary><c>null</c> for a new, never-saved document.</summary>
    public string? Path { get; internal set; }

    public DocumentKind Kind { get; init; } = DocumentKind.Markdown;

    /// <summary>Text at the last load/save (LF normalized). Baseline for round-trip preservation.</summary>
    public string OriginalText { get; internal set; } = string.Empty;

    public Encoding Encoding { get; internal set; } = EncodingDetector.Utf8NoBom;

    public bool HasBom { get; internal set; }

    public LineEnding Eol { get; internal set; } = LineEnding.Lf;

    public bool EndsWithNewline { get; internal set; }

    /// <summary>The file mixed CRLF and LF; it is normalized to <see cref="Eol"/> on save (first newline wins).</summary>
    public bool HasMixedEol { get; internal set; }

    /// <summary>True when the file was not valid UTF-8 (PRD F-FILE-06): editing requires an explicit conversion.</summary>
    public bool IsReadOnly { get; internal set; }

    public bool IsDirty { get; set; }

    /// <summary>Last write time observed when loading/saving; used to filter our own writes in the file watcher.</summary>
    public DateTimeOffset? LastWriteTimeOnLoad { get; internal set; }

    public string FileName => Path is null ? "Untitled" : System.IO.Path.GetFileName(Path);

    public string? Directory => Path is null ? null : System.IO.Path.GetDirectoryName(Path);

    public static DocumentKind KindFromPath(string? path)
    {
        if (path is null) return DocumentKind.Markdown;
        return System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".txt" => DocumentKind.PlainText,
            _ => DocumentKind.Markdown,
        };
    }

    /// <summary>Marks the non-UTF-8 document as convertible: it will be written as UTF-8 on the next save.</summary>
    public void ConvertToUtf8()
    {
        Encoding = EncodingDetector.Utf8NoBom;
        HasBom = false;
        IsReadOnly = false;
        IsDirty = true;
    }
}

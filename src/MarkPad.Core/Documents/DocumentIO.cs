using System.Text;

namespace MarkPad.Core.Documents;

public enum EolPolicy
{
    Preserve,
    Lf,
    CrLf,
}

/// <summary>Save-time rules from settings.save (ARCHITECTURE.md §3.1 Settings).</summary>
public sealed record SaveOptions(EolPolicy Eol = EolPolicy.Preserve, bool EnsureTrailingNewline = true)
{
    public static SaveOptions Default { get; } = new();
}

/// <summary>Open / save with encoding, EOL and BOM preservation (ARCHITECTURE.md §3.1 DocumentIO).</summary>
public sealed class DocumentIO
{
    public async Task<Document> OpenAsync(string path, CancellationToken ct = default)
    {
        var fullPath = Path.GetFullPath(path);
        var bytes = await File.ReadAllBytesAsync(fullPath, ct).ConfigureAwait(false);
        var lastWrite = new DateTimeOffset(File.GetLastWriteTimeUtc(fullPath), TimeSpan.Zero);
        return FromBytes(bytes, fullPath, lastWrite);
    }

    /// <summary>Builds a document from raw bytes; used by <see cref="OpenAsync"/> and tests.</summary>
    public static Document FromBytes(ReadOnlySpan<byte> bytes, string? path, DateTimeOffset? lastWrite = null)
    {
        var enc = EncodingDetector.Detect(bytes);
        var raw = EncodingDetector.Decode(bytes, enc);
        var eol = EolDetector.Detect(raw);

        return new Document
        {
            Path = path,
            Kind = Document.KindFromPath(path),
            OriginalText = EolDetector.NormalizeToLf(raw),
            Encoding = enc.Encoding,
            HasBom = enc.HasBom,
            Eol = eol.Eol,
            EndsWithNewline = eol.EndsWithNewline,
            HasMixedEol = eol.IsMixed,
            IsReadOnly = enc.RequiresConversion,
            IsDirty = false,
            LastWriteTimeOnLoad = lastWrite,
        };
    }

    /// <summary>New unsaved document. <paramref name="initialText"/> (e.g. dropped file contents) makes it dirty from the start.</summary>
    public static Document CreateNew(DocumentKind kind = DocumentKind.Markdown, LineEnding eol = LineEnding.Lf, string? initialText = null)
    {
        return new Document
        {
            Path = null,
            Kind = kind,
            OriginalText = initialText is null ? string.Empty : EolDetector.NormalizeToLf(initialText),
            Encoding = EncodingDetector.Utf8NoBom,
            HasBom = false,
            Eol = eol,
            EndsWithNewline = true,
            IsDirty = initialText is not null,
        };
    }

    /// <summary>
    /// Produces the exact bytes that <see cref="SaveAsync"/> would write. Pure, so it can be unit-tested.
    /// Returns the LF-normalized text that becomes the new <see cref="Document.OriginalText"/>.
    /// </summary>
    public static (byte[] Bytes, string TextLf) Encode(Document doc, string textLf, SaveOptions options)
    {
        if (doc.IsReadOnly)
        {
            throw new InvalidOperationException("Document is read-only (non-UTF-8). Call ConvertToUtf8() first.");
        }

        var text = EolDetector.NormalizeToLf(textLf);
        if (options.EnsureTrailingNewline && text.Length > 0 && !text.EndsWith('\n'))
        {
            text += "\n";
        }

        var eol = options.Eol switch
        {
            EolPolicy.Lf => LineEnding.Lf,
            EolPolicy.CrLf => LineEnding.CrLf,
            _ => doc.Eol,
        };

        var body = EolDetector.FromLf(text, eol);
        var encoding = doc.Encoding;
        var payload = encoding.GetBytes(body);

        if (!doc.HasBom) return (payload, text);

        var bom = encoding.GetPreamble();
        if (bom.Length == 0 && Equals(encoding.CodePage, Encoding.UTF8.CodePage)) bom = Encoding.UTF8.GetPreamble();
        var bytes = new byte[bom.Length + payload.Length];
        bom.CopyTo(bytes, 0);
        payload.CopyTo(bytes, bom.Length);
        return (bytes, text);
    }

    public async Task SaveAsync(Document doc, string textLf, string? newPath = null, SaveOptions? options = null, CancellationToken ct = default)
    {
        var target = newPath ?? doc.Path ?? throw new InvalidOperationException("No path for document.");
        var fullPath = Path.GetFullPath(target);
        var opts = options ?? SaveOptions.Default;

        var (bytes, normalized) = Encode(doc, textLf, opts);
        await AtomicWriter.WriteAsync(fullPath, bytes, ct).ConfigureAwait(false);

        doc.Path = fullPath;
        doc.OriginalText = normalized;
        doc.EndsWithNewline = normalized.EndsWith('\n');
        doc.Eol = opts.Eol switch
        {
            EolPolicy.Lf => LineEnding.Lf,
            EolPolicy.CrLf => LineEnding.CrLf,
            _ => doc.Eol,
        };
        doc.IsDirty = false;
        doc.LastWriteTimeOnLoad = new DateTimeOffset(File.GetLastWriteTimeUtc(fullPath), TimeSpan.Zero);
    }
}

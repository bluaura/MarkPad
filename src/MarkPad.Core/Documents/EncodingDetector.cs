using System.Text;

namespace MarkPad.Core.Documents;

public sealed record EncodingDetection(Encoding Encoding, bool HasBom, int BomLength, bool IsUtf8)
{
    /// <summary>Non-UTF-8 files open read-only until converted (PRD F-FILE-06).</summary>
    public bool RequiresConversion => !IsUtf8;
}

/// <summary>
/// BOM → strict UTF-8 → CP949 → Latin-1 fallback (ARCHITECTURE.md §3.1 DocumentIO).
/// </summary>
public static class EncodingDetector
{
    public static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
    private static readonly Encoding s_utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Lazy<Encoding?> s_cp949 = new(() =>
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    });

    public static EncodingDetection Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return new EncodingDetection(Utf8NoBom, HasBom: true, BomLength: 3, IsUtf8: true);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return new EncodingDetection(Encoding.Unicode, HasBom: true, BomLength: 2, IsUtf8: false);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return new EncodingDetection(Encoding.BigEndianUnicode, HasBom: true, BomLength: 2, IsUtf8: false);
        }

        if (IsValidUtf8(bytes))
        {
            return new EncodingDetection(Utf8NoBom, HasBom: false, BomLength: 0, IsUtf8: true);
        }

        var cp949 = s_cp949.Value;
        if (cp949 is not null && TryDecode(cp949, bytes))
        {
            return new EncodingDetection(cp949, HasBom: false, BomLength: 0, IsUtf8: false);
        }

        return new EncodingDetection(Encoding.Latin1, HasBom: false, BomLength: 0, IsUtf8: false);
    }

    /// <summary>Decodes with the detected encoding, skipping the BOM so the text never contains U+FEFF.</summary>
    public static string Decode(ReadOnlySpan<byte> bytes, EncodingDetection detection)
    {
        var payload = bytes[detection.BomLength..];
        if (detection.IsUtf8)
        {
            return Utf8NoBom.GetString(payload);
        }
        // Non-UTF-8 encodings were validated during detection; decode leniently here.
        var lenient = detection.Encoding.CodePage == 949 && s_cp949.Value is not null
            ? Encoding.GetEncoding(949)
            : detection.Encoding;
        return lenient.GetString(payload);
    }

    private static bool IsValidUtf8(ReadOnlySpan<byte> bytes)
    {
        try
        {
            _ = s_utf8Strict.GetCharCount(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool TryDecode(Encoding encoding, ReadOnlySpan<byte> bytes)
    {
        try
        {
            _ = encoding.GetCharCount(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}

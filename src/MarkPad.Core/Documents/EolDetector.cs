namespace MarkPad.Core.Documents;

public sealed record EolDetection(LineEnding Eol, bool EndsWithNewline, bool IsMixed);

/// <summary>Line-ending detection: the first newline wins for mixed files (ARCHITECTURE.md §3.1).</summary>
public static class EolDetector
{
    public static EolDetection Detect(ReadOnlySpan<char> text)
    {
        var first = LineEnding.Lf;
        var sawLf = false;
        var sawCrLf = false;
        var found = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r')
            {
                var isCrLf = i + 1 < text.Length && text[i + 1] == '\n';
                if (isCrLf)
                {
                    sawCrLf = true;
                    i++;
                }
                else
                {
                    // Lone CR: treat like CRLF for detection purposes (classic Mac files are effectively extinct).
                    sawCrLf = true;
                }
                if (!found)
                {
                    first = LineEnding.CrLf;
                    found = true;
                }
            }
            else if (c == '\n')
            {
                sawLf = true;
                if (!found)
                {
                    first = LineEnding.Lf;
                    found = true;
                }
            }
        }

        var endsWithNewline = text.Length > 0 && (text[^1] == '\n' || text[^1] == '\r');
        return new EolDetection(first, endsWithNewline, IsMixed: sawLf && sawCrLf);
    }

    /// <summary>CRLF / lone CR → LF.</summary>
    public static string NormalizeToLf(string text)
    {
        if (text.IndexOf('\r') < 0) return text;
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    /// <summary>LF → requested line ending. Input must be LF-normalized.</summary>
    public static string FromLf(string textLf, LineEnding eol)
    {
        return eol == LineEnding.CrLf
            ? textLf.Replace("\n", "\r\n", StringComparison.Ordinal)
            : textLf;
    }
}

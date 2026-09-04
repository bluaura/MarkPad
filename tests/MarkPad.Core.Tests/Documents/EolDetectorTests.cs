using MarkPad.Core.Documents;

namespace MarkPad.Core.Tests.Documents;

public class EolDetectorTests
{
    [Theory]
    [InlineData("a\nb\n", LineEnding.Lf, true, false)]
    [InlineData("a\r\nb\r\n", LineEnding.CrLf, true, false)]
    [InlineData("a\r\nb\nc", LineEnding.CrLf, false, true)]
    [InlineData("a\nb\r\n", LineEnding.Lf, true, true)]
    [InlineData("no newline", LineEnding.Lf, false, false)]
    [InlineData("", LineEnding.Lf, false, false)]
    public void Detect(string text, LineEnding expected, bool endsWithNewline, bool mixed)
    {
        var d = EolDetector.Detect(text);
        Assert.Equal(expected, d.Eol);
        Assert.Equal(endsWithNewline, d.EndsWithNewline);
        Assert.Equal(mixed, d.IsMixed);
    }

    [Fact]
    public void NormalizeAndRestore_RoundTrip()
    {
        const string crlf = "a\r\nb\r\n";
        var lf = EolDetector.NormalizeToLf(crlf);
        Assert.Equal("a\nb\n", lf);
        Assert.Equal(crlf, EolDetector.FromLf(lf, LineEnding.CrLf));
        Assert.Equal(lf, EolDetector.FromLf(lf, LineEnding.Lf));
    }

    [Fact]
    public void Normalize_HandlesLoneCr()
    {
        Assert.Equal("a\nb", EolDetector.NormalizeToLf("a\rb"));
    }
}

using System.Text;
using MarkPad.Core.Documents;

namespace MarkPad.Core.Tests.Documents;

public class EncodingDetectorTests
{
    [Fact]
    public void Utf8WithoutBom_IsUtf8()
    {
        var bytes = Encoding.UTF8.GetBytes("# 제목\n\n본문 텍스트\n");
        var d = EncodingDetector.Detect(bytes);
        Assert.True(d.IsUtf8);
        Assert.False(d.HasBom);
        Assert.False(d.RequiresConversion);
        Assert.Equal("# 제목\n\n본문 텍스트\n", EncodingDetector.Decode(bytes, d));
    }

    [Fact]
    public void Utf8WithBom_DetectsBomAndStripsIt()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("hello")).ToArray();
        var d = EncodingDetector.Detect(bytes);
        Assert.True(d.IsUtf8);
        Assert.True(d.HasBom);
        Assert.Equal(3, d.BomLength);
        Assert.Equal("hello", EncodingDetector.Decode(bytes, d));
    }

    [Fact]
    public void Cp949_IsDetectedAndReadOnly()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(949).GetBytes("한글 문서입니다.\n");
        var d = EncodingDetector.Detect(bytes);
        Assert.False(d.IsUtf8);
        Assert.True(d.RequiresConversion);
        Assert.Equal(949, d.Encoding.CodePage);
        Assert.Equal("한글 문서입니다.\n", EncodingDetector.Decode(bytes, d));
    }

    [Fact]
    public void Utf16Le_WithBom_IsReadOnly()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("abc")).ToArray();
        var d = EncodingDetector.Detect(bytes);
        Assert.True(d.HasBom);
        Assert.True(d.RequiresConversion);
        Assert.Equal("abc", EncodingDetector.Decode(bytes, d));
    }

    [Fact]
    public void InvalidEverywhere_FallsBackToLatin1()
    {
        // 0x81 is a stray continuation byte in UTF-8 and a CP949 lead byte with an invalid trail (0x20).
        var bytes = new byte[] { 0x41, 0x81, 0x20 };
        var d = EncodingDetector.Detect(bytes);
        Assert.True(d.RequiresConversion);
        Assert.Equal(Encoding.Latin1.CodePage, d.Encoding.CodePage);
    }

    [Fact]
    public void Empty_IsUtf8NoBom()
    {
        var d = EncodingDetector.Detect(ReadOnlySpan<byte>.Empty);
        Assert.True(d.IsUtf8);
        Assert.False(d.HasBom);
    }
}

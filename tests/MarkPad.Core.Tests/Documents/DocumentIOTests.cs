using System.Text;
using MarkPad.Core.Documents;

namespace MarkPad.Core.Tests.Documents;

public sealed class DocumentIOTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "markpad-tests", Guid.NewGuid().ToString("N"));
    private readonly DocumentIO _io = new();

    public DocumentIOTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    public static IEnumerable<object[]> Fixtures()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "fixtures");
        foreach (var f in Directory.GetFiles(root, "*.md").OrderBy(x => x))
        {
            yield return [Path.GetFileName(f)];
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task OpenThenSaveUnchanged_ProducesIdenticalBytes(string name)
    {
        var src = Path.Combine(AppContext.BaseDirectory, "fixtures", name);
        var original = await File.ReadAllBytesAsync(src);
        var doc = await _io.OpenAsync(src);
        if (doc.IsReadOnly || doc.HasMixedEol)
        {
            return; // non-UTF-8: covered by Cp949 tests. Mixed EOL: normalized by design (ARCHITECTURE §3.1)
        }

        var target = Path.Combine(_dir, name);
        await _io.SaveAsync(doc, doc.OriginalText, target, new SaveOptions(EolPolicy.Preserve, EnsureTrailingNewline: false));
        var saved = await File.ReadAllBytesAsync(target);
        Assert.Equal(original, saved);
    }

    [Fact]
    public void Open_NormalizesToLfAndRemembersCrLf()
    {
        var doc = DocumentIO.FromBytes(Encoding.UTF8.GetBytes("a\r\nb\r\n"), @"C:\x\a.md");
        Assert.Equal("a\nb\n", doc.OriginalText);
        Assert.Equal(LineEnding.CrLf, doc.Eol);
        Assert.True(doc.EndsWithNewline);
        Assert.Equal(DocumentKind.Markdown, doc.Kind);
    }

    [Fact]
    public void Open_TxtIsPlainText()
    {
        var doc = DocumentIO.FromBytes(Encoding.UTF8.GetBytes("x"), @"C:\x\notes.TXT");
        Assert.Equal(DocumentKind.PlainText, doc.Kind);
    }

    [Fact]
    public void Encode_RestoresCrLfAndBom()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("a\r\nb\r\n")).ToArray();
        var doc = DocumentIO.FromBytes(bytes, "a.md");
        var (saved, _) = DocumentIO.Encode(doc, "a\nb\nc\n", SaveOptions.Default);
        var expected = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("a\r\nb\r\nc\r\n")).ToArray();
        Assert.Equal(expected, saved);
    }

    [Fact]
    public void Encode_EnsureTrailingNewline_AppendsOnlyWhenMissing()
    {
        var doc = DocumentIO.CreateNew();
        var (a, _) = DocumentIO.Encode(doc, "text", new SaveOptions(EnsureTrailingNewline: true));
        var (b, _) = DocumentIO.Encode(doc, "text\n", new SaveOptions(EnsureTrailingNewline: true));
        var (c, _) = DocumentIO.Encode(doc, "text", new SaveOptions(EnsureTrailingNewline: false));
        Assert.Equal("text\n", Encoding.UTF8.GetString(a));
        Assert.Equal("text\n", Encoding.UTF8.GetString(b));
        Assert.Equal("text", Encoding.UTF8.GetString(c));
    }

    [Fact]
    public void Encode_EolPolicyOverridesOriginal()
    {
        var doc = DocumentIO.FromBytes(Encoding.UTF8.GetBytes("a\r\nb"), "a.md");
        var (lf, _) = DocumentIO.Encode(doc, "a\nb", new SaveOptions(EolPolicy.Lf, EnsureTrailingNewline: false));
        var (crlf, _) = DocumentIO.Encode(doc, "a\nb", new SaveOptions(EolPolicy.CrLf, EnsureTrailingNewline: false));
        Assert.Equal("a\nb", Encoding.UTF8.GetString(lf));
        Assert.Equal("a\r\nb", Encoding.UTF8.GetString(crlf));
    }

    [Fact]
    public async Task Cp949_OpensReadOnly_ConvertsToUtf8OnSave()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var path = Path.Combine(_dir, "cp949.md");
        await File.WriteAllBytesAsync(path, Encoding.GetEncoding(949).GetBytes("한글\r\n"));

        var doc = await _io.OpenAsync(path);
        Assert.True(doc.IsReadOnly);
        Assert.Equal("한글\n", doc.OriginalText);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _io.SaveAsync(doc, doc.OriginalText));

        doc.ConvertToUtf8();
        await _io.SaveAsync(doc, doc.OriginalText);
        var saved = await File.ReadAllBytesAsync(path);
        Assert.Equal(Encoding.UTF8.GetBytes("한글\r\n"), saved);
        Assert.False(doc.IsDirty);
        Assert.False(doc.IsReadOnly);
    }

    [Fact]
    public async Task Save_UpdatesDocumentState()
    {
        var doc = DocumentIO.CreateNew();
        doc.IsDirty = true;
        var path = Path.Combine(_dir, "new.md");
        await _io.SaveAsync(doc, "# Hi", path);
        Assert.Equal(Path.GetFullPath(path), doc.Path);
        Assert.Equal("# Hi\n", doc.OriginalText);
        Assert.False(doc.IsDirty);
        Assert.NotNull(doc.LastWriteTimeOnLoad);
        Assert.True(doc.EndsWithNewline);
    }
}

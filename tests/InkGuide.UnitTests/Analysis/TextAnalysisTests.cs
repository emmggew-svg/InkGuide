using System;
using System.Text;
using InkGuide.Analysis.Inspection;
using Xunit;

namespace InkGuide.UnitTests.Analysis;

public sealed class TextAnalysisTests
{
    [Fact]
    public void EmptyContentIsText()
    {
        TextAnalysis text = ContentInspector.AnalyzeText(Array.Empty<byte>());

        Assert.True(text.IsText);
        Assert.Equal(0, text.LineCount);
        Assert.Equal("none", text.EolStyle);
    }

    [Fact]
    public void PlainAsciiIsUtf8Lf()
    {
        TextAnalysis text = ContentInspector.AnalyzeText(Encoding.UTF8.GetBytes("hello\nworld\n"));

        Assert.True(text.IsText);
        Assert.Equal("utf-8", text.EncodingName);
        Assert.False(text.HasBom);
        Assert.Equal("lf", text.EolStyle);
        Assert.Equal(2, text.LineCount);
    }

    [Fact]
    public void EolStylesAreDetected()
    {
        Assert.Equal("crlf", ContentInspector.AnalyzeText(Encoding.UTF8.GetBytes("a\r\nb")).EolStyle);
        Assert.Equal("cr", ContentInspector.AnalyzeText(Encoding.UTF8.GetBytes("a\rb\rc")).EolStyle);
        Assert.Equal("mixed", ContentInspector.AnalyzeText(Encoding.UTF8.GetBytes("a\nb\r\nc")).EolStyle);
        Assert.Equal("none", ContentInspector.AnalyzeText(Encoding.UTF8.GetBytes("abc")).EolStyle);
        Assert.Equal(3, ContentInspector.AnalyzeText(Encoding.UTF8.GetBytes("a\rb\rc")).LineCount);
        Assert.Equal(1, ContentInspector.AnalyzeText(Encoding.UTF8.GetBytes("abc")).LineCount);
    }

    [Fact]
    public void Utf8BomIsDetected()
    {
        byte[] content = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i' };

        TextAnalysis text = ContentInspector.AnalyzeText(content);

        Assert.True(text.IsText);
        Assert.True(text.HasBom);
        Assert.Equal("utf-8", text.EncodingName);
    }

    [Fact]
    public void Utf16IsText()
    {
        byte[] content = new byte[] { 0xFF, 0xFE, (byte)'h', 0x00, (byte)'i', 0x00 };

        TextAnalysis text = ContentInspector.AnalyzeText(content);

        Assert.True(text.IsText);
        Assert.Equal("utf-16le", text.EncodingName);
        Assert.True(text.HasBom);
        Assert.Equal(1, text.LineCount);
    }

    [Fact]
    public void NullBytesMeanBinary()
    {
        TextAnalysis text = ContentInspector.AnalyzeText(new byte[] { 0x48, 0x00, 0x49 });

        Assert.False(text.IsText);
        Assert.Equal("binary", text.EncodingName);
    }

    [Fact]
    public void ControlHeavyContentIsBinary()
    {
        byte[] content = new byte[100];
        for (int i = 0; i < content.Length; i++)
        {
            content[i] = 0x01;
        }

        Assert.False(ContentInspector.AnalyzeText(content).IsText);
    }

    [Fact]
    public void NonUtf8TextFallsBackToLatin1()
    {
        TextAnalysis text = ContentInspector.AnalyzeText(new byte[] { 0x48, 0xF0 });

        Assert.True(text.IsText);
        Assert.Equal("latin1", text.EncodingName);
    }
}

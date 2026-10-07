using System;
using InkGuide.Analysis.Catalogs;
using InkGuide.Analysis.Identification;
using Xunit;

namespace InkGuide.UnitTests.Analysis;

public sealed class KindIdentifierTests
{
    private const string KindMapJson = """
        {
          "version": 1,
          "map": {
            ".cs": "source-code", ".md": "document-text", ".png": "image-raster",
            ".json": "data-structured", ".docx": "document-rich", ".html": "web-page"
          },
          "mimeFallback": {
            "application/vnd.google-apps.document": "google-doc",
            "application/vnd.google-apps.spreadsheet": "google-sheet"
          }
        }
        """;

    [Theory]
    [InlineData("89504E470D0A1A0A", "image-raster")]
    [InlineData("FFD8FF", "image-raster")]
    [InlineData("474946383961", "image-raster")]
    [InlineData("474946383761", "image-raster")]
    [InlineData("424D", "image-raster")]
    [InlineData("49492A00", "image-raster")]
    [InlineData("4D4D002A", "image-raster")]
    [InlineData("00000100", "image-raster")]
    [InlineData("524946460000000057454250", "image-raster")]
    [InlineData("524946460000000057415645", "audio")]
    [InlineData("524946460000000041564920", "video")]
    [InlineData("25504446", "pdf")]
    [InlineData("504B0304", "archive")]
    [InlineData("504B0506", "archive")]
    [InlineData("504B0708", "archive")]
    [InlineData("1F8B", "archive")]
    [InlineData("377ABCAF271C", "archive")]
    [InlineData("52617221", "archive")]
    [InlineData("53514C69746520666F726D6174203300", "database")]
    [InlineData("494433", "audio")]
    [InlineData("FFFB", "audio")]
    [InlineData("4F676753", "audio")]
    [InlineData("664C6143", "audio")]
    [InlineData("0000000066747970", "video")]
    [InlineData("1A45DFA3", "video")]
    [InlineData("4D5A", "executable")]
    [InlineData("7F454C46", "executable")]
    [InlineData("FEEDFA", "executable")]
    [InlineData("CAFEBABE", "executable")]
    [InlineData("0061736D", "executable")]
    [InlineData("38425053", "design-source")]
    [InlineData("00010000", "font")]
    [InlineData("4F54544F", "font")]
    [InlineData("774F4646", "font")]
    [InlineData("774F4632", "font")]
    [InlineData("4D534346", "installer")]
    public void MagicBytesDecideKind(string hex, string expectedKind)
    {
        KindIdentifier identifier = Identifier();

        KindIdentification identification = identifier.Identify("file.bin", null, Convert.FromHexString(hex));

        Assert.Equal(expectedKind, identification.Kind);
        Assert.Equal(KindIdentificationSource.MagicBytes, identification.Source);
        Assert.Equal(0.9, identification.Confidence);
    }

    [Fact]
    public void MagicWinsOverExtension()
    {
        KindIdentifier identifier = Identifier();
        byte[] png = Convert.FromHexString("89504E470D0A1A0A");

        KindIdentification identification = identifier.Identify("note.md", null, png);

        Assert.Equal("image-raster", identification.Kind);
        Assert.Equal(KindIdentificationSource.MagicBytes, identification.Source);
    }

    [Fact]
    public void ZipMagicRefinesToZipFamilyKinds()
    {
        KindIdentifier identifier = Identifier();
        byte[] zip = Convert.FromHexString("504B0304");

        Assert.Equal("document-rich", identifier.Identify("report.docx", null, zip).Kind);
        Assert.Equal(0.85, identifier.Identify("report.docx", null, zip).Confidence);
        Assert.Equal("archive", identifier.Identify("backup.zip", null, zip).Kind);
    }

    [Fact]
    public void ExtensionMapDecidesWithoutContent()
    {
        KindIdentifier identifier = Identifier();

        Assert.Equal("source-code", identifier.Identify("Program.cs", null, null).Kind);
        Assert.Equal(KindIdentificationSource.Extension, identifier.Identify("Program.cs", null, null).Source);
        Assert.Equal("document-rich", identifier.Identify("report.docx", null, null).Kind);
    }

    [Fact]
    public void FolderMimeMapsToFolder()
    {
        KindIdentifier identifier = Identifier();

        KindIdentification identification = identifier.Identify(
            "docs", "application/vnd.google-apps.folder", null);

        Assert.Equal("folder", identification.Kind);
        Assert.Equal(1.0, identification.Confidence);
        Assert.Equal(KindIdentificationSource.Folder, identification.Source);
    }

    [Fact]
    public void GoogleNativeMimesMap()
    {
        KindIdentifier identifier = Identifier();

        Assert.Equal("google-doc", identifier.Identify("Doc", "application/vnd.google-apps.document", null).Kind);
        Assert.Equal("google-other", identifier.Identify("Drawing", "application/vnd.google-apps.drawing", null).Kind);
    }

    [Fact]
    public void GenericMimePrefixesMapWithLowConfidence()
    {
        KindIdentifier identifier = Identifier();

        KindIdentification pdf = identifier.Identify("file.unknownext", "application/pdf", null);
        KindIdentification image = identifier.Identify("file.unknownext", "image/png", null);
        KindIdentification text = identifier.Identify("notes.unknownext", "text/plain", null);

        Assert.Equal("pdf", pdf.Kind);
        Assert.Equal("image-raster", image.Kind);
        Assert.Equal("document-text", text.Kind);
        Assert.All(new[] { pdf, image, text }, identification =>
            Assert.Equal(KindIdentificationSource.Mime, identification.Source));
    }

    [Fact]
    public void UnknownInputsFallBack()
    {
        KindIdentifier identifier = Identifier();

        KindIdentification unknown = identifier.Identify("file", "application/octet-stream", null);

        Assert.Equal("unknown", unknown.Kind);
        Assert.Equal(0.1, unknown.Confidence);
        Assert.Equal(KindIdentificationSource.Fallback, unknown.Source);
    }

    [Fact]
    public void OnlyFirstEightKilobytesAreSniffed()
    {
        KindIdentifier identifier = Identifier();
        byte[] content = new byte[9000];
        byte[] png = Convert.FromHexString("89504E470D0A1A0A");
        Buffer.BlockCopy(png, 0, content, 0, png.Length);

        Assert.Equal("image-raster", identifier.Identify("big", null, content).Kind);
    }

    [Fact]
    public void UnknownRiffFallsThroughToExtension()
    {
        KindIdentifier identifier = Identifier();
        byte[] riff = Convert.FromHexString("524946460000000058585858");

        Assert.Equal("web-page", identifier.Identify("page.html", null, riff).Kind);
    }

    private static KindIdentifier Identifier()
    {
        var parsed = KindMapCatalog.Parse(KindMapJson);
        Assert.True(parsed.IsSuccess);
        return new KindIdentifier(parsed.Value!);
    }
}

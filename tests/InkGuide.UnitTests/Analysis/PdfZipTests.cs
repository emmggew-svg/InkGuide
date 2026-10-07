using System.IO;
using System.IO.Compression;
using System.Text;
using InkGuide.Analysis.Inspection;
using Xunit;

namespace InkGuide.UnitTests.Analysis;

public sealed class PdfZipTests
{
    private const string PdfDocument = "%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n"
        + "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 5 >>\nendobj\n"
        + "3 0 obj\n<< /Title (Hello World) >>\nendobj\ntrailer\n";

    [Fact]
    public void PdfTitleAndPageCountAreRead()
    {
        PdfInfo info = ContentInspector.ReadPdfInfo(Encoding.ASCII.GetBytes(PdfDocument));

        Assert.Equal("Hello World", info.Title);
        Assert.Equal(5, info.PageCount);
    }

    [Fact]
    public void PdfWithoutMetadataYieldsNulls()
    {
        PdfInfo info = ContentInspector.ReadPdfInfo(Encoding.ASCII.GetBytes("%PDF-1.7\ntrailer\n"));

        Assert.Null(info.Title);
        Assert.Null(info.PageCount);
    }

    [Fact]
    public void UnterminatedTitleYieldsNull()
    {
        PdfInfo info = ContentInspector.ReadPdfInfo(Encoding.ASCII.GetBytes("%PDF\n/Title (never ends"));

        Assert.Null(info.Title);
    }

    [Fact]
    public void ZipCentralDirectoryIsListed()
    {
        byte[] zip = BuildZip(("docs/a.txt", "hello"), ("img/b.png", "bytes"));

        ZipListing listing = ContentInspector.ReadZipListing(zip);

        Assert.Equal(2, listing.EntryCount);
        Assert.True(listing.IsComplete);
        Assert.Contains("docs", listing.TopLevelNames);
        Assert.Contains("img", listing.TopLevelNames);
    }

    [Fact]
    public void TruncatedAndForeignBytesAreIncomplete()
    {
        Assert.False(ContentInspector.ReadZipListing(new byte[10]).IsComplete);
        Assert.False(ContentInspector.ReadZipListing(Encoding.ASCII.GetBytes("definitely not a zip file........")).IsComplete);
        Assert.Equal(0, ContentInspector.ReadZipListing(new byte[10]).EntryCount);
    }

    private static byte[] BuildZip(params (string Name, string Content)[] entries)
    {
        using MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string content) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(name);
                using StreamWriter writer = new(entry.Open());
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }
}

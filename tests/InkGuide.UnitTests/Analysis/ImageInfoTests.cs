using System;
using InkGuide.Analysis.Inspection;
using Xunit;

namespace InkGuide.UnitTests.Analysis;

public sealed class ImageInfoTests
{
    [Fact]
    public void PngDimensionsAreRead()
    {
        byte[] content = Convert.FromHexString("89504E470D0A1A0A0000000D494844520000000300000002");

        ImageInfo? image = ContentInspector.ReadImageInfo(content);

        Assert.NotNull(image);
        Assert.Equal("png", image!.Format);
        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
    }

    [Fact]
    public void GifDimensionsAreRead()
    {
        byte[] content = Convert.FromHexString("47494638396103000200");

        ImageInfo? image = ContentInspector.ReadImageInfo(content);

        Assert.NotNull(image);
        Assert.Equal("gif", image!.Format);
        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
    }

    [Fact]
    public void BmpDimensionsAreRead()
    {
        byte[] content = Convert.FromHexString("424D000000000000000036000000280000000300000002000000");

        ImageInfo? image = ContentInspector.ReadImageInfo(content);

        Assert.NotNull(image);
        Assert.Equal("bmp", image!.Format);
        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
    }

    [Fact]
    public void JpegDimensionsAreRead()
    {
        byte[] content = Convert.FromHexString("FFD8FFE000104A46494600010200000100010000FFC0000B0800020003011100");

        ImageInfo? image = ContentInspector.ReadImageInfo(content);

        Assert.NotNull(image);
        Assert.Equal("jpeg", image!.Format);
        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
    }

    [Fact]
    public void WebpLosslessDimensionsAreRead()
    {
        byte[] content = Convert.FromHexString("5249464600000000574542505650384C000000002F02400000");

        ImageInfo? image = ContentInspector.ReadImageInfo(content);

        Assert.NotNull(image);
        Assert.Equal("webp", image!.Format);
        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
    }

    [Fact]
    public void WebpExtendedDimensionsAreRead()
    {
        byte[] content = Convert.FromHexString("524946460000000057454250565038580A00000000000000020000010000");

        ImageInfo? image = ContentInspector.ReadImageInfo(content);

        Assert.NotNull(image);
        Assert.Equal("webp", image!.Format);
        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
    }

    [Fact]
    public void WebpLossyDimensionsAreRead()
    {
        byte[] content = Convert.FromHexString("52494646000000005745425056503820000000000000009D012A03000200");

        ImageInfo? image = ContentInspector.ReadImageInfo(content);

        Assert.NotNull(image);
        Assert.Equal("webp", image!.Format);
        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
    }

    [Fact]
    public void GarbageAndTruncatedHeadersReturnNull()
    {
        Assert.Null(ContentInspector.ReadImageInfo(new byte[] { 0x01, 0x02, 0x03 }));
        Assert.Null(ContentInspector.ReadImageInfo(Convert.FromHexString("89504E47")));
        Assert.Null(ContentInspector.ReadImageInfo(Convert.FromHexString("FFD8FFE000104A46494600010200000100010000FFD9")));
        Assert.Null(ContentInspector.ReadImageInfo(Convert.FromHexString("5249464600000000574542505650385A")));
    }
}

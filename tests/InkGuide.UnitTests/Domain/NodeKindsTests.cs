using System.Linq;
using InkGuide.Domain.Kinds;
using Xunit;

namespace InkGuide.UnitTests.Domain;

public sealed class NodeKindsTests
{
    [Fact]
    public void TaxonomyHasThirtyNineStableIds()
    {
        Assert.Equal(39, NodeKinds.All.Length);
        Assert.Equal(NodeKinds.All.Length, NodeKinds.All.Distinct().Count());
    }

    [Theory]
    [InlineData("source-code")]
    [InlineData("web-page")]
    [InlineData("stylesheet")]
    [InlineData("client-script")]
    [InlineData("markup")]
    [InlineData("config")]
    [InlineData("data-structured")]
    [InlineData("data-tabular")]
    [InlineData("database")]
    [InlineData("notebook")]
    [InlineData("document-text")]
    [InlineData("document-rich")]
    [InlineData("spreadsheet")]
    [InlineData("presentation")]
    [InlineData("ebook")]
    [InlineData("pdf")]
    [InlineData("image-raster")]
    [InlineData("image-vector")]
    [InlineData("design-source")]
    [InlineData("audio")]
    [InlineData("video")]
    [InlineData("font")]
    [InlineData("archive")]
    [InlineData("executable")]
    [InlineData("library-binary")]
    [InlineData("installer")]
    [InlineData("disk-image")]
    [InlineData("model-3d")]
    [InlineData("game-asset")]
    [InlineData("minecraft-asset")]
    [InlineData("log")]
    [InlineData("certificate-or-key")]
    [InlineData("shortcut")]
    [InlineData("google-doc")]
    [InlineData("google-sheet")]
    [InlineData("google-slides")]
    [InlineData("google-other")]
    [InlineData("folder")]
    [InlineData("unknown")]
    public void EveryAppendixAKindIsKnown(string kind)
    {
        Assert.True(NodeKinds.IsKnown(kind));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pdf ")]
    [InlineData("PDF")]
    [InlineData("movie")]
    public void UnknownInputsAreRejected(string? kind)
    {
        Assert.False(NodeKinds.IsKnown(kind));
    }
}

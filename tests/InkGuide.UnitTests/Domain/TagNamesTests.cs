using InkGuide.Domain.ValueObjects;
using Xunit;

namespace InkGuide.UnitTests.Domain;

public sealed class TagNamesTests
{
    [Fact]
    public void NormalizeTrimsAndUppercases()
    {
        Assert.Equal("TYPE:WEB-SITE", TagNames.Normalize("  type:web-site  "));
    }

    [Theory]
    [InlineData("type:web-site", true)]
    [InlineData("plain", true)]
    [InlineData("a", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsValidAcceptsSaneNames(string? name, bool expected)
    {
        Assert.Equal(expected, TagNames.IsValid(name));
    }

    [Fact]
    public void IsValidRejectsTooLongAndControlNames()
    {
        Assert.False(TagNames.IsValid(new string('x', 129)));
        Assert.True(TagNames.IsValid(new string('x', 128)));
        Assert.False(TagNames.IsValid("a\tb"));
    }

    [Fact]
    public void SplitNamespaceSeparatesPrefix()
    {
        (string? prefix, string local) = TagNames.SplitNamespace("type:web-site");

        Assert.Equal("type", prefix);
        Assert.Equal("web-site", local);
    }

    [Fact]
    public void SplitNamespaceKeepsPlainNames()
    {
        (string? prefix, string local) = TagNames.SplitNamespace("plain");

        Assert.Null(prefix);
        Assert.Equal("plain", local);
    }

    [Fact]
    public void SplitNamespaceRejectsEdgeColons()
    {
        Assert.Equal((null, ":x"), TagNames.SplitNamespace(":x"));
        Assert.Equal((null, "a:"), TagNames.SplitNamespace("a:"));
    }
}

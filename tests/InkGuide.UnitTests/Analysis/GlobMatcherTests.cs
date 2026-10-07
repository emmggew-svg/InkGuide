using InkGuide.Analysis.Catalogs;
using Xunit;

namespace InkGuide.UnitTests.Analysis;

public sealed class GlobMatcherTests
{
    [Theory]
    [InlineData("README*", "README.md", true)]
    [InlineData("*.css", "style.css", true)]
    [InlineData("*.css", "a/b/style.css", true)]
    [InlineData("main.*", "main.ts", true)]
    [InlineData("bin/*", "bin/app.dll", true)]
    [InlineData("bin/*", "bin/a/app.dll", false)]
    [InlineData("bin/*", "app.dll", false)]
    [InlineData("**/node_modules/**", "a/node_modules/b/c.js", true)]
    [InlineData("next.config.*", "next.config.js", true)]
    [InlineData("ProjectSettings/ProjectVersion.txt", "ProjectSettings/ProjectVersion.txt", true)]
    [InlineData("wp-content/*", "wp-content/index.php", true)]
    [InlineData("index.html", "sub/index.html", true)]
    [InlineData("*.PEM", "key.pem", true)]
    [InlineData("id_rsa*", "id_rsa", true)]
    [InlineData("file.???", "file.txt", true)]
    [InlineData("file.???", "file.toolong", false)]
    public void MatchFileOrPathBehaves(string pattern, string path, bool expected)
    {
        Assert.Equal(expected, GlobMatcher.MatchFileOrPath(pattern, path, path));
    }

    [Fact]
    public void PatternWithoutSlashMatchesBasenameAtAnyDepth()
    {
        Assert.True(GlobMatcher.MatchFileOrPath("*.key", "server.key", "certs/server.key"));
        Assert.False(GlobMatcher.MatchPath("*.key", "certs/server.key"));
    }

    [Fact]
    public void BackslashesAndDotPrefixesNormalize()
    {
        Assert.True(GlobMatcher.MatchPath("a/b", "a\\b"));
        Assert.True(GlobMatcher.MatchPath("a/b", "./a/b"));
        Assert.Equal("b.txt", GlobMatcher.GetFileName("a\\b.txt"));
        Assert.Equal("b.txt", GlobMatcher.GetFileName("b.txt"));
        Assert.Equal("a/b", GlobMatcher.NormalizePath(".\\a\\b"));
    }
}

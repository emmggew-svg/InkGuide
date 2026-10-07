using InkGuide.Analysis.Catalogs;
using InkGuide.Domain.Common;
using Xunit;

namespace InkGuide.UnitTests.Analysis;

public sealed class CatalogTests
{
    private static readonly string[] IgnorePatterns = ["node_modules", ".git"];

    private static readonly string[] ArrayItems = ["a", "b"];
    private const string KindMapJson = """
        {
          "version": 1,
          "map": { ".cs": "source-code", "md": "document-text", ".png": "image-raster" },
          "mimeFallback": { "application/vnd.google-apps.document": "google-doc" }
        }
        """;

    private const string RolesJson = """
        {
          "version": 1,
          "rulesetVersion": "9.9.9-test",
          "roles": [
            { "id": "readme", "patterns": ["README*"], "priority": 100, "summaryTemplate": "Readme: {title}." },
            { "id": "plain", "patterns": ["*.txt"] }
          ]
        }
        """;

    private const string MarkersJson = """
        {
          "version": 1,
          "projects": [
            { "id": "web-nextjs", "name": "Next.js", "tags": ["type:web-site"],
              "markers": [{ "path-glob": "next.config.*" }, { "path-glob": "package.json", "contains": "next" }],
              "weight": 20, "excludes": ["node_modules/*"] }
          ],
          "monoRoots": [{ "markers": ["pnpm-workspace.yaml"] }]
        }
        """;

    [Fact]
    public void KindMapParsesAndLooksUp()
    {
        Result<KindMapCatalog, Failure> parsed = KindMapCatalog.Parse(KindMapJson);

        Assert.True(parsed.IsSuccess);
        Assert.Equal(1, parsed.Value!.Version);
        Assert.Equal("source-code", parsed.Value!.GetKindByExtension(".cs"));
        Assert.Equal("source-code", parsed.Value!.GetKindByExtension("cs"));
        Assert.Equal("document-text", parsed.Value!.GetKindByExtension(".MD"));
        Assert.Null(parsed.Value!.GetKindByExtension(".unknown"));
        Assert.Null(parsed.Value!.GetKindByExtension(null));
        Assert.Equal("google-doc", parsed.Value!.GetKindByMime("application/vnd.google-apps.document"));
        Assert.Equal("google-doc", parsed.Value!.GetKindByMime("application/vnd.google-apps.document; charset=utf-8"));
        Assert.Null(parsed.Value!.GetKindByMime(null));
    }

    [Fact]
    public void KindMapRejectsBadDocuments()
    {
        Assert.True(KindMapCatalog.Parse("not json").IsFailure);
        Assert.True(KindMapCatalog.Parse("[1,2]").IsFailure);
        Assert.True(KindMapCatalog.Parse("""{ "map": { ".x": "no-such-kind" } }""").IsFailure);
        Assert.True(KindMapCatalog.Parse("""{ "map": { ".x": 42 } }""").IsFailure);
        Assert.True(KindMapCatalog.Parse("""{ "mimeFallback": [] }""").IsFailure);
    }

    [Fact]
    public void KindMapToleratesMissingSections()
    {
        Result<KindMapCatalog, Failure> parsed = KindMapCatalog.Parse("""{ "version": 2 }""");

        Assert.True(parsed.IsSuccess);
        Assert.Equal(2, parsed.Value!.Version);
        Assert.Null(parsed.Value!.GetKindByExtension(".cs"));
    }

    [Fact]
    public void FileRolesParseWithDefaults()
    {
        Result<FileRoleCatalog, Failure> parsed = FileRoleCatalog.Parse(RolesJson);

        Assert.True(parsed.IsSuccess);
        Assert.Equal("9.9.9-test", parsed.Value!.RulesetVersion);
        Assert.Equal(2, parsed.Value!.Rules.Count);
        Assert.Equal(100, parsed.Value!.Rules[0].Priority);
        Assert.Equal(50, parsed.Value!.Rules[1].Priority);
        Assert.Equal(string.Empty, parsed.Value!.Rules[1].SummaryTemplate);
        Assert.False(parsed.Value!.Rules[1].IsSensitive);
        Assert.False(parsed.Value!.Rules[1].IsGenerated);
    }

    [Fact]
    public void FileRolesAcceptRulesArrayAndRejectBadRules()
    {
        Assert.True(FileRoleCatalog.Parse("""{ "rules": [{ "id": "x", "patterns": ["*.x"] }] }""").IsSuccess);
        Assert.True(FileRoleCatalog.Parse("bogus").IsFailure);
        Assert.True(FileRoleCatalog.Parse("""{ "roles": [{ "patterns": ["*.x"] }] }""").IsFailure);
        Assert.True(FileRoleCatalog.Parse("""{ "roles": [{ "id": "x", "patterns": [] }] }""").IsFailure);
        Assert.True(FileRoleCatalog.Parse("""{ "other": true }""").IsFailure);
        Assert.True(FileRoleCatalog.Parse("""{ "roles": ["nope"] }""").IsFailure);
    }

    [Fact]
    public void ProjectMarkersParseWithDefaults()
    {
        Result<ProjectMarkerCatalog, Failure> parsed = ProjectMarkerCatalog.Parse(MarkersJson);

        Assert.True(parsed.IsSuccess);
        Assert.Single(parsed.Value!.Projects);
        Assert.Equal("web-nextjs", parsed.Value!.Projects[0].Id);
        Assert.Equal(2, parsed.Value!.Projects[0].Markers.Count);
        Assert.Equal("next", parsed.Value!.Projects[0].Markers[1].Contains);
        Assert.Single(parsed.Value!.MonoRoots);
        Assert.Equal("mono-0", parsed.Value!.MonoRoots[0].Id);
    }

    [Fact]
    public void ProjectMarkersRejectBadDocuments()
    {
        Assert.True(ProjectMarkerCatalog.Parse("bogus").IsFailure);
        Assert.True(ProjectMarkerCatalog.Parse("[1]").IsFailure);
        Assert.True(ProjectMarkerCatalog.Parse("""{ "projects": [{ "name": "x", "markers": [] }] }""").IsFailure);
        Assert.True(ProjectMarkerCatalog.Parse("""{ "projects": [{ "id": "x" }] }""").IsFailure);
        Assert.True(ProjectMarkerCatalog.Parse("""{ "projects": [{ "id": "x", "markers": [{}] }] }""").IsFailure);
        Assert.True(ProjectMarkerCatalog.Parse("""{ "projects": [{ "id": "x", "markers": [{"path-glob": "a"}], "weight": -1 }] }""").IsFailure);
        Assert.True(ProjectMarkerCatalog.Parse("""{ "monoRoots": [{}] }""").IsFailure);
        Assert.True(ProjectMarkerCatalog.Parse("""{ "monoRoots": ["x"] }""").IsFailure);
    }

    [Fact]
    public void IgnoreListParsesBothShapes()
    {
        Result<System.Collections.Generic.IReadOnlyList<string>, Failure> fromObject =
            IgnoreList.Parse("""{ "version": 1, "patterns": ["node_modules", ".git"] }""");
        Result<System.Collections.Generic.IReadOnlyList<string>, Failure> fromArray =
            IgnoreList.Parse("""["a", "b"]""");

        Assert.True(fromObject.IsSuccess);
        Assert.Equal(IgnorePatterns, fromObject.Value!);
        Assert.True(fromArray.IsSuccess);
        Assert.Equal(ArrayItems, fromArray.Value!);
        Assert.True(IgnoreList.Parse("""{ "version": 1 }""").IsSuccess);
        Assert.True(IgnoreList.Parse("bogus").IsFailure);
        Assert.True(IgnoreList.Parse("42").IsFailure);
    }
}

using System.Linq;
using InkGuide.Analysis.Catalogs;
using InkGuide.UnitTests.Helpers;
using Xunit;

namespace InkGuide.UnitTests.Analysis;

public sealed class ShippedConfigTests
{
    [Fact]
    public void ShippedKindMapLoads()
    {
        KindMapCatalog catalog = ParseKindMap();

        Assert.Equal("source-code", catalog.GetKindByExtension(".cs"));
        Assert.Equal("client-script", catalog.GetKindByExtension(".js"));
        Assert.Equal("web-page", catalog.GetKindByExtension(".html"));
        Assert.Equal("pdf", catalog.GetKindByExtension(".pdf"));
        Assert.Equal("image-raster", catalog.GetKindByExtension(".png"));
        Assert.Equal("archive", catalog.GetKindByExtension(".zip"));
        Assert.Equal("executable", catalog.GetKindByExtension(".exe"));
        Assert.Equal("google-doc", catalog.GetKindByMime("application/vnd.google-apps.document"));
        Assert.Equal("google-sheet", catalog.GetKindByMime("application/vnd.google-apps.spreadsheet"));
    }

    [Fact]
    public void ShippedFileRolesLoad()
    {
        FileRoleCatalog catalog = ParseRoles();

        Assert.Contains(catalog.Rules, rule => rule.Id == "readme");
        Assert.Contains(catalog.Rules, rule => rule.Id == "license");
        Assert.Contains(catalog.Rules, rule => rule.Id == "package-manifest");
        Assert.Contains(catalog.Rules, rule => rule.Id == "env-config" && rule.IsSensitive);
        Assert.DoesNotContain(catalog.Rules, rule => rule.Patterns.Count == 0);
    }

    [Fact]
    public void ShippedProjectMarkersLoad()
    {
        ProjectMarkerCatalog catalog = ParseMarkers();

        Assert.Contains(catalog.Projects, project => project.Id == "web-nextjs");
        Assert.Contains(catalog.Projects, project => project.Id == "web-wordpress");
        Assert.Contains(catalog.Projects, project => project.Id == "dotnet-solution");
        Assert.Contains(catalog.Projects, project => project.Id == "node-package");
        Assert.Contains(catalog.Projects, project => project.Id == "python-project");
        Assert.Contains(catalog.Projects, project => project.Id == "minecraft-plugin");
        Assert.Contains(catalog.Projects, project => project.Id == "minecraft-mod");
        Assert.Contains(catalog.Projects, project => project.Id == "unity");
        Assert.Contains(catalog.Projects, project => project.Id == "godot");
        Assert.NotEmpty(catalog.MonoRoots);
        Assert.True(catalog.Projects.All(project => project.Weight >= 0 && project.Markers.Count > 0));
    }

    [Fact]
    public void ShippedIgnorePatternsLoad()
    {
        var parsed = IgnoreList.Parse(RepoFiles.ReadConfig("ignore-patterns.json"));

        Assert.True(parsed.IsSuccess);
        Assert.Contains("node_modules", parsed.Value!);
        Assert.Contains(".git", parsed.Value!);
        Assert.Contains("bin", parsed.Value!);
    }

    internal static KindMapCatalog ParseKindMap()
    {
        var parsed = KindMapCatalog.Parse(RepoFiles.ReadConfig("kind-map.json"));
        Assert.True(parsed.IsSuccess);
        return parsed.Value!;
    }

    internal static FileRoleCatalog ParseRoles()
    {
        var parsed = FileRoleCatalog.Parse(RepoFiles.ReadConfig("file-roles.json"));
        Assert.True(parsed.IsSuccess);
        return parsed.Value!;
    }

    internal static ProjectMarkerCatalog ParseMarkers()
    {
        var parsed = ProjectMarkerCatalog.Parse(RepoFiles.ReadConfig("project-markers.json"));
        Assert.True(parsed.IsSuccess);
        return parsed.Value!;
    }
}

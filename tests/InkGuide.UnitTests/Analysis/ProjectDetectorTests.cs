using System.Collections.Generic;
using System.Linq;
using InkGuide.Analysis.Projects;
using InkGuide.Analysis.Tags;
using Xunit;

namespace InkGuide.UnitTests.Analysis;

public sealed class ProjectDetectorTests
{
    private static readonly string[] NextjsPaths = ["package.json", "next.config.js"];

    private static readonly string[] PythonPaths = ["pyproject.toml"];

    private static readonly string[] PackageOnlyPaths = ["package.json"];

    private static readonly string[] StaticPaths = ["index.html", "style.css"];

    private static readonly string[] WordpressPaths = ["wp-config.php", "wp-content/index.php"];

    private static readonly string[] DotnetPaths = ["App.sln", "src/App/App.csproj"];

    private static readonly string[] PluginPaths = ["plugin.yml"];

    private static readonly string[] GodotPaths = ["project.godot"];

    private static readonly string[] RandomPaths = ["a.txt", "docs/b.md", "img/c.png"];

    private static readonly string[] MixedPaths = ["package.json", "shop/wp-config.php", "shop/wp-content/index.php"];

    private static readonly string[] DistPaths = ["dist/index.html"];

    private static readonly string[] SitePaths = ["site/index.html"];

    private static readonly string[] DeepPaths = ["x1/x2/x3/x4/x5/x6/x7/x8/index.html"];

    private static readonly string[] SolutionPaths = ["App.sln", "A/A.csproj", "B/B.csproj"];

    private static readonly string[] AutoTags = ["a", "z"];

    private static readonly string[] SuggestTags = ["b"];

    private static readonly string[] SuppressedNames = ["TYPE:ADS"];

    private static readonly string[] OkTags = ["ok"];

    private static readonly string[] SingleTag = ["a"];

    private const string MarkersJson = """
        {
          "version": 1,
          "projects": [
            { "id": "web-nextjs", "name": "Next.js", "tags": ["type:web-site", "tech:nextjs"],
              "markers": [{ "path-glob": "next.config.*" }, { "path-glob": "package.json", "contains": "next" }],
              "weight": 20, "excludes": [] },
            { "id": "node-package", "name": "Node", "tags": ["type:node-package"],
              "markers": [{ "path-glob": "package.json" }], "weight": 10, "excludes": ["node_modules/*"] },
            { "id": "python-project", "name": "Python", "tags": ["type:python"],
              "markers": [{ "path-glob": "pyproject.toml" }, { "path-glob": "requirements.txt" }, { "path-glob": "setup.py" }],
              "weight": 15, "excludes": [] },
            { "id": "web-wordpress", "name": "WordPress", "tags": ["type:web-site", "tech:wordpress"],
              "markers": [{ "path-glob": "wp-config.php" }, { "path-glob": "wp-content/*" }], "weight": 20, "excludes": [] },
            { "id": "web-static", "name": "Static", "tags": ["type:web-site"],
              "markers": [{ "path-glob": "index.html" }, { "path-glob": "*.css" }], "weight": 10, "excludes": [] },
            { "id": "dotnet-solution", "name": ".NET", "tags": ["type:dotnet"],
              "markers": [{ "path-glob": "*.sln" }, { "path-glob": "*.csproj" }], "weight": 20, "excludes": ["bin/*", "obj/*"] },
            { "id": "minecraft-plugin", "name": "MC plugin", "tags": ["type:minecraft-plugin"],
              "markers": [{ "path-glob": "plugin.yml" }], "weight": 20, "excludes": [] },
            { "id": "godot", "name": "Godot", "tags": ["type:game", "tech:godot"],
              "markers": [{ "path-glob": "project.godot" }], "weight": 20, "excludes": [] }
          ],
          "monoRoots": [{ "id": "monorepo", "markers": ["pnpm-workspace.yaml", "nx.json"] }]
        }
        """;

    [Fact]
    public void NextjsIsConfidentlyDetected()
    {
        ProjectDetector detector = Detector();
        var contents = new Dictionary<string, string>
        {
            ["package.json"] = """{"dependencies":{"next":"14.0.0"}}""",
        };

        ProjectDetectionResult result = detector.Detect(NextjsPaths, contents);

        Assert.False(result.IsMonoRoot);
        DetectedProject project = Assert.Single(result.Projects, item => item.DetectorId == "web-nextjs");
        Assert.Equal(string.Empty, project.RelativePath);
        Assert.Equal(0.769, project.Confidence, 3);
        Assert.Contains("type:web-site", project.Tags);
        Assert.NotEmpty(project.Evidence);
    }

    [Fact]
    public void PartialEvidenceBecomesASuggestion()
    {
        ProjectDetector detector = Detector();

        ProjectDetectionResult result = detector.Detect(PythonPaths);

        DetectedProject project = Assert.Single(result.Projects, item => item.DetectorId == "python-project");
        Assert.Equal(0.455, project.Confidence, 3);
        Assert.True(project.Confidence < 0.6);
    }

    [Fact]
    public void QuotedContainsAvoidsNextAuthFalsePositives()
    {
        ProjectDetector detector = Detector();
        var contents = new Dictionary<string, string>
        {
            ["package.json"] = """{"dependencies":{"next-auth":"4.0.0"}}""",
        };

        ProjectDetectionResult result = detector.Detect(PackageOnlyPaths, contents);

        Assert.DoesNotContain(result.Projects, item => item.DetectorId == "web-nextjs");
        Assert.Contains(result.Projects, item => item.DetectorId == "node-package");
    }

    [Fact]
    public void StaticWordpressDotnetMinecraftGodotDetect()
    {
        ProjectDetector detector = Detector();

        Assert.Equal("web-static", detector.Detect(StaticPaths).Projects[0].DetectorId);
        Assert.Equal("web-wordpress", detector.Detect(WordpressPaths).Projects[0].DetectorId);
        Assert.Equal("dotnet-solution", detector.Detect(DotnetPaths).Projects[0].DetectorId);
        Assert.Equal("minecraft-plugin", detector.Detect(PluginPaths).Projects[0].DetectorId);
        Assert.Equal("godot", detector.Detect(GodotPaths).Projects[0].DetectorId);
    }

    [Fact]
    public void RandomTreesYieldNothing()
    {
        ProjectDetector detector = Detector();

        ProjectDetectionResult result = detector.Detect(RandomPaths);

        Assert.Empty(result.Projects);
        Assert.False(result.IsMonoRoot);
    }

    [Fact]
    public void EmptyInputYieldsNothing()
    {
        ProjectDetectionResult result = Detector().Detect(System.Array.Empty<string>());

        Assert.Empty(result.Projects);
        Assert.False(result.IsMonoRoot);
    }

    [Fact]
    public void MonoRootChildrenBecomeProjects()
    {
        ProjectDetector detector = Detector();
        var contents = new Dictionary<string, string>
        {
            ["apps/web/package.json"] = """{"dependencies":{"next":"14.0.0"}}""",
        };
        string[] paths =
        {
            "pnpm-workspace.yaml",
            "apps/web/package.json",
            "apps/web/next.config.js",
            "services/api/requirements.txt",
        };

        ProjectDetectionResult result = detector.Detect(paths, contents);

        Assert.True(result.IsMonoRoot);
        Assert.Contains("pnpm-workspace.yaml", result.MonoRootEvidence);
        Assert.All(result.Projects, project => Assert.NotEqual(string.Empty, project.RelativePath));
        DetectedProject web = Assert.Single(result.Projects, item => item.DetectorId == "web-nextjs");
        Assert.Equal("apps/web", web.RelativePath);
        Assert.Contains(result.Projects, item => item.DetectorId == "python-project" && item.RelativePath == "services/api");
    }

    [Fact]
    public void NearestRootWinsOutsideMonoRepos()
    {
        ProjectDetector detector = Detector();
        var contents = new Dictionary<string, string>
        {
            ["package.json"] = """{"dependencies":{"react":"18.0.0"}}""",
        };

        ProjectDetectionResult result = detector.Detect(
            MixedPaths,
            contents);

        DetectedProject project = Assert.Single(result.Projects);
        Assert.Equal(string.Empty, project.RelativePath);
        Assert.Equal("node-package", project.DetectorId);
    }

    [Fact]
    public void ExcludedTreesProvideNoEvidence()
    {
        string catalog = """
            { "version": 1,
              "projects": [{ "id": "gen-site", "name": "Gen", "tags": [],
                "markers": [{ "path-glob": "index.html" }], "weight": 10, "excludes": ["dist/*"] }],
              "monoRoots": [] }
            """;
        ProjectDetector detector = Detector(catalog);

        Assert.Empty(detector.Detect(DistPaths).Projects);
        Assert.Single(detector.Detect(SitePaths).Projects);
    }

    [Fact]
    public void DeepEvidenceBeyondTheLimitIsIgnored()
    {
        ProjectDetector detector = Detector();

        ProjectDetectionResult result = detector.Detect(DeepPaths);

        Assert.Empty(result.Projects);
    }

    [Fact]
    public void MultiProjectSolutionsAreMonoRoots()
    {
        ProjectDetector detector = Detector();

        ProjectDetectionResult result = detector.Detect(SolutionPaths);

        Assert.True(result.IsMonoRoot);
        Assert.Contains("multi-project-sln", result.MonoRootEvidence);
    }

    [Fact]
    public void TagDeciderSplitsAutoApplyAndSuggestions()
    {
        var confidences = new Dictionary<string, double> { ["a"] = 0.9, ["z"] = 0.95, ["b"] = 0.3 };

        TagDecision decision = TagDecider.Decide(confidences, System.Array.Empty<string>());

        Assert.Equal(AutoTags, decision.AutoApply);
        Assert.Equal(SuggestTags, decision.Suggest);
    }

    [Fact]
    public void TagDeciderDropsSuppressedTags()
    {
        var confidences = new Dictionary<string, double> { ["type:ads"] = 0.9, ["ok"] = 0.9 };

        TagDecision decision = TagDecider.Decide(confidences, SuppressedNames);

        Assert.Equal(OkTags, decision.AutoApply);
        Assert.Empty(decision.Suggest);
    }

    [Fact]
    public void TagDeciderGuardsItsThreshold()
    {
        var confidences = new Dictionary<string, double> { ["a"] = 0.9 };

        Assert.Throws<System.ArgumentOutOfRangeException>(() => TagDecider.Decide(confidences, System.Array.Empty<string>(), 1.5));
        TagDecision all = TagDecider.Decide(confidences, System.Array.Empty<string>(), 1.0);
        Assert.Empty(all.AutoApply);
        Assert.Equal(SingleTag, all.Suggest);
    }

    private static ProjectDetector Detector(string? json = null)
    {
        var parsed = InkGuide.Analysis.Catalogs.ProjectMarkerCatalog.Parse(json ?? MarkersJson);
        Assert.True(parsed.IsSuccess);
        return new ProjectDetector(parsed.Value!);
    }
}

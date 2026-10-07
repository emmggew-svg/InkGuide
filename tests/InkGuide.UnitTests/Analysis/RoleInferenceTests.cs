using System.Collections.Generic;
using InkGuide.Analysis.Catalogs;
using InkGuide.Analysis.Inspection;
using InkGuide.Analysis.Roles;
using Xunit;

namespace InkGuide.UnitTests.Analysis;

public sealed class RoleInferenceTests
{
    private const string RolesJson = """
        {
          "version": 1,
          "rulesetVersion": "1.0.0",
          "roles": [
            { "id": "readme", "patterns": ["README*"], "priority": 100, "summaryTemplate": "Project readme: {title}. {lines} lines." },
            { "id": "entry-point", "patterns": ["index.html"], "priority": 50, "summaryTemplate": "Likely entry point: {name}." },
            { "id": "page", "patterns": ["*.html"], "priority": 50, "summaryTemplate": "Page {name}." },
            { "id": "package-manifest", "patterns": ["package.json"], "priority": 90, "summaryTemplate": "Package manifest for {project}." },
            { "id": "env-config", "patterns": [".env*"], "priority": 95, "sensitive": true, "summaryTemplate": "Sensitive configuration." },
            { "id": "generated-bundle", "patterns": ["dist/*"], "priority": 10, "generated": true, "summaryTemplate": "Generated output." },
            { "id": "build-with-paper", "patterns": ["*.gradle"], "priority": 60, "contentContains": ["paper-api"], "summaryTemplate": "Build script using paper." }
          ]
        }
        """;

    [Fact]
    public void ReadmeTemplateFillsSlots()
    {
        RoleInference inference = Inference();
        byte[] content = System.Text.Encoding.UTF8.GetBytes("# Hello\n\nWorld\n");
        TextAnalysis text = ContentInspector.AnalyzeText(content);
        StructuredPeek peek = ContentInspector.PeekStructured("README.md", content, text);

        RoleMatch match = inference.InferRole("README.md", "README.md", text, peek, "# Hello\n\nWorld\n");

        Assert.Equal("readme", match.RoleId);
        Assert.Equal(0.95, match.Confidence);
        Assert.False(match.IsSensitive);
        Assert.False(match.IsGenerated);
        Assert.Equal("Project readme: Hello. 3 lines.", match.Summary);
    }

    [Fact]
    public void SensitiveRuleWinsByPriority()
    {
        RoleInference inference = Inference();

        RoleMatch match = inference.InferRole(".env", ".env", null, null, null);

        Assert.Equal("env-config", match.RoleId);
        Assert.True(match.IsSensitive);
        Assert.Equal("Sensitive configuration.", match.Summary);
    }

    [Fact]
    public void FirstRuleWinsPriorityTies()
    {
        RoleInference inference = Inference();

        RoleMatch match = inference.InferRole("index.html", "index.html", null, null, null);

        Assert.Equal("entry-point", match.RoleId);
    }

    [Fact]
    public void ContentPredicatesGateMatches()
    {
        RoleInference inference = Inference();

        RoleMatch withMarker = inference.InferRole("build.gradle", "build.gradle", null, null, "dependencies { paper-api }");
        RoleMatch withoutMarker = inference.InferRole("build.gradle", "build.gradle", null, null, "dependencies { junit }");
        RoleMatch withoutText = inference.InferRole("build.gradle", "build.gradle", null, null, null);

        Assert.Equal("build-with-paper", withMarker.RoleId);
        Assert.Null(withoutMarker.RoleId);
        Assert.Null(withoutText.RoleId);
    }

    [Fact]
    public void GeneratedFlagSurvives()
    {
        RoleInference inference = Inference();

        RoleMatch match = inference.InferRole("bundle.js", "dist/bundle.js", null, null, null);

        Assert.Equal("generated-bundle", match.RoleId);
        Assert.True(match.IsGenerated);
    }

    [Fact]
    public void NoMatchYieldsEmptyRole()
    {
        RoleInference inference = Inference();

        RoleMatch match = inference.InferRole("notes.xyz", "notes.xyz", null, null, null);

        Assert.Null(match.RoleId);
        Assert.Equal(string.Empty, match.Summary);
    }

    [Fact]
    public void FillTemplateReplacesKnownSlots()
    {
        string filled = RoleInference.FillTemplate(
            "Hello {name}, welcome to {project} and {unknown}.",
            new Dictionary<string, string> { ["name"] = "Ada", ["project"] = "InkGuide" });

        Assert.Equal("Hello Ada, welcome to InkGuide and {unknown}.", filled);
        Assert.Equal("plain", RoleInference.FillTemplate("plain", new Dictionary<string, string>()));
    }

    private static RoleInference Inference()
    {
        var parsed = FileRoleCatalog.Parse(RolesJson);
        Assert.True(parsed.IsSuccess);
        return new RoleInference(parsed.Value!);
    }
}

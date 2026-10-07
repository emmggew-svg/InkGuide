using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Analysis.Catalogs;
using InkGuide.Analysis.Engine;
using InkGuide.Domain.Common;
using InkGuide.Domain.Entities;
using InkGuide.UnitTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InkGuide.UnitTests.Analysis;

public sealed class AnalysisEngineTests
{
    private const string KindMapJson = """
        {
          "version": 1,
          "map": { ".md": "document-text", ".png": "image-raster", ".json": "data-structured" },
          "mimeFallback": {}
        }
        """;

    private const string RolesJson = """
        {
          "version": 1,
          "rulesetVersion": "1.0.0",
          "roles": [
            { "id": "readme", "patterns": ["README*"], "priority": 100, "summaryTemplate": "Project readme: {title}. {lines} lines." },
            { "id": "package-manifest", "patterns": ["package.json"], "priority": 90, "summaryTemplate": "Package manifest for {project}." },
            { "id": "env-config", "patterns": [".env*"], "priority": 95, "sensitive": true, "summaryTemplate": "Sensitive configuration." }
          ]
        }
        """;

    [Fact]
    public async Task ReadmeIsIdentifiedDescribedAndTagged()
    {
        AnalysisEngine engine = Engine();
        byte[] content = Encoding.UTF8.GetBytes("# Hello\n\nWorld\n");

        Result<NodeInsight, Failure> result = await engine.AnalyzeAsync(
            Input("a1", "n1", "README.md", content),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        NodeInsight insight = result.Value!;
        Assert.Equal("document-text", insight.Kind);
        Assert.Equal("readme", insight.Role);
        Assert.Equal("Project readme: Hello. 3 lines.", insight.Summary);
        Assert.Equal(InsightSource.Rule, insight.Source);
        Assert.Equal(SensitivityLevel.None, insight.Sensitivity);
        Assert.Equal("1.0.0", insight.RulesetVersion);
        Assert.NotNull(insight.ContentHash);
        Assert.Equal(16, insight.ContentHash!.Length);
        Assert.InRange(insight.Confidence, 0.9, 1.0);
    }

    [Fact]
    public async Task SensitiveContentNeverLeaksIntoSummaries()
    {
        AnalysisEngine engine = Engine();
        byte[] content = Encoding.UTF8.GetBytes("API_KEY=sk-ant-secret-value-1234567890abcdef\n");

        Result<NodeInsight, Failure> result = await engine.AnalyzeAsync(
            Input("a1", "n1", ".env", content),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        NodeInsight insight = result.Value!;
        Assert.Equal(SensitivityLevel.Sensitive, insight.Sensitivity);
        Assert.Equal("env-config", insight.Role);
        Assert.DoesNotContain("sk-ant-secret-value", insight.Summary);
    }

    [Fact]
    public async Task KeyMaterialChangesTheKind()
    {
        AnalysisEngine engine = Engine();

        Result<NodeInsight, Failure> result = await engine.AnalyzeAsync(
            Input("a1", "n1", "id_rsa", null),
            CancellationToken.None);

        Assert.Equal("certificate-or-key", result.Value!.Kind);
        Assert.Equal(SensitivityLevel.Sensitive, result.Value.Sensitivity);
    }

    [Fact]
    public async Task FoldersSummarizeChildCounts()
    {
        AnalysisEngine engine = Engine();
        AnalysisInput input = Input("a1", "n1", "docs", null) with { IsFolder = true, ChildCount = 3 };

        Result<NodeInsight, Failure> result = await engine.AnalyzeAsync(input, CancellationToken.None);

        Assert.Equal("folder", result.Value!.Kind);
        Assert.Contains("3", result.Value.Summary);
        Assert.Null(result.Value.Role);
    }

    [Fact]
    public async Task ImagesSummarizeDimensions()
    {
        AnalysisEngine engine = Engine();
        byte[] png = Convert.FromHexString("89504E470D0A1A0A0000000D494844520000000300000002");

        Result<NodeInsight, Failure> result = await engine.AnalyzeAsync(
            Input("a1", "n1", "photo.png", png),
            CancellationToken.None);

        Assert.Equal("image-raster", result.Value!.Kind);
        Assert.Contains("3x2", result.Value.Summary);
    }

    [Fact]
    public async Task ProjectContextEnrichesGenericSummaries()
    {
        AnalysisEngine engine = Engine();
        byte[] content = Encoding.UTF8.GetBytes(new string('x', 100));
        AnalysisInput input = Input("a1", "n1", "notes.xyz", content) with { ProjectName = "Shop" };

        Result<NodeInsight, Failure> result = await engine.AnalyzeAsync(input, CancellationToken.None);

        Assert.Equal("unknown", result.Value!.Kind);
        Assert.Contains("Shop", result.Value.Summary);
        Assert.InRange(result.Value.Confidence, 0.0, 0.6);
    }

    [Fact]
    public async Task MissingIdentityIsRejected()
    {
        AnalysisEngine engine = Engine();

        Result<NodeInsight, Failure> result = await engine.AnalyzeAsync(
            Input("a1", "n1", string.Empty, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.InvalidParams, result.Error!.Code);
    }

    [Fact]
    public async Task CancellationIsHonoured()
    {
        AnalysisEngine engine = Engine();
        using CancellationTokenSource source = new();
        source.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => engine.AnalyzeAsync(Input("a1", "n1", "a.txt", null), source.Token));
    }

    private static AnalysisEngine Engine()
    {
        var kinds = KindMapCatalog.Parse(KindMapJson);
        var roles = FileRoleCatalog.Parse(RolesJson);
        Assert.True(kinds.IsSuccess);
        Assert.True(roles.IsSuccess);
        return new AnalysisEngine(
            kinds.Value!,
            roles.Value!,
            TimeProvider.System,
            NullLogger<AnalysisEngine>.Instance);
    }

    private static AnalysisInput Input(string accountId, string nodeId, string fileName, byte[]? content)
    {
        return new AnalysisInput(
            accountId,
            nodeId,
            fileName,
            null,
            null,
            false,
            0,
            null,
            null,
            null,
            content);
    }
}

using System;
using System.Linq;
using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace InkGuide.ArchitectureTests;

/// <summary>Layer rules from MASTER_PROMPT Sec 6.2.</summary>
public sealed class LayerRulesTests
{
    private static Assembly Domain() => typeof(InkGuide.Domain.AppIdentity).Assembly;

    private static Assembly Application() => typeof(InkGuide.Application.Tags.TagPolicy).Assembly;

    private static Assembly Analysis() => typeof(InkGuide.Analysis.Tags.TagDecider).Assembly;

    private static Assembly Persistence() => typeof(InkGuide.Infrastructure.Persistence.InkGuideDbContext).Assembly;

    private static Assembly Storage() => typeof(InkGuide.Infrastructure.Storage.LocalFolder.LocalFolderProvider).Assembly;

    private static Assembly Platform() => typeof(InkGuide.Infrastructure.Platform.Secrets.LogRedactor).Assembly;

    private static Assembly Bridge() => typeof(InkGuide.Bridge.Handlers.BridgeDispatcher).Assembly;

    [Fact]
    public void DomainDependsOnNothingProjectScoped()
    {
        var result = Types.InAssembly(Domain())
            .ShouldNot()
            .HaveDependencyOnAny("InkGuide.Application", "InkGuide.Analysis", "InkGuide.Infrastructure", "InkGuide.Bridge", "InkGuide.App", "InkGuide.Tools", "Google.Apis", "Microsoft.EntityFrameworkCore", "Microsoft.Web.WebView2")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }

    [Fact]
    public void ApplicationOnlyReferencesDomain()
    {
        var result = Types.InAssembly(Application())
            .ShouldNot()
            .HaveDependencyOnAny("InkGuide.Analysis", "InkGuide.Infrastructure", "InkGuide.Bridge", "InkGuide.App", "Google.Apis", "Microsoft.EntityFrameworkCore", "Microsoft.Web.WebView2")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }

    [Fact]
    public void AnalysisOnlyReferencesDomainAndApplication()
    {
        var result = Types.InAssembly(Analysis())
            .ShouldNot()
            .HaveDependencyOnAny("InkGuide.Infrastructure", "InkGuide.Bridge", "InkGuide.App", "Google.Apis", "Microsoft.EntityFrameworkCore", "Microsoft.Web.WebView2")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }

    [Fact]
    public void InfrastructureNeverReferencedByApplicationAnalysisBridge()
    {
        foreach (Assembly source in new[] { Application(), Analysis(), Bridge() })
        {
            var result = Types.InAssembly(source)
                .ShouldNot()
                .HaveDependencyOn("InkGuide.Infrastructure")
                .GetResult();
            Assert.True(result.IsSuccessful, source.GetName().Name + ": " + string.Join(",", result.FailingTypeNames ?? Enumerable.Empty<string>()));
        }
    }

    [Fact]
    public void BridgeOnlyReferencesApplicationAndDomain()
    {
        var result = Types.InAssembly(Bridge())
            .ShouldNot()
            .HaveDependencyOnAny("InkGuide.Infrastructure", "InkGuide.App", "Microsoft.Web.WebView2", "Google.Apis")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }

    [Fact]
    public void GoogleTypesStayInsideStorageGoogleDrive()
    {
        var outside = new[] { Domain(), Application(), Analysis(), Persistence(), Platform(), Bridge() };
        foreach (Assembly asm in outside)
        {
            var result = Types.InAssembly(asm)
                .ShouldNot()
                .HaveDependencyOn("Google.Apis")
                .GetResult();
            Assert.True(result.IsSuccessful, asm.GetName().Name + " leaks Google.Apis");
        }
    }

    [Fact]
    public void PersistenceStoragePlatformOnlyReferenceDomainApplication()
    {
        foreach (Assembly asm in new[] { Persistence(), Storage(), Platform() })
        {
            var result = Types.InAssembly(asm)
                .ShouldNot()
                .HaveDependencyOnAny("InkGuide.Bridge", "InkGuide.App", "InkGuide.Analysis")
                .GetResult();
            Assert.True(result.IsSuccessful, asm.GetName().Name + ": " + string.Join(",", result.FailingTypeNames ?? Enumerable.Empty<string>()));
        }
    }
}

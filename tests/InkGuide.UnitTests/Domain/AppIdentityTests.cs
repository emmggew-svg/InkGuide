using InkGuide.Domain;
using Xunit;

namespace InkGuide.UnitTests.Domain;

public sealed class AppIdentityTests
{
    [Fact]
    public void IdentityStringsAreStable()
    {
        Assert.Equal("InkGuide", AppIdentity.AppName);
        Assert.Equal("inkguide.desktop", AppIdentity.AppId);
        Assert.Equal("InkGuide Workspace", AppIdentity.WorkspaceFolderName);
        Assert.Equal(".inkguide", AppIdentity.ManifestFolderName);
        Assert.Equal("inkguide", AppIdentity.AppPropertiesPrefix);
        Assert.Equal("inkguide.k", AppIdentity.PropertyKind);
        Assert.Equal("inkguide.s", AppIdentity.PropertyInsightSource);
        Assert.Equal("inkguide.dh", AppIdentity.PropertyDescriptionHash);
        Assert.Equal("inkguide.ch", AppIdentity.PropertyContentHash);
        Assert.Equal("inkguide.op", AppIdentity.PropertyOperationId);
        Assert.Equal("1.0.0", AppIdentity.RulesetVersion);
    }
}

using InkGuide.Analysis.Sensitivity;
using Xunit;

namespace InkGuide.UnitTests.Analysis;

public sealed class SensitivityDetectorTests
{
    [Theory]
    [InlineData(".env")]
    [InlineData(".env.local")]
    [InlineData("server.pem")]
    [InlineData("server.key")]
    [InlineData("cert.pfx")]
    [InlineData("id_rsa")]
    [InlineData("id_ed25519")]
    [InlineData("credentials.json")]
    [InlineData("client_secret.json")]
    [InlineData("mytoken.json")]
    [InlineData("secrets.yaml")]
    [InlineData(".npmrc")]
    public void SensitiveNamesAreDetected(string fileName)
    {
        SensitivityFinding finding = SensitivityDetector.Detect(fileName, null);

        Assert.True(finding.IsSensitive);
        Assert.NotEmpty(finding.Reasons);
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("photo.png")]
    [InlineData("README.md")]
    public void OrdinaryNamesAreClean(string fileName)
    {
        Assert.False(SensitivityDetector.Detect(fileName, null).IsSensitive);
    }

    [Fact]
    public void PemHeadersAreDetectedInContent()
    {
        SensitivityFinding finding = SensitivityDetector.Detect(
            "notes.txt", "prefix -----BEGIN RSA PRIVATE KEY----- suffix");

        Assert.True(finding.IsSensitive);
    }

    [Fact]
    public void ApiKeyPrefixesAreDetectedInContent()
    {
        Assert.True(SensitivityDetector.Detect("a.txt", "key sk-ant-abcdef").IsSensitive);
        Assert.True(SensitivityDetector.Detect("a.txt", "token ya29.abcdef").IsSensitive);
        Assert.True(SensitivityDetector.Detect("a.txt", "ci AKIAIOSFODNN7EXAMPLE").IsSensitive);
    }

    [Fact]
    public void HighEntropyTokensAreDetected()
    {
        SensitivityFinding finding = SensitivityDetector.Detect(
            "data.bin", "token K9xQ2mZ8vL4pR7tYw3aB6cD5eF0gH1j end");

        Assert.True(finding.IsSensitive);
        Assert.Contains("content:high-entropy-token", finding.Reasons);
    }

    [Fact]
    public void LowEntropyRunsAreIgnored()
    {
        Assert.False(SensitivityDetector.Detect("data.bin", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa").IsSensitive);
    }

    [Fact]
    public void KeyMaterialNamesAreRecognised()
    {
        Assert.True(SensitivityDetector.LooksLikeKeyMaterial("server.pem"));
        Assert.True(SensitivityDetector.LooksLikeKeyMaterial("id_rsa"));
        Assert.False(SensitivityDetector.LooksLikeKeyMaterial("notes.txt"));
        Assert.False(SensitivityDetector.LooksLikeKeyMaterial(".env"));
    }
}

using System.Text;
using InkGuide.Analysis.Inspection;
using Xunit;

namespace InkGuide.UnitTests.Analysis;

public sealed class StructuredPeekTests
{
    private static readonly string[] JsonKeys = ["b", "a"];

    private static readonly string[] YamlKeys = ["name", "version", "nested"];

    private static readonly string[] TomlSections = ["package", "dependencies"];

    private static readonly string[] CsvHeaders = ["name", "age", "city"];

    private static readonly string[] QuotedCsvHeaders = ["a,b", "c"];

    [Fact]
    public void JsonTopKeysAreExtracted()
    {
        TextAnalysis text = ContentInspector.AnalyzeText(Encoding.UTF8.GetBytes("""{"b":1,"a":2}"""));

        StructuredPeek peek = ContentInspector.PeekStructured("data.json", Encoding.UTF8.GetBytes("""{"b":1,"a":2}"""), text);

        Assert.Equal(JsonKeys, peek.JsonTopKeys);
        Assert.True(peek.HasFacts);
    }

    [Fact]
    public void InvalidJsonYieldsNoFacts()
    {
        byte[] content = Encoding.UTF8.GetBytes("{oops");
        TextAnalysis text = ContentInspector.AnalyzeText(content);

        StructuredPeek peek = ContentInspector.PeekStructured("data.json", content, text);

        Assert.Null(peek.JsonTopKeys);
        Assert.False(peek.HasFacts);
    }

    [Fact]
    public void PackageJsonFactsAreExtracted()
    {
        string json = """
            {"name":"shop","description":"A shop","scripts":{"dev":"next dev"},
             "dependencies":{"next":"14.0.0"},"devDependencies":{"typescript":"5.0.0"}}
            """;
        byte[] content = Encoding.UTF8.GetBytes(json);

        StructuredPeek peek = ContentInspector.PeekStructured("package.json", content, ContentInspector.AnalyzeText(content));

        Assert.NotNull(peek.PackageJson);
        Assert.Equal("shop", peek.PackageJson!.Name);
        Assert.Equal("A shop", peek.PackageJson.Description);
        Assert.Contains("dev", peek.PackageJson.ScriptNames);
        Assert.Contains("next", peek.PackageJson.DependencyNames);
        Assert.Contains("typescript", peek.PackageJson.DependencyNames);
    }

    [Fact]
    public void YamlTopKeysAreExtracted()
    {
        string yaml = "name: test\nversion: 1\n# comment\nnested:\n  a: 1\n";
        byte[] content = Encoding.UTF8.GetBytes(yaml);

        StructuredPeek peek = ContentInspector.PeekStructured("config.yaml", content, ContentInspector.AnalyzeText(content));

        Assert.Equal(YamlKeys, peek.YamlTopKeys);
    }

    [Fact]
    public void TomlSectionsAreExtracted()
    {
        string toml = "[package]\nname = \"x\"\n[dependencies]\n";
        byte[] content = Encoding.UTF8.GetBytes(toml);

        StructuredPeek peek = ContentInspector.PeekStructured("Cargo.toml", content, ContentInspector.AnalyzeText(content));

        Assert.Equal(TomlSections, peek.TomlSections);
    }

    [Fact]
    public void XmlOutlineAndCsprojFactsAreExtracted()
    {
        string csproj = """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net9.0-windows</TargetFramework><UseWPF>true</UseWPF></PropertyGroup><ItemGroup><PackageReference Include="Serilog" Version="4.3.0" /></ItemGroup></Project>
            """;
        byte[] content = Encoding.UTF8.GetBytes(csproj);

        StructuredPeek peek = ContentInspector.PeekStructured("App.csproj", content, ContentInspector.AnalyzeText(content));

        Assert.Equal("Project", peek.XmlRoot);
        Assert.Contains("PropertyGroup", peek.XmlChildNames!);
        Assert.NotNull(peek.Csproj);
        Assert.Equal("Microsoft.NET.Sdk", peek.Csproj!.Sdk);
        Assert.Contains("net9.0-windows", peek.Csproj.TargetFrameworks);
        Assert.True(peek.Csproj.UsesWpf);
        Assert.False(peek.Csproj.UsesWindowsForms);
        Assert.Contains("Serilog", peek.Csproj.PackageReferences);
    }

    [Fact]
    public void InvalidXmlYieldsNoFacts()
    {
        byte[] content = Encoding.UTF8.GetBytes("<oops");
        TextAnalysis text = ContentInspector.AnalyzeText(content);

        StructuredPeek peek = ContentInspector.PeekStructured("data.xml", content, text);

        Assert.Null(peek.XmlRoot);
    }

    [Fact]
    public void CsvHeadersAndDelimitersAreDetected()
    {
        StructuredPeek comma = PeekCsv("a.csv", "name,age,city\nAlice,30,Istanbul\n");
        StructuredPeek tabs = PeekCsv("a.tsv", "a\tb\n1\t2\n");
        StructuredPeek quoted = PeekCsv("a.csv", "\"a,b\",c\n1,2\n");

        Assert.Equal(CsvHeaders, comma.CsvHeaders);
        Assert.Equal("comma", comma.CsvDelimiter);
        Assert.Equal("tab", tabs.CsvDelimiter);
        Assert.Equal(QuotedCsvHeaders, quoted.CsvHeaders);
    }

    [Fact]
    public void HtmlTitleAndDescriptionAreExtracted()
    {
        string html = """<html><head><title>My Site</title><meta name="description" content="A test site"></head></html>""";
        byte[] content = Encoding.UTF8.GetBytes(html);

        StructuredPeek peek = ContentInspector.PeekStructured("index.html", content, ContentInspector.AnalyzeText(content));

        Assert.Equal("My Site", peek.HtmlTitle);
        Assert.Equal("A test site", peek.HtmlDescription);
    }

    [Fact]
    public void MarkdownH1IsExtracted()
    {
        byte[] content = Encoding.UTF8.GetBytes("# Hello\n\nBody text\n");

        StructuredPeek peek = ContentInspector.PeekStructured("README.md", content, ContentInspector.AnalyzeText(content));

        Assert.Equal("Hello", peek.MarkdownH1);
    }

    [Fact]
    public void DocExcerptsFollowCommentStyles()
    {
        Assert.Equal(
            "Hello world second line",
            PeekExcerpt("a.cs", "// Hello world\n// second line\nvar x = 1;"));
        Assert.Equal(
            "comment here",
            PeekExcerpt("a.py", "#!/usr/bin/env python\n# comment here\nx = 1"));
        Assert.Null(PeekExcerpt("a.cs", "var x = 1;"));
    }

    [Fact]
    public void DocExcerptIsSkippedForSensitiveNodes()
    {
        byte[] content = Encoding.UTF8.GetBytes("// API_KEY=abc123\n");
        TextAnalysis text = ContentInspector.AnalyzeText(content);

        StructuredPeek peek = ContentInspector.PeekStructured("a.cs", content, text, includeDocExcerpt: false);

        Assert.Null(peek.DocExcerpt);
        Assert.False(peek.HasFacts);
    }

    [Fact]
    public void BinaryContentYieldsNoFacts()
    {
        byte[] content = new byte[] { 0x00, 0x01, 0x02 };
        TextAnalysis text = ContentInspector.AnalyzeText(content);

        StructuredPeek peek = ContentInspector.PeekStructured("a.bin", content, text);

        Assert.False(peek.HasFacts);
    }

    private static StructuredPeek PeekCsv(string fileName, string csv)
    {
        byte[] content = Encoding.UTF8.GetBytes(csv);
        return ContentInspector.PeekStructured(fileName, content, ContentInspector.AnalyzeText(content));
    }

    private static string? PeekExcerpt(string fileName, string source)
    {
        byte[] content = Encoding.UTF8.GetBytes(source);
        return ContentInspector.PeekStructured(fileName, content, ContentInspector.AnalyzeText(content)).DocExcerpt;
    }
}

using AwesomeAssertions;

using Kora.Core.Presentation.CodeHighlighting;

namespace Kora.Core.UnitTests.Presentation;

public sealed class CodeHighlightingTests
{
    [Theory]
    [InlineData(" powershell ", CodeLanguage.PowerShell)]
    [InlineData("ps1", CodeLanguage.PowerShell)]
    [InlineData("CS", CodeLanguage.CSharp)]
    [InlineData("csharp", CodeLanguage.CSharp)]
    [InlineData("json", CodeLanguage.Json)]
    [InlineData("yml", CodeLanguage.Yaml)]
    [InlineData("yaml", CodeLanguage.Yaml)]
    [InlineData("xaml", CodeLanguage.Xml)]
    [InlineData("xml", CodeLanguage.Xml)]
    [InlineData("md", CodeLanguage.Markdown)]
    [InlineData("markdown", CodeLanguage.Markdown)]
    [InlineData("sql", CodeLanguage.Sql)]
    [InlineData("js", CodeLanguage.JavaScript)]
    [InlineData("javascript", CodeLanguage.JavaScript)]
    [InlineData("ts", CodeLanguage.TypeScript)]
    [InlineData("typescript", CodeLanguage.TypeScript)]
    [InlineData("html", CodeLanguage.Html)]
    [InlineData("htm", CodeLanguage.Html)]
    [InlineData("css", CodeLanguage.Css)]
    [InlineData("sh", CodeLanguage.Shell)]
    [InlineData("shell", CodeLanguage.Shell)]
    [InlineData("bash", CodeLanguage.Shell)]
    [InlineData("bat", CodeLanguage.Batch)]
    [InlineData("batch", CodeLanguage.Batch)]
    [InlineData("cmd", CodeLanguage.Batch)]
    [InlineData("ini", CodeLanguage.Ini)]
    [InlineData("properties", CodeLanguage.Ini)]
    [InlineData("patch", CodeLanguage.Diff)]
    [InlineData("diff", CodeLanguage.Diff)]
    [InlineData(null, CodeLanguage.PlainText)]
    [InlineData("", CodeLanguage.PlainText)]
    [InlineData("cs js", CodeLanguage.PlainText)]
    [InlineData("https://example.invalid/grammar.js", CodeLanguage.PlainText)]
    [InlineData("mermaid", CodeLanguage.PlainText)]
    public void FenceAliasesUseOnlyTheBundledTable(string? alias, CodeLanguage expected) =>
        CodeHighlightProfile.Resolve(fenceLanguage: alias).Should().Be(expected);

    [Theory]
    [InlineData("code.cs", CodeLanguage.CSharp)]
    [InlineData(@"C:\snapshot\file.ps1", CodeLanguage.PowerShell)]
    [InlineData("snapshot/file.json", CodeLanguage.Json)]
    [InlineData("Dockerfile", CodeLanguage.Shell)]
    [InlineData(".gitignore", CodeLanguage.Ini)]
    [InlineData("file", CodeLanguage.PlainText)]
    [InlineData("unknown.ext", CodeLanguage.PlainText)]
    [InlineData("", CodeLanguage.PlainText)]
    public void TrustedSnapshotNamesPrecedeFences(string name, CodeLanguage expected) =>
        CodeHighlightProfile.Resolve(snapshotFileName: name, fenceLanguage: "js").Should().Be(expected);

    [Fact]
    public void HostChoicePrecedesNamesAndUnsupportedAuthorityNeverFallsThrough()
    {
        CodeHighlightProfile.Resolve("yaml", "file.cs", "js").Should().Be(CodeLanguage.Yaml);
        CodeHighlightProfile.Resolve("unknown", "file.cs", "js").Should().Be(CodeLanguage.PlainText);
        CodeHighlightProfile.Resolve("", "file.cs", "js").Should().Be(CodeLanguage.PlainText);
        CodeHighlightProfile.Name.Should().Be("offline-code-v1");
        CodeHighlightProfile.MaximumDuration.Should().Be(TimeSpan.FromMilliseconds(100));
        CodeHighlightProfile.MaximumUtf8Bytes.Should().Be(32768);
        CodeHighlightProfile.MaximumLines.Should().Be(1024);
        CodeHighlightProfile.MaximumTokens.Should().Be(2048);
        CodeHighlightProfile.MaximumNesting.Should().Be(64);
    }
}

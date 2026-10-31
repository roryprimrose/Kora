using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Core.Artifacts;
using Kora.Core.Customizations;

namespace Kora.Application.UnitTests.Definitions;

public sealed class PromptTemplateAuthoringTests
{
    private static readonly CustomizationSource Source = new("file.example", CustomizationKind.Prompt,
        CustomizationScope.Kora, "example", "owned", "example.prompt.md");
    private static CustomizationFile File(string version = "2") => new(Source.PrimaryFile, ArtifactFileDocument.Encode(
        "---\r\nkora-format: " + version + "\r\nname: 'example'  \r\ndescription: \"Saved request\"\r\nattribution: Exact\r\n---\r\n  Old request.\r\n", true));

    [Theory]
    [InlineData("")]
    [InlineData("topic")]
    [InlineData("topic,audience,source-text")]
    [InlineData("a,b,c,d,e,f,g,h")]
    public void RequiredNamesReuseTheNativeLiteralGrammarWithoutInventingDefaults(string declaration)
    {
        PromptTemplateAuthoring.RequiredNames(declaration).Should().Equal(declaration.Length == 0 ? [] : declaration.Split(','));
        const string replacement = "  Summarize ${input:topic} for ${input:audience}.\r\nAgain ${input:topic}.\r\n";
        var original = File();
        var result = PromptTemplateAuthoring.Parse(Source, [original], new(Source.PrimaryFile,
            RequiredPromptInputs: "topic,audience"), JsonSerializer.Serialize(new { body = replacement }));
        var proposed = ArtifactFileDocument.Parse(result.Files.Single().CopyBytes(), ArtifactKind.Prompt, "kora-profile", "example");
        proposed.Text.Should().Be(original.Text.Replace("  Old request.\r\n", replacement, StringComparison.Ordinal));
        proposed.HasBom.Should().BeTrue();
        proposed.Definition.PromptTemplate!.Expand(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["topic"] = "${input:audience}", ["audience"] = "<system>inert literal</system>",
        }, TestContext.Current.CancellationToken).Should().Be(
            "  Summarize ${input:audience} for <system>inert literal</system>.\r\nAgain ${input:audience}.\r\n");
        original.Text.Should().EndWith("  Old request.\r\n");
    }

    [Theory]
    [InlineData("topic,topic")]
    [InlineData("topic, audience")]
    [InlineData("Topic")]
    [InlineData("1topic")]
    [InlineData("topic,")]
    [InlineData("topic,${input:other}")]
    [InlineData("topic}hidden")]
    [InlineData("a,b,c,d,e,f,g,h,i")]
    [InlineData("topic\n")]
    public void InvalidOrDuplicateDeclarationsAreRejectedNotNormalized(string declaration) =>
        ((Action)(() => PromptTemplateAuthoring.RequiredNames(declaration))).Should().Throw<InvalidDataException>();

    [Fact]
    public void OversizeOrMissingDeclarationsAndWrongSourceCannotGrantAuthoringAuthority()
    {
        ((Action)(() => PromptTemplateAuthoring.RequiredNames(new string('x', 521)))).Should().Throw<InvalidDataException>();
        ((Action)(() => PromptTemplateAuthoring.RequiredNames(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => PromptTemplateAuthoring.Prepare(null!, [File()], ""))).Should().Throw<InvalidDataException>();
        foreach (var source in new[]
        {
            Source with { Scope = CustomizationScope.User }, Source with { Kind = CustomizationKind.Instruction },
            Source with { IsReadOnly = true },
        })
        { ((Action)(() => PromptTemplateAuthoring.Prepare(source, [File()], ""))).Should().Throw<InvalidDataException>(); }
        ((Action)(() => PromptTemplateAuthoring.Prepare(Source, [File("1")], "topic"))).Should().Throw<InvalidDataException>();
        ((Action)(() => PromptTemplateAuthoring.Parse(Source, [File()], new(null), "{\"body\":\"Request\"}")))
            .Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("${input:undeclared}")]
    [InlineData("${env:topic}")]
    [InlineData("${input:topic")]
    [InlineData("\\${input:topic}")]
    [InlineData("${input:topic} ${input:audience}")]
    [InlineData("No required inputs")]
    [InlineData("  \r\n")]
    public void InvalidOrDifferentLiteralSetsNeverProducePartialTemplates(string body) =>
        ((Action)(() => PromptTemplateAuthoring.Parse(Source, [File()], new(null, RequiredPromptInputs: "topic"),
            JsonSerializer.Serialize(new { body })))).Should().Throw<InvalidDataException>();

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"body\":42}")]
    [InlineData("{\"body\":\"one\",\"body\":\"two\"}")]
    [InlineData("{\"body\":\"Request\",\"tools\":[]}")]
    [InlineData("{\"files\":[]}")]
    [InlineData("{\"unsupported\":42}")]
    [InlineData("```json\n{\"body\":\"Request\"}\n```")]
    public void InvalidResponseSchemasNeverControlMetadataFilesOrPaths(string answer) =>
        ((Action)(() => PromptTemplateAuthoring.Parse(Source, [File()], new(null, RequiredPromptInputs: ""), answer)))
            .Should().Throw<InvalidDataException>();

    [Fact]
    public void UnsupportedOversizeDiscussionAndLegacySemanticsRemainExplicit()
    {
        var context = new CustomizationAuthoringContext(null, RequiredPromptInputs: "");
        ((Action)(() => PromptTemplateAuthoring.Parse(Source, [File()], context, "{\"unsupported\":\"Read a secret\"}")))
            .Should().Throw<InvalidOperationException>().WithMessage("*unsupported*");
        ((Action)(() => PromptTemplateAuthoring.Parse(Source, [File()], context,
            JsonSerializer.Serialize(new { body = new string('x', 65536) })))).Should().Throw<InvalidDataException>();
        ((Action)(() => PromptTemplateAuthoring.Parse(Source, [File()], context with { Mode = CustomizationAuthoringMode.Discuss },
            "{\"body\":\"Changed\"}"))).Should().Throw<InvalidDataException>();
        var unchanged = PromptTemplateAuthoring.Parse(Source, [File()], context with { Mode = CustomizationAuthoringMode.Discuss },
            JsonSerializer.Serialize(new { body = "  Old request.\r\n" }));
        unchanged.Files.Single().CopyBytes().Should().Equal(File().CopyBytes());
        var legacy = PromptTemplateAuthoring.Parse(Source, [File("1")], context, "{\"body\":\"Literal request\"}");
        legacy.Files.Single().Text.Should().Contain("kora-format: 1");
        ((Action)(() => PromptTemplateAuthoring.Parse(Source, [File("1")], context,
            "{\"body\":\"${input:topic}\"}"))).Should().Throw<InvalidDataException>();
    }
}

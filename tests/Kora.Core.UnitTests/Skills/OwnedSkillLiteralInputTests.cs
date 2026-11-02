using System.Text;
using AwesomeAssertions;
using Kora.Core.Artifacts;
using Kora.Core.Network;
using Kora.Core.Skills;

namespace Kora.Core.UnitTests.Skills;

public sealed class OwnedSkillLiteralInputTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static OwnedDeclarativeSkillDocument Document(string guidance = "Compare ${input:topic} with ${input:topic}.") =>
        OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", guidance,
            guidanceFormat: OwnedSkillGuidanceFormat.RequiredLiteralInputs);

    [Theory]
    [InlineData(OwnedSkillToolSelection.None, "1", "none")]
    [InlineData(OwnedSkillToolSelection.WebPageGet, "2", WebPageCapability.Id)]
    public void OldSchemasRemainExactLiteralGoldenBytes(OwnedSkillToolSelection tool, string format, string declaration)
    {
        const string guidance = "  ${input:topic}\r\n${unknown:thing}\n${file:private}\n";
        var document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", guidance, tool);
        var expected = $"kora-skill-format: {format}\nid: example\nversion: 1.0.0\nname: Example\ndescription: Description\nruntime: local\ntools: {declaration}\nreferences: none\n";
        document.ManifestBytes.ToArray().Should().Equal(Encoding.UTF8.GetBytes(expected));
        document.InstructionBytes.ToArray().Should().Equal(Encoding.UTF8.GetBytes(guidance));
        var reopened = OwnedDeclarativeSkillDocument.Parse(document.ManifestBytes.Span, document.InstructionBytes.Span);
        reopened.InputTemplate.Should().BeNull();
        reopened.Tool.Should().Be(tool);
        reopened.RevisionDigest.Should().Be(document.RevisionDigest);
        reopened.Guidance.Should().Be(guidance);
    }

    [Fact]
    public void V4ObservationsRetainTheirExactDeclarationAndCannotAcquireV3Inputs()
    {
        const string source = "{\"steps\":[{\"tool\":\"application.get_version\",\"input\":{}}]}";
        var document = OwnedDeclarativeSkillDocument.CreateObservationWorkflow("example", "1.0.0",
            "Example", "Description", source);
        var reopened = OwnedDeclarativeSkillDocument.Parse(document.ManifestBytes.Span, document.InstructionBytes.Span);
        reopened.Tool.Should().Be(OwnedSkillToolSelection.ReadOnlyObservations);
        reopened.InputTemplate.Should().BeNull();
        reopened.ManifestBytes.ToArray().Should().Equal(document.ManifestBytes.ToArray());
        reopened.InstructionBytes.ToArray().Should().Equal(Encoding.UTF8.GetBytes(source));
        reopened.RevisionDigest.Should().Be(document.RevisionDigest);
        reopened.ObservationWorkflow!.Steps.Should().ContainSingle();
        reopened.IsAvailableForInvocation.Should().BeFalse();
        var parse = () => OwnedDeclarativeSkillDocument.Parse(
            Encoding.UTF8.GetBytes(document.ManifestText + "inputs: required-literal\n"), document.InstructionBytes.Span);
        parse.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ExplicitV3RoundtripUsesTheExistingSinglePassLiteralContract()
    {
        var document = Document();
        document.ManifestText.Should().Be("kora-skill-format: 3\nid: example\nversion: 1.0.0\nname: Example\ndescription: Description\nruntime: local\ntools: none\nreferences: none\ninputs: required-literal\n");
        var reopened = OwnedDeclarativeSkillDocument.Parse(document.ManifestBytes.Span, document.InstructionBytes.Span);
        reopened.InputTemplate!.RequiredNames.Should().Equal("topic");
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["topic"] = "e\u0301 ${input:other} ${file:secret}" };
        reopened.InputTemplate.Expand(values, Token).Should().Be("Compare e\u0301 ${input:other} ${file:secret} with e\u0301 ${input:other} ${file:secret}.");
        reopened.Guidance.Should().Be(document.Guidance);
        reopened.RevisionDigest.Should().Be(document.RevisionDigest);
        reopened.IsAvailableForInvocation.Should().BeFalse();
    }

    [Theory]
    [InlineData("inputs: required-literal\n", "")]
    [InlineData("inputs: required-literal", "inputs: optional")]
    [InlineData("inputs: required-literal", "parameters: required-literal")]
    [InlineData("references: none", "inputs: required-literal")]
    [InlineData("tools: none", "tools: network.get_web_page")]
    [InlineData("runtime: local", "runtime: hosted")]
    [InlineData("references: none", "references: other.md")]
    [InlineData("kora-skill-format: 3", "kora-skill-format: 1")]
    [InlineData("kora-skill-format: 3", "kora-skill-format: 2")]
    [InlineData("kora-skill-format: 3", "kora-skill-format: 4")]
    [InlineData("kora-skill-format: 3", "kora-skill-format: 5")]
    public void ExplicitFieldsetCannotBeOmittedDuplicatedDowngradedOrAcquireAuthority(string original, string replacement)
    {
        var document = Document();
        var parse = () => OwnedDeclarativeSkillDocument.Parse(
            Encoding.UTF8.GetBytes(document.ManifestText.Replace(original, replacement, StringComparison.Ordinal)), document.InstructionBytes.Span);
        parse.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void VersionedManifestRequiresItsExactLineCountAndFinalNewline()
    {
        var document = Document();
        foreach (var manifest in new[] { document.ManifestText + "\n", document.ManifestText.TrimEnd('\n'), "kora-skill-format: 3\n" })
        {
            ((Action)(() => OwnedDeclarativeSkillDocument.Parse(Encoding.UTF8.GetBytes(manifest), document.InstructionBytes.Span)))
                .Should().Throw<InvalidDataException>();
        }
    }

    [Theory]
    [InlineData("Literal without declared inputs")]
    [InlineData("${input:Bad}")]
    [InlineData("${input:1bad}")]
    [InlineData("${input:bad_name}")]
    [InlineData("${env:secret}")]
    [InlineData("${file:private}")]
    [InlineData("${input:unfinished")]
    [InlineData("$${input:escaped}")]
    public void RequiredInputDeclarationsUseTheAuthoritativeParser(string guidance)
    {
        ((Action)(() => Document(guidance))).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void InputsCannotBeCombinedWithToolsOrUnknownFormatsOrGuidanceReferences()
    {
        ((Action)(() => OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description",
            "${input:topic}", OwnedSkillToolSelection.WebPageGet, OwnedSkillGuidanceFormat.RequiredLiteralInputs)))
            .Should().Throw<InvalidDataException>();
        ((Action)(() => OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description",
            "${input:topic}", guidanceFormat: (OwnedSkillGuidanceFormat)99))).Should().Throw<InvalidDataException>();
        var reference = SharedSkillSnapshot.Parse(Guid.NewGuid(), "SKILL.md",
            Encoding.UTF8.GetBytes("---\nname: example\nversion: 1.0.0\ndescription: Description\nformat: 3\ninputs: required-literal\nreferences: other.md\n---\n${input:topic}"), false);
        reference.IsInstructionCompatible.Should().BeFalse();
        reference.IsReferenceCandidate.Should().BeFalse();
        reference.UnavailableReasons.Should().Contain("unsupported-reference-format").And.Contain("unsupported-metadata");
    }

    [Fact]
    public void RepeatedValuesRespectExactCumulativeUtf16AndUtf8Bounds()
    {
        var document = Document(string.Concat(Enumerable.Repeat("${input:topic}", 8)));
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["topic"] = new string('\u754c', 512) };
        var expanded = document.InputTemplate!.Expand(values, Token);
        expanded.Length.Should().Be(4096);
        Encoding.UTF8.GetByteCount(expanded).Should().Be(12288).And.BeLessThanOrEqualTo(OwnedPromptTemplate.MaximumRequestUtf8Bytes);
        var tooMuch = Document(document.Guidance + "x");
        ((Action)(() => tooMuch.InputTemplate!.Expand(values, Token))).Should().Throw<InvalidDataException>();
        values["topic"] += "x";
        ((Action)(() => document.InputTemplate.Expand(values, Token))).Should().Throw<InvalidDataException>();
    }
}

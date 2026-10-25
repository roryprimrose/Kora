using AwesomeAssertions;
using Kora.Core.Agents;
using Kora.Core.Artifacts;

namespace Kora.Core.UnitTests.Agents;

public sealed class AgentSharedSkillReferenceTests
{
    private const string Identity = "shared-profile:1234567890abcdef1234567890abcdef:example@1.0.0";

    [Fact]
    public void FormatThreePinsOneQualifiedSkillWithoutOwnedReferencesOrTools()
    {
        var profile = Parse();
        profile.SharedSkill!.Identity.Should().Be(Identity);
        profile.SharedSkill.SourceId.ToString("N").Should().Be("1234567890abcdef1234567890abcdef");
        profile.References.Should().BeEmpty();
        profile.Tools.Should().BeEmpty();
        profile.MaximumCalls.Should().Be(0);
        profile.MaximumRounds.Should().Be(1);
        profile.MaximumInputUtf8Bytes.Should().Be(32768);
        new AgentSharedSkillReference("shared-profile:1234567890abcdef1234567890abcdef:example-1@9999.1.0")
            .Identity.Should().EndWith("example-1@9999.1.0");
    }

    [Theory]
    [InlineData("")]
    [InlineData("none")]
    [InlineData("wrong:1234567890abcdef1234567890abcdef:example@1.0.0")]
    [InlineData("shared-profile:invalid:example@1.0.0")]
    [InlineData("example")]
    [InlineData("disk.kora-profile.instruction.example")]
    [InlineData("shared-profile:00000000000000000000000000000000:example@1.0.0")]
    [InlineData("shared-profile:12345678-90ab-cdef-1234-567890abcdef:example@1.0.0")]
    [InlineData("shared-profile:1234567890ABCDEF1234567890ABCDEF:example@1.0.0")]
    [InlineData("shared-profile:1234567890abcdef1234567890abcdef:example@latest")]
    [InlineData("shared-profile:1234567890abcdef1234567890abcdef:Example@1.0.0")]
    [InlineData("shared-profile:1234567890abcdef1234567890abcdef:{example@1.0.0")]
    [InlineData("shared-profile:1234567890abcdef1234567890abcdef:example{@1.0.0")]
    [InlineData("shared-profile:1234567890abcdef1234567890abcdef:example_@1.0.0")]
    [InlineData("shared-profile:1234567890abcdef1234567890abcdef:example.@1.0.0")]
    [InlineData("shared-profile:1234567890abcdef1234567890abcdef:a--b@1.0.0")]
    [InlineData("shared-profile:1234567890abcdef1234567890abcdef:a-@1.0.0")]
    [InlineData("shared-profile:1234567890abcdef1234567890abcdef:a/b@1.0.0")]
    [InlineData("shared-profile:1234567890abcdef1234567890abcdef:example@01.0.0")]
    [InlineData("shared-profile:1234567890abcdef1234567890abcdef:example@1.0")]
    [InlineData("shared-profile:1234567890abcdef1234567890abcdef:example@1.0.0,other")]
    [InlineData("shared-profile:1234567890abcdef1234567890abcdef:example@1.0.0@2.0.0")]
    [InlineData("shared-profile:1234567890abcdef1234567890abcdef:example")]
    [InlineData("shared-profile:1234567890abcdef1234567890abcdef:@1.0.0")]
    [InlineData("shared-profile:1234567890abcdef1234567890abcdef:example@1..0")]
    [InlineData("shared-profile:1234567890abcdef1234567890abcdef:example@12345.0.0")]
    [InlineData("shared-profile:1234567890abcdef1234567890abcdef:example@x.0.0")]
    [InlineData("shared-profile:1234567890abcdef1234567890abcdef:example@1.0.0:extra")]
    public void UnsupportedUnqualifiedAndMixedDeclarationsAreRejected(string identity)
    {
        var act = () => new AgentSharedSkillReference(identity);
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void MissingAndOversizedSkillIdentityAreRejected()
    {
        var missing = () => new AgentSharedSkillReference(null!);
        missing.Should().Throw<ArgumentNullException>();
        var oversized = () => new AgentSharedSkillReference($"shared-profile:1234567890abcdef1234567890abcdef:{new string('a', 65)}@1.0.0");
        oversized.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("runtime: local", "runtime: hosted")]
    [InlineData("tools: none", "tools: application.get_version")]
    [InlineData("maximum-calls: 0", "maximum-calls: 1")]
    [InlineData("input: text", "input: files")]
    [InlineData("output: text", "output: json")]
    [InlineData("runtime: local", "unknown: local")]
    public void FormatThreeCannotExpandExecution(string before, string after)
    {
        var act = () => Parse(before, after);
        act.Should().Throw<InvalidDataException>();
    }

    private static BoundedAgentProfile Parse(string before = "unused", string after = "unused") =>
        BoundedAgentProfile.Parse(ArtifactFileDocument.Parse(ArtifactFileDocument.Encode($"""
            ---
            name: example-agent
            description: Local guidance
            kora-agent-format: 3
            runtime: local
            input: text
            output: text
            references: {Identity}
            tools: none
            maximum-calls: 0
            maximum-rounds: 1
            maximum-input-bytes: 32768
            maximum-output-bytes: 4096
            deadline-ms: 15000
            ---
            Explain only the original request.
            """.Replace(before, after, StringComparison.Ordinal), false), ArtifactKind.Agent, "selected-user-source", "example-agent"));
}

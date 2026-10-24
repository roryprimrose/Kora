using AwesomeAssertions;

using Kora.Core.Agents;
using Kora.Core.Artifacts;

namespace Kora.Core.UnitTests.Agents;

public sealed class AgentInstructionBundleTests
{
    [Fact]
    public void BundlePinsCompleteSourceBytesAndEnforcesExactCombinedByteThreshold()
    {
        var profile = Profile();
        var empty = Instruction("x");
        var bodyLength = AgentInstructionBundle.MaximumSourceBytes - profile.Document.ByteCount - empty.Document.ByteCount + 1;
        var exact = Instruction(new string('x', bodyLength));
        var bundle = new AgentInstructionBundle(profile, [exact]);
        bundle.Instructions.Single().Document.ByteCount.Should().Be(AgentInstructionBundle.MaximumSourceBytes - profile.Document.ByteCount);
        bundle.Profile.Should().BeSameAs(profile);
        bundle.Instructions.Single().Should().BeSameAs(exact);
        var bytes = exact.Document.CopyBytes();
        bytes[0] = 0;
        bundle.Instructions.Single().Document.CopyBytes()[0].Should().Be((byte)'-');
        Action oversized = () => _ = new AgentInstructionBundle(profile, [Instruction(new string('x', bodyLength + 1))]);
        oversized.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void BundleRejectsMissingForeignWrongKindAndNullSources()
    {
        var profile = Profile();
        foreach (var entries in new OwnedInstructionSnapshot[][]
        {
            [], [Instruction("body", name: "other")], [Instruction("body", source: "personal")],
            [Instruction("body", kind: ArtifactKind.Prompt)], [null!],
        })
        {
            Action create = () => _ = new AgentInstructionBundle(profile, entries);
            create.Should().Throw<InvalidDataException>();
        }
        Action nullProfile = () => _ = new AgentInstructionBundle(null!, []);
        nullProfile.Should().Throw<ArgumentNullException>();
        Action nullSources = () => _ = new AgentInstructionBundle(profile, null!);
        nullSources.Should().Throw<ArgumentNullException>();
    }

    private static OwnedInstructionSnapshot Instruction(string body, string name = "example",
        string source = "kora-profile", ArtifactKind kind = ArtifactKind.Instruction) =>
        new(name, new string('a', 48), new string('b', 48),
            ArtifactFileDocument.Parse(ArtifactFileDocument.Encode($"---\nkora-format: 1\nname: {name}\ndescription: Test\n---\n{body}", false),
                kind, source, name));

    private static BoundedAgentProfile Profile() => BoundedAgentProfile.Parse(ArtifactFileDocument.Parse(
        ArtifactFileDocument.Encode("""
        ---
        name: agent
        description: Test
        kora-agent-format: 2
        runtime: local
        input: text
        output: text
        references: disk.kora-profile.instruction.example
        tools: none
        maximum-calls: 0
        maximum-rounds: 1
        maximum-input-bytes: 32768
        maximum-output-bytes: 4096
        deadline-ms: 15000
        ---
        Original role.
        """, false), ArtifactKind.Agent, "selected-user-source", "agent"));
}

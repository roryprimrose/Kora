using System.Text;

using AwesomeAssertions;
using Kora.Application.Definitions;
using Kora.Core.Agents;
using Kora.Core.Customizations;

namespace Kora.Application.UnitTests.Definitions;

public sealed class CopilotCustomizationWorkflowTests
{
    [Theory]
    [InlineData("tools")]
    [InlineData("model")]
    [InlineData("reference")]
    [InlineData("script")]
    [InlineData("companion")]
    public void UnsupportedSkillRequirementsAreRefusedInsteadOfDropped(string requirement)
    {
        var source = new CustomizationSource("test.skill", CustomizationKind.Skill, CustomizationScope.User,
            "reviewed", Path.GetFullPath(Path.Combine("synthetic", "reviewed")), "SKILL.md");
        var metadata = requirement is "tools" ? "allowed-tools: shell\n" : requirement is "model" ? "model: other\n" : string.Empty;
        var body = requirement is "reference" ? "Read [reference](notes.md)." : requirement is "script" ? "Run action.ps1" : "Plain instructions.";
        List<CustomizationFile> files = [new("SKILL.md", Encoding.UTF8.GetBytes(
            "---\nname: reviewed\nversion: 1.0.0\ndescription: Reviewed\n" + metadata + "---\n" + body))];
        if (requirement is "companion") { files.Add(new("notes.md", Encoding.UTF8.GetBytes("extra"))); }
        Action parse = () => CopilotCustomizationSelection.Parse(new(source, "revision", files));
        parse.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void SeparateHostedCopyPreservesOriginalLocalProfileWithoutRewritingItsSource()
    {
        var document = OwnedAgentProfileDocument.ProposeToolFree("reviewed", string.Empty, "Reviewed", "Use brief paragraphs.").Document;
        var source = new CustomizationSource("test.agent", CustomizationKind.Agent, CustomizationScope.Kora,
            "reviewed", Path.GetFullPath(Path.Combine("synthetic", "reviewed.agent.md")), "reviewed.agent.md");
        var snapshot = new CustomizationSnapshot(source, "revision", [new(source.PrimaryFile, document.CopyBytes())]);
        var selection = CopilotCustomizationSelection.Parse(snapshot);
        selection.SourceText.Should().Be(document.Text).And.Contain("runtime: local");
        selection.Instructions.Should().Be(document.Body);
        snapshot.Files[0].CopyBytes().Should().Equal(document.CopyBytes());
    }

    [Theory]
    [InlineData("bom")]
    [InlineData("recovery")]
    [InlineData("unavailable")]
    [InlineData("wrong-kind")]
    [InlineData("metadata")]
    [InlineData("active-agent")]
    [InlineData("tools-agent")]
    [InlineData("reference-agent")]
    public void CompleteSourcesWithUnreviewableOrUnsupportedNativeRequirementsAreRefused(string fault)
    {
        var agent = fault.EndsWith("-agent", StringComparison.Ordinal);
        var source = new CustomizationSource("test.source",
            agent ? CustomizationKind.Agent : fault is "wrong-kind" ? CustomizationKind.Prompt : CustomizationKind.Skill,
            CustomizationScope.User, "reviewed", Path.GetFullPath(Path.Combine("synthetic", "reviewed.md")), "reviewed.md",
            UnavailableReason: fault is "unavailable" ? "source-unavailable" : null);
        var text = agent
            ? OwnedAgentProfileDocument.ProposeToolFree("reviewed", string.Empty, "Reviewed",
                fault is "active-agent" ? "Run action.ps1" : "Plain instructions.").Document.Text
            : "---\nname: reviewed\nversion: 1.0.0\ndescription: Reviewed\n"
                + (fault is "metadata" ? "disable-model-invocation: true\n" : string.Empty) + "---\nPlain instructions.";
        if (fault is "tools-agent")
        {
            text = text.Replace("tools: none", "tools: application.get_version", StringComparison.Ordinal)
                .Replace("maximum-calls: 0", "maximum-calls: 1", StringComparison.Ordinal);
        }
        if (fault is "reference-agent")
        {
            text = text.Replace("kora-agent-format: 1", "kora-agent-format: 2", StringComparison.Ordinal)
                .Replace("references: none", "references: disk.kora-profile.instruction.reviewed", StringComparison.Ordinal);
        }
        var bytes = Encoding.UTF8.GetBytes(text);
        var file = new CustomizationFile(source.PrimaryFile, fault is "bom" ? [0xef, 0xbb, 0xbf, .. bytes] : bytes);
        var snapshot = new CustomizationSnapshot(source, "revision", [file], fault is "recovery" ? "recovery-only" : null);
        Action parse = () => CopilotCustomizationSelection.Parse(snapshot);
        parse.Should().Throw<InvalidDataException>();
    }

    internal sealed class SourceStore : ICustomizationStore
    {
        internal SourceStore()
        {
            var document = OwnedAgentProfileDocument.ProposeToolFree("reviewed", string.Empty, "Reviewed", "Plain instructions.").Document;
            Source = new("test.agent", CustomizationKind.Agent, CustomizationScope.Kora, "reviewed",
                Path.GetFullPath(Path.Combine("synthetic", "reviewed.agent.md")), "reviewed.agent.md");
            Snapshot = new(Source, "revision", [new(Source.PrimaryFile, document.CopyBytes())]);
        }
        internal CustomizationSource Source { get; set; }
        internal CustomizationSnapshot Snapshot { get; set; }
        internal int Reads { get; private set; }
        internal Action? OnRead { get; set; }
        internal Task<CustomizationSnapshot>? PendingRead { get; set; }
        public InstructionOriginalRegistration? RegisteredInstruction => null;
        public bool IsInstructionRegistered(CustomizationSource source) => false;
        public void WithdrawInstruction(CustomizationSource source) => throw new NotSupportedException();
        public Task<InstructionOriginalRegistration> PrepareInstructionRegistration(CustomizationSource source, CancellationToken token) => throw new NotSupportedException();
        public Task RegisterInstruction(InstructionOriginalRegistration proposal, Func<bool> eligible, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<CustomizationSource>> List(CancellationToken token) => Task.FromResult<IReadOnlyList<CustomizationSource>>([Source]);
        public Task<CustomizationSource> Create(CustomizationKind kind, CustomizationScope scope, string name, CancellationToken token) => throw new NotSupportedException();
        public Task<CustomizationSnapshot> Read(CustomizationSource source, CancellationToken token)
        {
            Reads++;
            OnRead?.Invoke();
            return PendingRead ?? Task.FromResult(Snapshot);
        }
        public Task<CustomizationSnapshot> ReadRecovery(CustomizationSource source, CancellationToken token) => throw new NotSupportedException();
        public Task<CustomizationSnapshot> Save(CustomizationSource source, CustomizationSnapshot? expected, IReadOnlyList<CustomizationFile> files,
            Func<bool> eligible, Func<Func<CustomizationSnapshot>, Task<CustomizationSnapshot>> commit, CancellationToken token,
            CustomizationSnapshot? copiedFrom = null) => throw new NotSupportedException();
        public Task Delete(CustomizationSnapshot reviewed, Func<bool> eligible, Func<Action, Task> commit, CancellationToken token) => throw new NotSupportedException();
    }
}

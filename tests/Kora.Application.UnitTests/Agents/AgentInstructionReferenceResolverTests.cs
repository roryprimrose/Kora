using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Application.Definitions;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Agents;
using Kora.Core.Artifacts;

namespace Kora.Application.UnitTests.Agents;

public sealed class AgentInstructionReferenceResolverTests
{
    [Fact]
    public async Task CandidateReviewReadsNothingAndResolutionPinsExactBytesWithSingleUseConsent()
    {
        var f = new Fixture();
        using var review = f.Resolver.Review(Profile(), () => f.Revocations++);
        review.Candidates.Should().Equal(f.Catalogue.Candidates);
        review.Bundle.Should().BeNull();
        f.Store.Reads.Should().Be(0);
        await review.Invoking(value => value.ValidateAsync(() => true, Token)).Should().ThrowAsync<InvalidOperationException>();
        await review.ResolveAsync(() => true, Token);
        review.Bundle!.Instructions.Single().Should().BeSameAs(f.Store.Value);
        review.IsCurrent().Should().BeTrue();
        f.Store.Reads.Should().Be(2);
        await review.Invoking(value => value.ResolveAsync(() => true, Token)).Should().ThrowAsync<InvalidOperationException>();
        await review.ValidateAsync(() => true, Token);
        f.Revocations.Should().Be(0);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("disabled")]
    [InlineData("reload")]
    [InlineData("revision")]
    [InlineData("wrong-kind")]
    [InlineData("wrong-source")]
    [InlineData("candidate-kind")]
    [InlineData("candidate-source")]
    [InlineData("candidate-name")]
    [InlineData("candidate-revision")]
    public void UnavailableCandidateCannotBeReviewedOrRead(string fault)
    {
        var f = new Fixture();
        if (fault is "disabled") { f.Revisions.DisableExact(f.Store.Value!, f.Catalogue); }
        if (fault is "reload") { f.Catalogue.ReportReloadFailure(new IOException("reload")); }
        if (fault is "revision") { f.Catalogue.Update(items => items.Select(item => item with { OwnedInstructionRevision = "invalid" }).ToArray()); }
        if (fault is "candidate-kind" or "candidate-source")
        {
            var original = f.Catalogue.Candidates.Single();
            var bad = new ArtifactDefinition(original.Id, fault is "candidate-kind" ? ArtifactKind.Prompt : ArtifactKind.Instruction,
                original.Name, original.Description, original.CommandName, original.SpokenNames,
                fault is "candidate-source" ? "personal" : "kora-profile", original.Version, original.Digest, original.Content)
            { OwnedInstructionName = original.OwnedInstructionName, OwnedInstructionRevision = original.OwnedInstructionRevision };
            f.Catalogue.Publish([bad]);
        }
        if (fault is "candidate-name") { f.Catalogue.Update(items => items.Select(item => item with { OwnedInstructionName = null }).ToArray()); }
        if (fault is "candidate-revision") { f.Catalogue.Update(items => items.Select(item => item with { OwnedInstructionRevision = null }).ToArray()); }
        Action act = () => f.Resolver.Review(fault switch
        {
            "missing" => Profile("disk.kora-profile.instruction.absent"),
            "wrong-kind" => Profile("disk.kora-profile.prompt.example"),
            "wrong-source" => Profile("disk.personal.instruction.example"),
            _ => Profile(),
        }, () => f.Revocations++);
        act.Should().Throw<Exception>();
        f.Store.Reads.Should().Be(0);
    }

    [Theory]
    [InlineData("absence")]
    [InlineData("identity")]
    [InlineData("bytes")]
    [InlineData("legacy")]
    [InlineData("hold")]
    [InlineData("access")]
    public async Task ResolutionCannotSubstituteChangedSourceOrAuthority(string fault)
    {
        var f = new Fixture();
        using var review = f.Resolver.Review(Profile(), () => f.Revocations++);
        if (fault is "absence") { f.Store.Value = null; }
        if (fault is "identity") { f.Store.Value = f.Store.Value! with { FileIdentity = new string('c', 48) }; }
        if (fault is "bytes") { f.Store.Value = Snapshot("Changed"); }
        if (fault is "legacy") { f.Store.Value = Snapshot(native: false); }
        if (fault is "hold") { f.Revisions.Hold("example"); }
        await review.Invoking(value => value.ResolveAsync(() => fault is not "access", Token)).Should().ThrowAsync<Exception>();
        review.IsCurrent().Should().BeFalse();
        f.Revocations.Should().Be(1);
        if (fault is "access") { f.Store.Reads.Should().Be(0); }
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("hold")]
    [InlineData("epoch")]
    [InlineData("reload")]
    [InlineData("replacement")]
    public async Task BundleRevocationNeverUsesCapturedAnswerExceptionOrRevivesAfterEnable(string fault)
    {
        var f = new Fixture();
        using var review = f.Resolver.Review(Profile(), () => f.Revocations++);
        await review.ResolveAsync(() => true, Token);
        if (fault is "disable") { f.Revisions.DisableExact(f.Store.Value!, f.Catalogue); }
        if (fault is "hold") { f.Revisions.HoldExact(f.Store.Value!, f.Catalogue); }
        if (fault is "epoch") { f.Revisions.SuspendReviewedUse(); }
        if (fault is "reload") { f.Catalogue.ReportReloadFailure(new IOException("reload")); }
        if (fault is "replacement") { f.Catalogue.Publish([f.Store.Value!.Document.Definition with { OwnedInstructionRevision = f.Store.Value.Revision }]); }
        review.IsCurrent().Should().BeFalse();
        await review.Invoking(value => value.ValidateAsync(() => true, Token)).Should().ThrowAsync<InvalidOperationException>();
        if (fault is "disable")
        {
            f.Revisions.CompleteActivation(f.Store.Value!, f.Revisions.PrepareActivation(f.Store.Value!), f.Catalogue);
            review.IsCurrent().Should().BeFalse();
            f.Revocations.Should().Be(1);
        }
    }

    [Fact]
    public async Task CancellationIgnoringReadRemainsOwnedUntilItActuallyReturnsAndCannotPublishBundle()
    {
        var f = new Fixture();
        var gate = new TaskCompletionSource<OwnedInstructionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.Pending = gate.Task;
        using var review = f.Resolver.Review(Profile(), () => f.Revocations++);
        using var cancellation = new CancellationTokenSource();
        var pending = review.ResolveAsync(() => true, cancellation.Token);
        f.Store.Reads.Should().Be(1);
        await cancellation.CancelAsync();
        review.Dispose();
        pending.IsCompleted.Should().BeFalse();
        gate.SetResult(f.Store.Value);
        await pending.Invoking(value => value).Should().ThrowAsync<OperationCanceledException>();
        review.Bundle.Should().BeNull();
        f.Store.Reads.Should().Be(1);
        f.Revocations.Should().Be(1);
    }

    [Fact]
    public void UnrelatedCatalogueChangesPreserveReviewAndNullOrUnreferencedProfilesAreRejected()
    {
        var f = new Fixture();
        using var review = f.Resolver.Review(Profile(), () => f.Revocations++);
        var unrelated = Snapshot("Unrelated").Document.Definition;
        unrelated = new("unrelated", unrelated.Kind, "unrelated", unrelated.Description, "unrelated", ["unrelated"],
            unrelated.Source, unrelated.Version, unrelated.Digest, unrelated.Content);
        f.Catalogue.Update(items => [.. items, unrelated]);
        review.IsCurrent().Should().BeTrue();
        f.Revocations.Should().Be(0);
        Action nullProfile = () => f.Resolver.Review(null!, () => { });
        nullProfile.Should().Throw<ArgumentNullException>();
        Action nullRevoke = () => f.Resolver.Review(Profile(), null!);
        nullRevoke.Should().Throw<ArgumentNullException>();
        var v1 = BoundedAgentProfile.Parse(ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            Profile().Document.Text.Replace("kora-agent-format: 2", "kora-agent-format: 1", StringComparison.Ordinal)
                .Replace("references: disk.kora-profile.instruction.example", "references: none", StringComparison.Ordinal), false),
            ArtifactKind.Agent, "selected-user-source", "role"));
        Action unreferenced = () => f.Resolver.Review(v1, () => { });
        unreferenced.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TransientReloadFailureCannotReviveBundleEvenWhenRecoveryReusesTheSameCandidates()
    {
        var f = new Fixture();
        AgentInstructionReferenceReview? observed = null;
        f.Catalogue.ReloadFailed += (_, _) =>
        {
            observed!.IsCurrent().Should().BeFalse();
            f.Catalogue.Publish(f.Catalogue.Candidates);
            observed.IsCurrent().Should().BeFalse();
        };
        using var review = f.Resolver.Review(Profile(), () => f.Revocations++);
        observed = review;
        f.Catalogue.ReportReloadFailure(new IOException("transient"));
        review.IsCurrent().Should().BeFalse();
        f.Revocations.Should().Be(1);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static OwnedInstructionSnapshot Snapshot(string body = "Exact instruction.", bool native = true) =>
        new("example", new string('a', 48), new string('b', 48),
            ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                $"---\n{(native ? "kora-format: 1\n" : "")}name: example\ndescription: Test\n---\n{body}", false),
                ArtifactKind.Instruction, "kora-profile", "example"));

    private static BoundedAgentProfile Profile(string id = "disk.kora-profile.instruction.example") =>
        BoundedAgentProfile.Parse(ArtifactFileDocument.Parse(ArtifactFileDocument.Encode($"""
            ---
            name: role
            description: Role
            kora-agent-format: 2
            runtime: local
            input: text
            output: text
            references: {id}
            tools: none
            maximum-calls: 0
            maximum-rounds: 1
            maximum-input-bytes: 32768
            maximum-output-bytes: 4096
            deadline-ms: 15000
            ---
            Role guidance.
            """, false), ArtifactKind.Agent, "selected-user-source", "role"));

    internal sealed class Fixture
    {
        internal Store Store { get; } = new();
        internal InstructionRevisionCatalogue Revisions { get; } = new(new LocalSharedSkillPreferencesTests.Store());
        internal ArtifactCatalogue Catalogue { get; }
        internal AgentInstructionReferenceResolver Resolver { get; }
        internal int Revocations { get; set; }
        internal Fixture()
        {
            var source = Store.Value!;
            Catalogue = new(Revisions.Reconcile([source.Document.Definition with { OwnedInstructionRevision = source.Revision }]));
            Revisions.CompleteActivation(source, Revisions.PrepareActivation(source), Catalogue);
            Resolver = new(Catalogue, Store, Revisions);
        }
    }

    internal sealed class Store : IOwnedInstructionStore
    {
        internal OwnedInstructionSnapshot? Value { get; set; } = Snapshot();
        internal Task<OwnedInstructionSnapshot?>? Pending { get; set; }
        internal Action? BeforeRead { get; set; }
        internal int Reads { get; private set; }
        public Task<OwnedInstructionSnapshot?> Read(string name, CancellationToken token)
        {
            name.Should().Be("example");
            Reads++;
            BeforeRead?.Invoke();
            return Pending ?? Task.FromResult(Value);
        }
        public Task<IReadOnlyList<string>> List(CancellationToken token) => throw new InvalidOperationException("No discovery.");
        public Task<InstructionWriteResult> Write(string name, OwnedInstructionSnapshot? expected,
            ArtifactFileDocument? replacement, Func<bool> eligible,
            Func<Func<InstructionWriteResult>, Task<InstructionWriteResult>> commit, CancellationToken token) =>
            throw new InvalidOperationException("No writes or activation.");
    }
}

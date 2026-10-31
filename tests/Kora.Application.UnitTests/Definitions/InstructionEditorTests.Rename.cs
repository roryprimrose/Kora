using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Application.Agents;
using Kora.Core.Agents;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Commands;

namespace Kora.Application.UnitTests.Definitions;

public sealed partial class InstructionEditorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenameReconcilesOldIdentityUnavailableAndNewDisabledWithoutReferenceOrUseTransfer(bool recover)
    {
        await using var f = new Fixture();
        var source = f.Files.Current!;
        await f.Actions.Activate(source, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        var agent = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            $"---\nname: reader\ndescription: Dependent\nkora-agent-format: 2\nreferences: {source.Document.Definition.Id}\n---\nAgent body",
            false), ArtifactKind.Agent, "user-profile", "reader").Definition;
        f.Catalogue.Publish(f.Revisions.Reconcile([Candidate(source), agent]));
        var proposal = new OwnedInstructionRename(source, "renamed");
        if (recover)
        {
            f.Files.PendingRename = new(proposal, "observed", OwnedInstructionRenamePhase.Prepared);
            f.Catalogue.ReportReloadFailure(new IOException("Held recovery"));
        }
        var review = recover ? await f.Actions.ReviewRenameRecovery(f.Eligible, CancellationToken.None)
            : await f.Actions.ReviewRename(source, "renamed", f.Eligible, CancellationToken.None);
        review!.Proposal.Source.Document.CopyBytes().Should().Equal(source.Document.CopyBytes());
        review.Dependents.Should().ContainSingle().Which.Should().BeSameAs(agent);
        review.AuditTarget.Should().StartWith("definition.owned-rename.").And.NotContain("renamed").And.NotContain("Original");
        var saved = await f.Actions.Rename(review, (_, _) => Task.FromResult(true), (before, after) =>
        {
            before.Should().BeSameAs(review.Proposal.Source);
            f.Catalogue.Publish(f.Revisions.Reconcile([agent, Candidate(after)]));
            return true;
        }, f.Eligible, CancellationToken.None);
        saved!.Name.Should().Be("renamed");
        f.Files.Current.Should().BeNull();
        f.Catalogue.Candidates.Should().NotContain(item => item.OwnedInstructionName == "example");
        f.Catalogue.Candidates.Single(item => item.OwnedInstructionName is "renamed").IsUseEnabled.Should().BeFalse();
        f.Revisions.IsEnabled(saved).Should().BeFalse();
        f.Catalogue.Candidates.Single(item => item.Kind == ArtifactKind.Agent).Should().BeSameAs(agent);
        agent.DeclaredInstructionReferences.Should().Equal(source.Document.Definition.Id);
        new ArtifactCommandRouter(f.Catalogue).Match("/instruction renamed", "Kora").Invocation.Should().BeNull();
        f.Files.Renames.Should().Be(1);
        f.Files.Recoveries.Should().Be(recover ? 1 : 0);
        f.Audit.Events.Should().Contain(item => item.ActionId == (recover
            ? "definition.instruction.recover-rename" : "definition.instruction.rename") && item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task CancelRenameReviewDoesNotHoldWriteReconcileOrTransferAnything()
    {
        await using var f = new Fixture();
        var source = f.Files.Current!;
        await f.Actions.Activate(source, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        var review = await f.Actions.ReviewRename(source, "renamed", f.Eligible, CancellationToken.None);
        review.Recovery.Should().BeNull();
        review.Dependents.Should().BeEmpty();
        var result = await f.Actions.Rename(review, (_, _) => Task.FromResult(false), (_, _) => throw new InvalidOperationException("No reconciliation"),
            f.Eligible, CancellationToken.None);
        result.Should().BeNull();
        f.Files.Renames.Should().Be(0);
        f.Files.Current.Should().BeSameAs(source);
        f.Revisions.IsEnabled(source).Should().BeTrue();
        f.Audit.Events.Should().NotContain(item => item.ActionId.EndsWith(".rename", StringComparison.Ordinal));
        (await f.Actions.ReviewRenameRecovery(f.Eligible, CancellationToken.None)).Should().BeNull();
    }

    [Theory]
    [InlineData("help", "reserved control")]
    [InlineData("instructions", "reserved control")]
    [InlineData("local-models", "built-in command")]
    public async Task RejectedRenameDestinationPreservesExactSourceAndReviewedUseWithoutReadsOrMutation(string destination, string reason)
    {
        await using var f = new Fixture();
        var source = f.Files.Current!;
        await f.Actions.Activate(source, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        var candidates = f.Catalogue.Candidates;
        var reads = f.Files.Reads;
        var audit = f.Audit.Events.ToArray();
        var preferences = f.State.ReadText("instruction-revisions.json");

        var operation = () => f.Actions.ReviewRename(source, destination, f.Eligible, CancellationToken.None);

        await operation.Should().ThrowAsync<InvalidDataException>().WithMessage("*" + reason + "*");
        f.Files.Current.Should().BeSameAs(source);
        f.Files.Reads.Should().Be(reads);
        f.Files.Writes.Should().Be(0);
        f.Files.Renames.Should().Be(0);
        f.Files.Recoveries.Should().Be(0);
        f.Files.Copies.Should().BeEmpty();
        f.Files.PendingRename.Should().BeNull();
        f.Catalogue.Candidates.Should().BeSameAs(candidates);
        f.Revisions.IsEnabled(source).Should().BeTrue();
        f.State.ReadText("instruction-revisions.json").Should().Be(preferences);
        f.Audit.Events.Should().Equal(audit);
    }

    [Theory]
    [InlineData("source-missing")]
    [InlineData("source-revision")]
    [InlineData("source-bytes")]
    [InlineData("occupied")]
    [InlineData("privacy")]
    [InlineData("reload")]
    [InlineData("reserved")]
    [InlineData("collision")]
    [InlineData("observed-id")]
    [InlineData("excluded")]
    public async Task RenamePreparationRejectsStaleSourcesAndNamespaceCollisions(string failure)
    {
        await using var f = new Fixture();
        var source = f.Files.Current!;
        switch (failure)
        {
            case "source-missing": f.Files.Current = null; break;
            case "source-revision": f.Files.Current = source with { FileIdentity = "different" }; break;
            case "source-bytes": f.Files.Current = source with { Document = Snapshot("Changed").Document }; break;
            case "occupied": f.Files.Copies["renamed"] = source; break;
            case "privacy": f.Files.BeforeRead = () => f.Open = false; break;
            case "reload": f.Catalogue.ReportReloadFailure(new IOException("Unavailable")); break;
            case "excluded": f.Catalogue.Publish([Snapshot(kind: ArtifactKind.Prompt).Document.Definition]); break;
            case "collision":
                f.Catalogue.Update(items => [.. items, ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                    "---\nname: renamed\ndescription: Collision\n---\nBody", false), ArtifactKind.Prompt, "other-source", "renamed").Definition]);
                break;
            case "observed-id":
                f.Catalogue.Update(items => [.. items, new OwnedInstructionRename(source, "renamed").Proposed.Definition]);
                break;
        }
        var operation = () => f.Actions.ReviewRename(source, failure is "reserved" ? "help" : "renamed", f.Eligible, CancellationToken.None);
        await operation.Should().ThrowAsync<Exception>();
        f.Files.Renames.Should().Be(0);
    }

    [Theory]
    [InlineData("before-confirm")]
    [InlineData("during-confirm")]
    [InlineData("privacy")]
    [InlineData("session")]
    public async Task ChangedCatalogueOrOriginalAuthorityInvalidatesRenameConfirmation(string failure)
    {
        await using var f = new Fixture();
        var review = await f.Actions.ReviewRename(f.Files.Current!, "renamed", f.Eligible, CancellationToken.None);
        void Change() => f.Catalogue.Update(items => items.Select(item => item with { PreservesCapturedUse = true }).ToArray());
        if (failure is "before-confirm") { Change(); }
        var operation = () => f.Actions.Rename(review, (_, _) =>
        {
            if (failure is "during-confirm") { Change(); }
            if (failure is "privacy") { f.Open = false; }
            if (failure is "session") { f.Control.Authority = null; }
            return Task.FromResult(true);
        }, (_, _) => true, f.Eligible, CancellationToken.None);
        await operation.Should().ThrowAsync<InvalidOperationException>();
        f.Files.Renames.Should().Be(0);
        f.Files.Current.Should().NotBeNull();
    }

    [Theory]
    [InlineData("storage")]
    [InlineData("cancel")]
    [InlineData("uncertain")]
    [InlineData("late-privacy")]
    [InlineData("reconcile")]
    [InlineData("audit")]
    [InlineData("reconcile-uncertain")]
    public async Task RenameFailureKeepsBothUseIdentitiesHeldAndNeverClaimsSuccess(string failure)
    {
        await using var f = new Fixture();
        var source = f.Files.Current!;
        await f.Actions.Activate(source, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        var review = await f.Actions.ReviewRename(source, "renamed", f.Eligible, CancellationToken.None);
        f.Files.RenameFailure = failure is "storage" or "cancel" or "uncertain" ? failure : null;
        if (failure is "late-privacy") { f.Files.AfterRename = () => f.Open = false; }
        if (failure is "audit")
        {
            f.Audit.BeforeWrite = item =>
            {
                if (item.ActionId.EndsWith(".rename", StringComparison.Ordinal) && item.Outcome == SecurityAuditOutcome.Succeeded)
                { throw new IOException("Terminal audit failed"); }
            };
        }
        var operation = () => f.Actions.Rename(review, (_, _) => Task.FromResult(true), (_, saved) =>
        {
            if (failure is "reconcile") { return false; }
            if (failure is "reconcile-uncertain") { throw new InstructionRenameUnconfirmedException(new IOException("Unconfirmed")); }
            f.Catalogue.Publish(f.Revisions.Reconcile([Candidate(saved)]));
            return true;
        }, f.Eligible, CancellationToken.None);
        await operation.Should().ThrowAsync<Exception>();
        f.Revisions.IsEnabled(source).Should().BeFalse();
        f.Catalogue.Artifacts.Should().BeEmpty();
        f.Audit.Events.Should().NotContain(item => item.ActionId.EndsWith(".rename", StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Succeeded);
        var outcome = failure is "storage" ? SecurityAuditOutcome.Failed
            : failure is "cancel" ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Unknown;
        f.Audit.Events.Should().Contain(item => item.ActionId.EndsWith(".rename", StringComparison.Ordinal) && item.Outcome == outcome);
    }

    [Fact]
    public async Task PromptRenameIsExplicitlyUnavailableAndRecoveryCannotResumeForeignAuthority()
    {
        await using var prompt = new Fixture(ArtifactKind.Prompt);
        var unavailable = () => prompt.Actions.ReviewRename(prompt.Files.Current!, "renamed", prompt.Eligible, CancellationToken.None);
        await unavailable.Should().ThrowAsync<InvalidOperationException>();
        var recovery = () => prompt.Actions.ReviewRenameRecovery(prompt.Eligible, CancellationToken.None);
        await recovery.Should().ThrowAsync<InvalidOperationException>();
        await using var f = new Fixture();
        f.Files.AfterRecoveryRead = () => f.Open = false;
        var inspect = () => f.Actions.ReviewRenameRecovery(f.Eligible, CancellationToken.None);
        await inspect.Should().ThrowAsync<InvalidOperationException>();
        var legacy = new OwnedInstructionEditorActions(new NoRenameStore(), f.Admission, f.Revisions, f.Catalogue, f.Audit,
            new BuiltInCommandRouter(new BuiltInCommandCatalog()));
        var noNative = () => legacy.ReviewRename(f.Files.Current!, "renamed", () => true, CancellationToken.None);
        await noNative.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExistingDependentResolutionIsRevokedAndOldReferenceCannotSelectIndependentlyEnabledNewName()
    {
        await using var f = new Fixture();
        var source = f.Files.Current!;
        await f.Actions.Activate(source, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        var profile = BoundedAgentProfile.Parse(ArtifactFileDocument.Parse(ArtifactFileDocument.Encode($"""
            ---
            name: role
            description: Role
            kora-agent-format: 2
            runtime: local
            input: text
            output: text
            references: {source.Document.Definition.Id}
            tools: none
            maximum-calls: 0
            maximum-rounds: 1
            maximum-input-bytes: 32768
            maximum-output-bytes: 4096
            deadline-ms: 15000
            ---
            Exact dependent guidance.
            """, false), ArtifactKind.Agent, "selected-user-source", "role"));
        var resolver = new AgentInstructionReferenceResolver(f.Catalogue, f.Files, f.Revisions);
        var revoked = 0;
        using var dependent = resolver.Review(profile, () => revoked++);
        await dependent.ResolveAsync(f.Eligible, CancellationToken.None);
        var captured = dependent.Bundle!.Instructions.Single();
        var review = await f.Actions.ReviewRename(source, "renamed", f.Eligible, CancellationToken.None);
        var saved = await f.Actions.Rename(review, (_, _) => Task.FromResult(true), (_, after) =>
        {
            f.Catalogue.Publish(f.Revisions.Reconcile([Candidate(after)]));
            return true;
        }, f.Eligible, CancellationToken.None);
        dependent.IsCurrent().Should().BeFalse();
        revoked.Should().Be(1);
        captured.Document.CopyBytes().Should().Equal(source.Document.CopyBytes());
        await f.Actions.Activate(saved!, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        ((Action)(() => _ = resolver.Review(profile, () => { }))).Should().Throw<InvalidOperationException>();
        profile.References.Single().Id.Should().Be(source.Document.Definition.Id);
        dependent.Bundle.Instructions.Single().Should().BeSameAs(captured);
    }

    private sealed class NoRenameStore : IOwnedInstructionStore
    {
        public Task<IReadOnlyList<string>> List(CancellationToken token) => throw new NotSupportedException();
        public Task<OwnedInstructionSnapshot?> Read(string name, CancellationToken token) => throw new NotSupportedException();
        public Task<InstructionWriteResult> Write(string name, OwnedInstructionSnapshot? expected, ArtifactFileDocument? replacement,
            Func<bool> eligible, Func<Func<InstructionWriteResult>, Task<InstructionWriteResult>> commit, CancellationToken token) =>
            throw new NotSupportedException();
    }

    private sealed partial class Files
    {
        internal int Renames;
        internal int Recoveries;
        internal string? RenameFailure;
        internal OwnedInstructionRenameRecovery? PendingRename;
        internal Action? AfterRename;
        internal Action? AfterRecoveryRead;
        public Task<OwnedInstructionRenameRecovery?> ReadRenameRecovery(CancellationToken token)
        { AfterRecoveryRead?.Invoke(); return Task.FromResult(PendingRename); }
        public Task<OwnedInstructionSnapshot> RecoverRename(OwnedInstructionRenameRecovery recovery, Func<bool> eligible,
            Func<Func<OwnedInstructionSnapshot>, Task<OwnedInstructionSnapshot>> commit, CancellationToken token)
        { Recoveries++; return Rename(recovery.Proposal, eligible, commit, token); }
        public Task<OwnedInstructionSnapshot> Rename(OwnedInstructionRename proposal, Func<bool> eligible,
            Func<Func<OwnedInstructionSnapshot>, Task<OwnedInstructionSnapshot>> commit, CancellationToken token) =>
            commit(() =>
            {
                token.ThrowIfCancellationRequested();
                if (!eligible()) { throw new InvalidOperationException("Retired authority"); }
                if (RenameFailure is "storage") { throw new IOException("Native unavailable"); }
                if (RenameFailure is "cancel") { throw new OperationCanceledException(token); }
                Renames++;
                var saved = new OwnedInstructionSnapshot(proposal.DestinationName, proposal.Source.DirectoryIdentity, new string('d', 48), proposal.Proposed);
                Current = null;
                Copies[saved.Name] = saved;
                PendingRename = null;
                AfterRename?.Invoke();
                if (RenameFailure is "uncertain") { throw new InstructionRenameUnconfirmedException(new IOException("Interrupted")); }
                return saved;
            });
    }
}

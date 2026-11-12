using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Application.Hosting;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Commands;

namespace Kora.Application.UnitTests.Definitions;

[Collection("Host tracing")]
public sealed class OwnedPromptRenameTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public OwnedPromptRenameTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenameAndRecoveryHoldBothIdentitiesAndRequireIndependentNewUse(bool recover)
    {
        await using var f = new Fixture();
        var source = f.Files.Source!;
        await f.Actions.Activate(source, _ => Task.FromResult(true), () => true, CancellationToken.None);
        var router = new ArtifactCommandRouter(f.Catalogue);
        router.Match("/prompt original", "Kora").Invocation.Should().NotBeNull();
        if (recover) { f.Files.Recovery = new(new OwnedPromptRename(source, "renamed"), "observed", OwnedInstructionRenamePhase.Prepared); }
        var review = recover ? await f.Actions.ReviewRenameRecovery(() => true, CancellationToken.None)
            : await f.Actions.ReviewRename(source, "renamed", () => true, CancellationToken.None);
        review!.Dependents.Should().BeEmpty();
        review.Proposal.Should().BeOfType<OwnedPromptRename>();
        var confirmed = false;
        var saved = await f.Actions.Rename(review, (exact, _) =>
        {
            exact.Should().BeSameAs(review);
            confirmed = true;
            f.Files.Renames.Should().Be(0);
            return Task.FromResult(true);
        }, (_, after) =>
        {
            confirmed.Should().BeTrue();
            f.Catalogue.Publish(f.Revisions.Reconcile([Candidate(after)]));
            return true;
        }, () => true, CancellationToken.None);
        saved!.Name.Should().Be("renamed");
        saved.Document.CopyBytes().Should().Equal(review.Proposal.Proposed.CopyBytes());
        saved.Document.Body.Should().Be(source.Document.Body);
        f.Files.Source.Should().BeNull();
        f.Files.Renames.Should().Be(1);
        f.Files.Recoveries.Should().Be(recover ? 1 : 0);
        f.Catalogue.Artifacts.Should().BeEmpty();
        router.Match("/prompt original", "Kora").Invocation.Should().BeNull();
        router.Match("/prompt renamed", "Kora").Invocation.Should().BeNull();
        f.Revisions.IsEnabled(source).Should().BeFalse();
        f.Revisions.IsEnabled(saved).Should().BeFalse();
        var action = recover ? "definition.prompt.recover-rename" : "definition.prompt.rename";
        f.Audit.Events.Where(item => string.Equals(item.ActionId, action, StringComparison.Ordinal)).Select(item => item.Outcome)
            .Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        review.AuditTarget.Should().NotContain("original").And.NotContain("renamed").And.NotContain("Literal");
        (await f.Actions.Inspect("renamed", () => true, CancellationToken.None)).Should().BeSameAs(saved);
        await f.Actions.Activate(saved, _ => Task.FromResult(true), () => true, CancellationToken.None);
        router.Match("/prompt renamed", "Kora").Invocation!.Artifact.Digest.Should().Be(saved.Document.Definition.Digest);
        router.Match("/prompt original", "Kora").Invocation.Should().BeNull();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("catalogue")]
    [InlineData("during-confirm")]
    [InlineData("privacy")]
    [InlineData("session")]
    [InlineData("request-audit")]
    public async Task UnconfirmedOrStaleReviewNeverReachesWriter(string change)
    {
        await using var f = new Fixture();
        var review = await f.Actions.ReviewRename(f.Files.Source!, "renamed", () => true, CancellationToken.None);
        var current = true;
        if (change is "catalogue") { f.Catalogue.Publish(f.Catalogue.Candidates.ToArray()); }
        if (change is "request-audit") { f.Audit.Failure = SecurityAuditOutcome.Requested; }
        var operation = () => f.Actions.Rename(review, (_, _) =>
        {
            if (change is "privacy") { current = false; }
            if (change is "session") { f.Control.Authority = f.Control.Authority! with { IsActive = false }; }
            if (change is "during-confirm") { f.Catalogue.Publish(f.Catalogue.Candidates.ToArray()); }
            return Task.FromResult(change is not "cancel");
        }, (_, _) => throw new InvalidOperationException("No reconciliation authorized"), () => current, CancellationToken.None);
        if (change is "cancel") { (await operation()).Should().BeNull(); }
        else { await operation.Should().ThrowAsync<Exception>(); }
        f.Files.Renames.Should().Be(0);
        f.Files.Source.Should().NotBeNull();
        f.Audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("stale")]
    [InlineData("occupied")]
    [InlineData("excluded")]
    [InlineData("unavailable")]
    [InlineData("kind")]
    [InlineData("control")]
    [InlineData("builtin")]
    public async Task PreparationRejectsChangedSourceAndNamespaceWithoutWrites(string change)
    {
        await using var f = new Fixture();
        var source = f.Files.Source!;
        if (change is "missing") { f.Files.Source = null; }
        if (change is "stale") { f.Files.Source = source with { FileIdentity = "new-identity" }; }
        if (change is "occupied") { f.Files.Destination = source with { Name = "renamed" }; }
        if (change is "excluded") { f.Catalogue.Publish([source.Document.Definition]); }
        if (change is "unavailable") { f.Catalogue.ReportReloadFailure(new IOException("Held")); }
        if (change is "kind")
        {
            source = source with { Document = ArtifactFileDocument.Parse(source.Document.CopyBytes(), ArtifactKind.Instruction, "kora-profile", "original") };
        }
        var operation = () => f.Actions.ReviewRename(source, change is "control" ? "help" : change is "builtin" ? "local-models" : "renamed",
            () => true, CancellationToken.None);
        await operation.Should().ThrowAsync<Exception>();
        f.Files.Renames.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("storage", SecurityAuditOutcome.Failed)]
    [InlineData("cancel", SecurityAuditOutcome.Cancelled)]
    [InlineData("uncertain", SecurityAuditOutcome.Unknown)]
    [InlineData("reconcile", SecurityAuditOutcome.Unknown)]
    [InlineData("terminal-audit", SecurityAuditOutcome.Unknown)]
    public async Task FailedOrUncertainEffectsNeverRestoreReviewedUse(string failure, SecurityAuditOutcome outcome)
    {
        await using var f = new Fixture();
        var source = f.Files.Source!;
        await f.Actions.Activate(source, _ => Task.FromResult(true), () => true, CancellationToken.None);
        var review = await f.Actions.ReviewRename(source, "renamed", () => true, CancellationToken.None);
        f.Files.Failure = failure;
        if (failure is "terminal-audit") { f.Audit.Failure = SecurityAuditOutcome.Succeeded; }
        var operation = () => f.Actions.Rename(review, (_, _) => Task.FromResult(true), (_, saved) =>
        {
            if (failure is "reconcile") { return false; }
            f.Catalogue.Publish(f.Revisions.Reconcile([Candidate(saved)]));
            return true;
        }, () => true, CancellationToken.None);
        await operation.Should().ThrowAsync<Exception>();
        f.Catalogue.Artifacts.Should().BeEmpty();
        f.Revisions.IsEnabled(source).Should().BeFalse();
        f.Audit.Events.Should().Contain(item => item.ActionId == "definition.prompt.rename" && item.Outcome == outcome);
        f.Audit.Events.Should().NotContain(item => item.ActionId == "definition.prompt.rename" && item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task RecoveryObservationAndReviewCannotCrossKindOrRetiredAuthority()
    {
        await using var f = new Fixture();
        (await f.Actions.ReviewRenameRecovery(() => true, CancellationToken.None)).Should().BeNull();
        var instruction = f.Files.Source! with
        {
            Document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                "---\nkora-format: 1\nname: original\ndescription: Test\n---\nGuidance", false), ArtifactKind.Instruction, "kora-profile", "original"),
            CanonicalFileName = "original.instructions.md",
        };
        f.Files.Recovery = new(new(instruction, "renamed"), "foreign", OwnedInstructionRenamePhase.Prepared);
        var recovery = () => f.Actions.ReviewRenameRecovery(() => true, CancellationToken.None);
        await recovery.Should().ThrowAsync<InvalidDataException>();
        var instructionActions = new OwnedInstructionEditorActions(f.Files, f.Admission, f.Revisions, f.Catalogue, f.Audit,
            new(new()), ArtifactKind.Instruction);
        var review = await f.Actions.ReviewRename(f.Files.Source, "renamed", () => true, CancellationToken.None);
        var wrongKind = () => instructionActions.Rename(review, (_, _) => Task.FromResult(true), (_, _) => true, () => true, CancellationToken.None);
        await wrongKind.Should().ThrowAsync<InvalidDataException>();
        f.Files.Recovery = null;
        var current = true;
        f.Files.AfterRecoveryRead = () => current = false;
        var retired = () => f.Actions.ReviewRenameRecovery(() => current, CancellationToken.None);
        await retired.Should().ThrowAsync<InvalidOperationException>();
        f.Files.Renames.Should().Be(0);
    }

    [Fact]
    public async Task CancellationWaitsForPhysicalWriterJoinAndCannotPublishAfterAuthorityRetires()
    {
        await using var f = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Files.BeforeCommit = async () => { entered.SetResult(); await release.Task; };
        var review = await f.Actions.ReviewRename(f.Files.Source!, "renamed", () => true, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var rename = f.Actions.Rename(review, (_, _) => Task.FromResult(true), (_, _) => true, () => true, cancellation.Token);
        await entered.Task;
        cancellation.Cancel();
        rename.IsCompleted.Should().BeFalse();
        release.SetResult();
        await ((Func<Task>)(async () => await rename)).Should().ThrowAsync<OperationCanceledException>();
        f.Files.Renames.Should().Be(0);
        f.Files.Source.Should().NotBeNull();
    }

    private static ArtifactDefinition Candidate(OwnedInstructionSnapshot value) =>
        value.Document.Definition with { OwnedInstructionRevision = value.Revision };

    private sealed class Fixture : IAsyncDisposable
    {
        internal readonly AudioControlTestStore Control = new();
        internal readonly Files Files = new();
        internal readonly Audit Audit = new();
        internal readonly InstructionControlAdmission Admission;
        internal readonly InstructionRevisionCatalogue Revisions = new(new LocalSharedSkillPreferencesTests.Store(), ArtifactKind.Prompt);
        internal readonly ArtifactCatalogue Catalogue;
        internal readonly OwnedInstructionEditorActions Actions;
        internal Fixture()
        {
            Catalogue = new(Revisions.Reconcile([Candidate(Files.Source!)]));
            Admission = new(Control, Control, new HostTaskCoordinator(Control));
            Actions = new(Files, Admission, Revisions, Catalogue, Audit, new BuiltInCommandRouter(new()), ArtifactKind.Prompt);
        }
        public async ValueTask DisposeAsync()
        {
            Catalogue.Dispose();
            await Admission.DisposeAsync();
        }
    }

    private sealed class Files : IOwnedInstructionStore, IOwnedPromptRenameStore
    {
        internal OwnedInstructionSnapshot? Source = new("original", new string('a', 48), new string('b', 48),
            ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                "---\nkora-format: 2\nname: original\ndescription: Test\n---\nLiteral ${input:topic}", true),
                ArtifactKind.Prompt, "kora-profile", "original"));
        internal OwnedInstructionSnapshot? Destination;
        internal OwnedInstructionRenameRecovery? Recovery;
        internal int Renames;
        internal int Recoveries;
        internal string? Failure;
        internal Action? AfterRecoveryRead;
        internal Func<Task>? BeforeCommit;
        public Task<IReadOnlyList<string>> List(CancellationToken token) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<OwnedInstructionSnapshot?> Read(string name, CancellationToken token) => Task.FromResult(name is "original" ? Source : Destination);
        public Task<InstructionWriteResult> Write(string name, OwnedInstructionSnapshot? expected, ArtifactFileDocument? replacement,
            Func<bool> eligible, Func<Func<InstructionWriteResult>, Task<InstructionWriteResult>> commit, CancellationToken token) =>
            throw new NotSupportedException();
        public Task<OwnedInstructionRenameRecovery?> ReadRenameRecovery(CancellationToken token)
        { AfterRecoveryRead?.Invoke(); return Task.FromResult(Recovery); }
        public Task<OwnedInstructionSnapshot> RecoverRename(OwnedInstructionRenameRecovery recovery, Func<bool> eligible,
            Func<Func<OwnedInstructionSnapshot>, Task<OwnedInstructionSnapshot>> commit, CancellationToken token)
        { Recoveries++; return Rename(recovery.Proposal, eligible, commit, token); }
        public async Task<OwnedInstructionSnapshot> Rename(OwnedInstructionRename proposal, Func<bool> eligible,
            Func<Func<OwnedInstructionSnapshot>, Task<OwnedInstructionSnapshot>> commit, CancellationToken token)
        {
            if (BeforeCommit is not null) { await BeforeCommit(); }
            return await commit(() =>
            {
                token.ThrowIfCancellationRequested();
                if (!eligible()) { throw new InvalidOperationException("Retired authority"); }
                if (Failure is "storage") { throw new IOException("Storage unavailable"); }
                if (Failure is "cancel") { throw new OperationCanceledException(token); }
                Renames++;
                Source = null;
                Destination = new(proposal.DestinationName, proposal.Source.DirectoryIdentity, new string('c', 48), proposal.Proposed);
                Recovery = null;
                if (Failure is "uncertain") { throw new InstructionRenameUnconfirmedException(new IOException("Readback unavailable")); }
                return Destination;
            });
        }
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal readonly List<SecurityAuditEvent> Events = [];
        internal SecurityAuditOutcome? Failure;
        public void Write(SecurityAuditEvent value)
        {
            if (value.ActionId.StartsWith("definition.prompt.", StringComparison.Ordinal)
                && value.ActionId.EndsWith("rename", StringComparison.Ordinal) && value.Outcome == Failure)
            { throw new IOException("Required audit unavailable"); }
            Events.Add(value);
        }
    }
}

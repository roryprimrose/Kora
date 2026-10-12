using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Application.Hosting;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Commands;

namespace Kora.Application.UnitTests.Definitions;

[Collection("Host tracing")]
public sealed partial class InstructionEditorTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public InstructionEditorTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    private static OwnedInstructionSnapshot Snapshot(string body = "Original", bool native = true, ArtifactKind kind = ArtifactKind.Instruction) =>
        new("example", new string('a', 48), new string('b', 48),
            ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                $"---\n{(native ? "kora-format: 1\n" : "")}name: example\ndescription: Test\n---\n{body}", false),
                kind, "kora-profile", "example"));
    private static ArtifactDefinition Candidate(OwnedInstructionSnapshot snapshot) =>
        snapshot.Document.Definition with { OwnedInstructionRevision = snapshot.Revision };

    [Theory]
    [InlineData(InstructionConflictChoice.Reload, ArtifactKind.Instruction)]
    [InlineData(InstructionConflictChoice.Overwrite, ArtifactKind.Instruction)]
    [InlineData(InstructionConflictChoice.Cancel, ArtifactKind.Instruction)]
    [InlineData(InstructionConflictChoice.Reload, ArtifactKind.Prompt)]
    [InlineData(InstructionConflictChoice.Overwrite, ArtifactKind.Prompt)]
    [InlineData(InstructionConflictChoice.Cancel, ArtifactKind.Prompt)]
    public async Task ActualSaveWorkflowAppliesOnlyTheExplicitConflictChoice(InstructionConflictChoice choice, ArtifactKind kind)
    {
        await using var f = new Fixture(kind);
        var original = f.Files.Current!;
        f.Files.Current = Snapshot("External", kind: kind);
        var review = new Review { Choice = choice };
        var draft = Snapshot("Kora draft", kind: kind).Document.Text;
        var outcome = await f.Workflow.Save("example", original, draft, false, review, f.Eligible, CancellationToken.None);
        review.Conflicts.Should().Be(1);
        if (choice == InstructionConflictChoice.Reload)
        {
            outcome.Reloaded.Should().BeTrue();
            outcome.Current!.Document.Definition.Content.Should().Be("External");
        }
        else if (choice == InstructionConflictChoice.Cancel)
        {
            outcome.Reloaded.Should().BeFalse();
            outcome.Current.Should().BeSameAs(original);
            f.Files.Current.Document.Definition.Content.Should().Be("External");
        }
        else
        {
            outcome.Saved.Should().BeTrue();
            f.Files.Current!.Document.Definition.Content.Should().Be("Kora draft");
            review.Confirmations.Should().Be(2);
        }
        draft.Should().Be(Snapshot("Kora draft", kind: kind).Document.Text);
        f.Catalogue.Artifacts.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task AnotherChangeBeforeFinalComparisonRequiresRenewedConflictReview(ArtifactKind kind)
    {
        await using var f = new Fixture(kind);
        var original = f.Files.Current;
        f.Files.Current = Snapshot("External", kind: kind);
        var review = new Review { Choice = InstructionConflictChoice.Overwrite };
        review.BeforeConfirm = count => { if (count == 2) { f.Files.Current = Snapshot("External again", kind: kind); } };
        var result = await f.Workflow.Save("example", original, Snapshot("Draft").Document.Text,
            false, review, f.Eligible, CancellationToken.None);
        result.Saved.Should().BeTrue();
        review.Conflicts.Should().Be(2);
        review.Confirmations.Should().Be(3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelConfirmationPreservesDraftAndReviewedDeleteRequiresOwnConfirmation(bool delete)
    {
        await using var f = new Fixture();
        var original = f.Files.Current!;
        var review = new Review { Confirmed = false };
        var result = await f.Workflow.Save("example", original, original.Document.Text, delete, review, f.Eligible, CancellationToken.None);
        result.Saved.Should().BeFalse();
        result.Current.Should().BeSameAs(original);
        f.Files.Writes.Should().Be(0);
        review.Confirmed = true;
        var committed = await f.Workflow.Save("example", original, original.Document.Text, delete, review, f.Eligible, CancellationToken.None);
        committed.Saved.Should().BeTrue();
        committed.Deleted.Should().Be(delete);
        f.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi, ArtifactKind.Instruction)]
    [InlineData(RequestOrigin.ActivatedVoice, ArtifactKind.Instruction)]
    [InlineData(RequestOrigin.LocalUi, ArtifactKind.Prompt)]
    [InlineData(RequestOrigin.ActivatedVoice, ArtifactKind.Prompt)]
    public async Task NativeSaveReadbackIsDisabledUntilSeparateExactActivationAndSharedLookup(RequestOrigin origin, ArtifactKind kind)
    {
        await using var f = new Fixture(kind);
        using var originalInput = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Desktop, HostOperation.Request);
        (await f.Actions.List(f.Eligible, CancellationToken.None)).Should().Equal("example");
        (await f.Actions.Inspect("example", f.Eligible, CancellationToken.None)).Should().BeSameAs(f.Files.Current);
        var saved = await f.Workflow.Save("example", f.Files.Current, Snapshot("Edited").Document.Text,
            false, new Review(), f.Eligible, CancellationToken.None);
        f.Catalogue.Publish(f.Revisions.Reconcile([Candidate(saved.Current!)]));
        var router = new ArtifactCommandRouter(f.Catalogue);
        var command = "/" + kind.ToString().ToLowerInvariant() + " example";
        router.Match(command, "Kora").Invocation.Should().BeNull();
        (await f.Actions.Activate(saved.Current!, _ => Task.FromResult(false), f.Eligible, CancellationToken.None)).Should().BeFalse();
        (await f.Actions.Activate(saved.Current!, _ => Task.FromResult(true), f.Eligible, CancellationToken.None)).Should().BeTrue();
        router.Match(command, "Kora").Invocation!.Artifact.Digest.Should().Be(saved.Current!.Document.Definition.Digest);
        router.Match("Kora, use example", "Kora").Invocation.Should().NotBeNull();
        (await f.Actions.IsCurrent(Candidate(saved.Current), f.Eligible, CancellationToken.None)).Should().BeTrue();
        f.Files.Current = Snapshot("External", kind: kind);
        (await f.Actions.IsCurrent(Candidate(saved.Current), f.Eligible, CancellationToken.None)).Should().BeFalse();
        router.Match(command, "Kora").Invocation.Should().BeNull();
        f.Catalogue.Publish(f.Revisions.Reconcile([Candidate(f.Files.Current)]));
        f.Catalogue.Artifacts.Should().BeEmpty();
        (await f.Actions.IsCurrent(new ArtifactDefinition("bundled.test", ArtifactKind.Prompt, "Bundled",
            "Test", "test", ["test"], "bundled", "1", new string('a', 64), "Text"), f.Eligible, CancellationToken.None)).Should().BeTrue();
    }

    [Theory]
    [InlineData("privacy", ArtifactKind.Instruction)]
    [InlineData("cancel", ArtifactKind.Instruction)]
    [InlineData("session", ArtifactKind.Instruction)]
    [InlineData("audit-request", ArtifactKind.Instruction)]
    [InlineData("audit-terminal", ArtifactKind.Instruction)]
    [InlineData("storage", ArtifactKind.Instruction)]
    [InlineData("privacy", ArtifactKind.Prompt)]
    [InlineData("cancel", ArtifactKind.Prompt)]
    [InlineData("session", ArtifactKind.Prompt)]
    [InlineData("audit-request", ArtifactKind.Prompt)]
    [InlineData("audit-terminal", ArtifactKind.Prompt)]
    [InlineData("storage", ArtifactKind.Prompt)]
    public async Task ChangedAdmissionAndAuditOrStorageFailuresCannotClaimSuccessfulUse(string failure, ArtifactKind kind)
    {
        await using var f = new Fixture(kind);
        using var cancelled = new CancellationTokenSource();
        var review = new Review();
        review.BeforeConfirm = _ =>
        {
            if (failure is "privacy") { f.Open = false; }
            if (failure is "cancel") { cancelled.Cancel(); }
            if (failure is "session") { f.Control.Authority = f.Control.Authority! with { IsActive = false }; }
        };
        if (failure.StartsWith("audit", StringComparison.Ordinal))
        {
            f.Audit.BeforeWrite = audit =>
            {
                if (audit.Outcome == (failure is "audit-request" ? SecurityAuditOutcome.Requested : SecurityAuditOutcome.Succeeded))
                { throw new IOException("Audit unavailable"); }
            };
        }

        f.Files.FailWrite = failure is "storage";
        var save = () => f.Workflow.Save("example", f.Files.Current, Snapshot("Draft").Document.Text,
            false, review, f.Eligible, cancelled.Token);
        await save.Should().ThrowAsync<Exception>();
        f.Catalogue.Artifacts.Should().BeEmpty();
        f.Audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task ClosedKindActionsRejectForeignReviewAndMutationBeforeAnyWriteOrAudit()
    {
        await using var f = new Fixture(ArtifactKind.Prompt);
        var instruction = Snapshot();
        var expected = () => f.Actions.Save("example", instruction, null, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        await expected.Should().ThrowAsync<InvalidDataException>();
        var replacement = () => f.Actions.Save("example", null, instruction.Document, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        await replacement.Should().ThrowAsync<InvalidDataException>();
        var review = () => f.Actions.Activate(instruction, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        await review.Should().ThrowAsync<InvalidDataException>();
        (await f.Actions.IsCurrent(Candidate(instruction), f.Eligible, CancellationToken.None)).Should().BeFalse();
        ((Action)(() => _ = new OwnedInstructionEditorActions(f.Files, f.Admission, f.Revisions, f.Catalogue, f.Audit,
            new BuiltInCommandRouter(new BuiltInCommandCatalog()), ArtifactKind.Agent))).Should().Throw<InvalidDataException>();
        f.Files.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task SameFileIdInOtherKindIsNotRemovedDisabledOrSelectedByPromptActions()
    {
        await using var f = new Fixture(ArtifactKind.Prompt);
        var instruction = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            "---\nname: instruction-note\ndescription: Test\n---\nGuidance", false), ArtifactKind.Instruction, "kora-profile", "example").Definition;
        f.Catalogue.Publish([instruction, Candidate(f.Files.Current!)]);
        f.Actions.Validate("example", f.Files.Current!.Document.Text, false).Definition.Kind.Should().Be(ArtifactKind.Prompt);
        var result = await f.Workflow.Save("example", f.Files.Current, Snapshot("Draft").Document.Text,
            false, new Review(), f.Eligible, CancellationToken.None);
        result.Saved.Should().BeTrue();
        f.Catalogue.Artifacts.Should().ContainSingle().Which.Should().BeSameAs(instruction);
        f.Actions.SuspendReviewedUse();
        f.Catalogue.Artifacts.Should().ContainSingle().Which.Should().BeSameAs(instruction);
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("missing")]
    [InlineData("candidate")]
    [InlineData("terminal")]
    public async Task StaleOrUnconfirmedActivationAlwaysRemainsWithheld(string reason)
    {
        await using var f = new Fixture();
        var reviewed = f.Files.Current!;
        if (reason is "changed") { f.Files.Current = Snapshot("New"); }
        if (reason is "missing") { f.Files.Current = null; }
        if (reason is "candidate") { f.Catalogue.Publish([Candidate(Snapshot("Other")) with { IsUseEnabled = false }]); }
        if (reason is "terminal")
        {
            await f.Actions.List(f.Eligible, CancellationToken.None);
            f.Control.FailTerminal = true;
        }
        var activate = () => f.Actions.Activate(reviewed, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        await activate.Should().ThrowAsync<Exception>();
        f.Catalogue.Artifacts.Should().BeEmpty();
    }

    [Fact]
    public async Task HostSystemCannotReclassifyOutputAsOriginalNativeInput()
    {
        await using var f = new Fixture();
        using var foreign = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request);
        var list = () => f.Actions.List(f.Eligible, CancellationToken.None);
        await list.Should().ThrowAsync<InvalidOperationException>();
        f.Files.Writes.Should().Be(0);
    }

    [Fact]
    public async Task ExpiredConflictReviewCannotWriteOrSilentlyReload()
    {
        await using var f = new Fixture();
        var original = f.Files.Current;
        f.Files.Current = Snapshot("External");
        var review = new Review { BeforeConflict = () => f.Open = false, Choice = InstructionConflictChoice.Reload };
        var save = () => f.Workflow.Save("example", original, Snapshot("Draft").Document.Text, false,
            review, f.Eligible, CancellationToken.None);
        await save.Should().ThrowAsync<InvalidOperationException>();
        f.Files.Current.Document.Definition.Content.Should().Be("External");
    }

    [Fact]
    public void ValidatorRejectsCollisionsUnsupportedOriginalsAndReservedControlNames()
    {
        var state = new LocalSharedSkillPreferencesTests.Store();
        var revisions = new InstructionRevisionCatalogue(state);
        var original = Snapshot();
        var files = new Files();
        var control = new AudioControlTestStore();
        var admission = new InstructionControlAdmission(control, control, new HostTaskCoordinator(control));
        var actions = new OwnedInstructionEditorActions(files, admission, revisions,
            new ArtifactCatalogue([Candidate(original)]), new Audit(), new BuiltInCommandRouter(new BuiltInCommandCatalog()));
        ((Action)(() => actions.Validate("new", original.Document.Text, false))).Should().Throw<ArgumentException>();
        foreach (var reserved in new[] { "skill", "skills", "instruction", "instructions", "prompt", "prompts", "agent", "agents", "definitions", "help", "exit", "settings" })
        {
            ((Action)(() => actions.Validate("example", Snapshot().Document.Text.Replace("name: example", "name: " + reserved, StringComparison.Ordinal), false)))
                .Should().Throw<InvalidDataException>();
        }
        ((Action)(() => actions.Validate("example", Snapshot().Document.Text.Replace("name: example", "name: local models", StringComparison.Ordinal), false)))
            .Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task NewDraftHasNoOriginalAndDeletedConflictReloadObservesAbsence()
    {
        await using var f = new Fixture();
        f.Files.Current = null;
        var saved = await f.Workflow.Save("example", null, Snapshot("New").Document.Text, false,
            new Review(), f.Eligible, CancellationToken.None);
        saved.Saved.Should().BeTrue();
        var original = saved.Current;
        f.Files.Current = null;
        var reloaded = await f.Workflow.Save("example", original, Snapshot("Draft").Document.Text, false,
            new Review { Choice = InstructionConflictChoice.Reload }, f.Eligible, CancellationToken.None);
        reloaded.Reloaded.Should().BeTrue();
        reloaded.Current.Should().BeNull();
    }

    [Fact]
    public async Task UnchangedSavePreservesTheExactReviewedActiveRevisionWithoutWriting()
    {
        await using var f = new Fixture();
        var original = f.Files.Current!;
        await f.Actions.Activate(original, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        var result = await f.Workflow.Save("example", original, original.Document.Text, false,
            new Review(), f.Eligible, CancellationToken.None);
        result.Unchanged.Should().BeTrue();
        result.Saved.Should().BeTrue();
        f.Files.Writes.Should().Be(0);
        f.Catalogue.IsCurrent(original.Document.Definition.Id, original.Document.Definition.Digest).Should().BeTrue();
        f.Actions.SuspendReviewedUse();
        f.Catalogue.Artifacts.Should().BeEmpty();
    }

    [Fact]
    public async Task ChangedLegacySaveIncludesAReviewedNativeMarkerAndRemainsDisabled()
    {
        await using var f = new Fixture();
        f.Files.Current = Snapshot(native: false);
        var original = f.Files.Current;
        var result = await f.Workflow.Save("example", original, Snapshot("Edited", native: false).Document.Text,
            false, new Review(), f.Eligible, CancellationToken.None);
        result.Current!.Document.Metadata["kora-format"].Should().Be("1");
        f.Catalogue.Artifacts.Should().BeEmpty();
    }

    [Fact]
    public async Task ObservedPublicationFollowedBySessionClosureIsHeldAndAuditedAsUnconfirmed()
    {
        await using var f = new Fixture();
        f.Files.AfterWrite = () => f.Control.Authority = f.Control.Authority! with { IsActive = false };
        var save = () => f.Workflow.Save("example", f.Files.Current, Snapshot("Published").Document.Text,
            false, new Review(), f.Eligible, CancellationToken.None);
        var failure = await save.Should().ThrowAsync<InstructionPublicationUnconfirmedException>();
        failure.Which.CancellationRequested.Should().BeFalse();
        f.Files.Current!.Document.Definition.Content.Should().Be("Published");
        f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Unknown);
        f.Catalogue.Artifacts.Should().BeEmpty();
    }

    [Fact]
    public async Task NativeUnconfirmedEffectAndReaderFailureNeverRestoreUsableGuidance()
    {
        await using var f = new Fixture();
        var original = f.Files.Current!;
        f.Files.Unconfirmed = true;
        var save = () => f.Workflow.Save("example", original, Snapshot("Published").Document.Text,
            false, new Review(), f.Eligible, CancellationToken.None);
        await save.Should().ThrowAsync<InstructionPublicationUnconfirmedException>();
        f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Unknown);
        f.Files.FailRead = true;
        (await f.Actions.IsCurrent(Candidate(original), f.Eligible, CancellationToken.None)).Should().BeFalse();
        f.Catalogue.Artifacts.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReviewedUseExpiringAfterDurableCompletionCannotBeOffered(bool cancel)
    {
        await using var f = new Fixture();
        await f.Actions.List(f.Eligible, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        f.Control.BeforeCommit = record =>
        {
            if (!record.IsTerminal) { return; }
            if (cancel) { cancellation.Cancel(); }
            else { f.Open = false; }
        };
        var activate = () => f.Actions.Activate(f.Files.Current!, _ => Task.FromResult(true), f.Eligible, cancellation.Token);
        await activate.Should().ThrowAsync<InvalidOperationException>();
        f.Catalogue.Artifacts.Should().BeEmpty();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal readonly AudioControlTestStore Control = new();
        internal readonly Files Files = new();
        internal readonly Audit Audit = new();
        internal readonly InstructionRevisionCatalogue Revisions;
        internal readonly ArtifactCatalogue Catalogue;
        internal readonly LocalSharedSkillPreferencesTests.Store State = new();
        internal readonly InstructionControlAdmission Admission;
        internal readonly OwnedInstructionEditorActions Actions;
        internal readonly InstructionSaveWorkflow Workflow;
        internal bool Open = true;
        internal bool Eligible() => Open;
        internal Fixture(ArtifactKind kind = ArtifactKind.Instruction)
        {
            Files.Current = Snapshot(kind: kind);
            Revisions = new(State, kind);
            Catalogue = new(Revisions.Reconcile([Candidate(Files.Current!)]));
            Admission = new(Control, Control, new HostTaskCoordinator(Control));
            Actions = new(Files, Admission, Revisions, Catalogue, Audit, new BuiltInCommandRouter(new BuiltInCommandCatalog()), kind);
            Workflow = new(Actions);
        }
        public ValueTask DisposeAsync() => Admission.DisposeAsync();
    }

    private sealed class Files : IOwnedInstructionStore
    {
        internal OwnedInstructionSnapshot? Current = Snapshot();
        internal readonly Dictionary<string, OwnedInstructionSnapshot> Copies = new(StringComparer.Ordinal);
        internal int Writes;
        internal bool FailWrite;
        internal bool FailRead;
        internal bool Unconfirmed;
        internal Action? AfterWrite;
        internal Action? BeforeRead;
        internal int Reads;
        public Task<IReadOnlyList<string>> List(CancellationToken token) => Task.FromResult<IReadOnlyList<string>>(["example"]);
        public Task<OwnedInstructionSnapshot?> Read(string name, CancellationToken token)
        {
            Reads++; BeforeRead?.Invoke();
            return FailRead ? throw new IOException("Reader unavailable") : Task.FromResult(name is "example" ? Current : Copies.GetValueOrDefault(name));
        }
        public Task<InstructionWriteResult> Write(string name, OwnedInstructionSnapshot? expected, ArtifactFileDocument? replacement,
            Func<bool> eligible, Func<Func<InstructionWriteResult>, Task<InstructionWriteResult>> commit, CancellationToken token) =>
            commit(() =>
        {
            token.ThrowIfCancellationRequested();
            if (!eligible()) { throw new InvalidOperationException("Host closed."); }
            if (FailWrite) { throw new IOException("Failed native storage"); }
            var current = name is "example" ? Current : Copies.GetValueOrDefault(name);
            if (!string.Equals(expected?.Revision, current?.Revision, StringComparison.Ordinal))
            { return new InstructionWriteResult(false, false, true, current); }
            if (replacement is not null && current is not null
                && string.Equals(replacement.Definition.Digest, current.Document.Definition.Digest, StringComparison.Ordinal))
            { return new InstructionWriteResult(false, false, false, current, Unchanged: true); }
            Writes++;
            var saved = replacement is null ? null : new OwnedInstructionSnapshot(name, new string('a', 48), new string('c', 48), replacement);
            if (name is "example") { Current = saved; }
            else if (saved is not null) { Copies[name] = saved; }
            AfterWrite?.Invoke();
            if (Unconfirmed) { throw new InstructionPublicationUnconfirmedException(false, new IOException("Readback unconfirmed")); }
            return new InstructionWriteResult(true, replacement is null, false, saved);
        });
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal readonly List<SecurityAuditEvent> Events = [];
        internal Action<SecurityAuditEvent>? BeforeWrite;
        public void Write(SecurityAuditEvent auditEvent) { BeforeWrite?.Invoke(auditEvent); Events.Add(auditEvent); }
    }

    private sealed class Review : IInstructionMutationReview
    {
        internal bool Confirmed = true;
        internal int Confirmations;
        internal int Conflicts;
        internal InstructionConflictChoice Choice = InstructionConflictChoice.Cancel;
        internal Action<int>? BeforeConfirm;
        internal Action? BeforeConflict;
        public Task<bool> ConfirmCopy(OwnedDefinitionCopy copy, CancellationToken token)
        { Confirmations++; BeforeConfirm?.Invoke(Confirmations); return Task.FromResult(Confirmed); }
        public Task<bool> Confirm(string name, OwnedInstructionSnapshot? expected, ArtifactFileDocument? proposed, CancellationToken token)
        { Confirmations++; BeforeConfirm?.Invoke(Confirmations); return Task.FromResult(Confirmed); }
        public Task<InstructionConflictChoice> Conflict(OwnedInstructionSnapshot? current, ArtifactFileDocument? proposed, CancellationToken token)
        { Conflicts++; BeforeConflict?.Invoke(); return Task.FromResult(Choice); }
    }
}

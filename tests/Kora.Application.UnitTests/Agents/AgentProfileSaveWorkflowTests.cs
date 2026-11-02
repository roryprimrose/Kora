using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Application.Definitions;
using Kora.Application.Hosting;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Agents;
using Kora.Core.Auditing;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Agents;

[Collection("Host tracing")]
public sealed class AgentProfileSaveWorkflowTests : IDisposable
{
    private const string Source = """
        ---
        name: example
        description: Inert original
        kora-agent-format: 1
        runtime: local
        input: text
        output: text
        references: none
        tools: none
        maximum-calls: 0
        maximum-rounds: 1
        maximum-input-bytes: 16000
        maximum-output-bytes: 2000
        deadline-ms: 1000
        ---
        Exact private guidance.
        """;
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public AgentProfileSaveWorkflowTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task CompleteReviewNativeConfirmCreateReadbackAndReadOnlyInventory(RequestOrigin origin)
    {
        await using var f = new Fixture();
        f.Workflow.Validate("example", Source).Document.Text.Should().Be(Source);
        (await f.Workflow.PrepareNewAsync("example", f.Eligible, CancellationToken.None)).Name.Should().Be("example");
        f.Files.Writes.Should().Be(0);
        using var review = await f.Review(origin);
        review.Revision.Should().Be(1);
        review.Document.Document.Text.Should().Be(Source);
        review.IsCurrent.Should().BeTrue();
        f.Files.Writes.Should().Be(0);
        var confirmations = 0;
        var result = await f.Workflow.SaveAsNewAsync(review, RequestOrigin.LocalUi, _ =>
        {
            confirmations++;
            f.Files.Writes.Should().Be(0);
            return Task.FromResult(true);
        }, f.Eligible, CancellationToken.None);
        result.State.Should().Be(AgentProfileWriteState.Saved);
        confirmations.Should().Be(1);
        f.Files.Writes.Should().Be(1);
        result.Snapshot!.Document.Document.CopyBytes().Should().Equal(review.Document.Document.CopyBytes());
        (await f.Workflow.ListAsync(f.Eligible, CancellationToken.None)).Should().Equal("example");
        (await f.Workflow.ReadAsync("example", f.Eligible, CancellationToken.None)).Should().BeSameAs(result.Snapshot);
        f.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Audit.Events.Should().OnlyContain(item => item.TargetId == "definition.owned-agent"
            && item.ActionId == "definition.agent.create");
        f.Audit.Events[0].Initiator.Should().Be(origin == RequestOrigin.ActivatedVoice
            ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser);
        review.IsCurrent.Should().BeFalse();
        var replay = () => f.Save(review);
        await replay.Should().ThrowAsync<InvalidOperationException>();
        f.Files.Writes.Should().Be(1);
    }

    [Theory]
    [InlineData(RequestOrigin.ActivatedVoice)]
    [InlineData(RequestOrigin.HostSystem)]
    public async Task FinalMutationIsNativeOnlyAndWrongChannelDoesNotConsumeReview(RequestOrigin origin)
    {
        await using var f = new Fixture();
        using var review = await f.Review();
        var save = () => f.Workflow.SaveAsNewAsync(review, origin, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        await save.Should().ThrowAsync<InvalidOperationException>();
        f.Files.Writes.Should().Be(0);
        review.IsCurrent.Should().BeTrue();
    }

    [Fact]
    public async Task WrongWorkflowInvalidOriginAndRetiredReviewsCannotWrite()
    {
        await using var f = new Fixture();
        await using var other = new Fixture();
        using var review = await f.Review();
        var foreign = () => other.Save(review);
        await foreign.Should().ThrowAsync<InvalidOperationException>();
        var invalid = () => f.Review(RequestOrigin.HostSystem);
        await invalid.Should().ThrowAsync<InvalidOperationException>();
        review.Dispose();
        review.IsCurrent.Should().BeFalse();
        var retired = () => f.Save(review);
        await retired.Should().ThrowAsync<InvalidOperationException>();
        f.Files.Writes.Should().Be(0);
    }

    [Fact]
    public async Task CancelCreatesNothingAndConsumesReview()
    {
        await using var f = new Fixture();
        using var review = await f.Review();
        var result = await f.Workflow.SaveAsNewAsync(review, RequestOrigin.LocalUi, _ => Task.FromResult(false), f.Eligible, CancellationToken.None);
        result.Should().Be(new AgentProfileWriteResult(AgentProfileWriteState.Cancelled, null));
        f.Audit.Events.Should().BeEmpty();
        f.Files.Writes.Should().Be(0);
        review.IsCurrent.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalAuthorityCannotBeRenewedByLaterNativeConfirmation(bool duringConfirmation)
    {
        await using var f = new Fixture();
        using var review = await f.Review(RequestOrigin.ActivatedVoice);
        if (!duringConfirmation) { f.Open = false; }
        var save = () => f.Workflow.SaveAsNewAsync(review, RequestOrigin.LocalUi, _ =>
        {
            f.Open = false;
            return Task.FromResult(true);
        }, () => true, CancellationToken.None);
        await save.Should().ThrowAsync<InvalidOperationException>();
        f.Files.Writes.Should().Be(0);
    }

    [Fact]
    public async Task CollisionIsFailureWithNoOverwriteLoop()
    {
        await using var f = new Fixture();
        using var review = await f.Review();
        f.Files.Conflict = true;
        (await f.Save(review)).State.Should().Be(AgentProfileWriteState.Conflict);
        f.Files.Writes.Should().Be(0);
        f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed);
        f.Audit.Events.Last().ReasonCode.Should().Be("destination-occupied");
    }

    [Theory]
    [InlineData(SecurityAuditOutcome.Requested)]
    [InlineData(SecurityAuditOutcome.Succeeded)]
    [InlineData(SecurityAuditOutcome.Unknown)]
    public async Task AuditFailureNeverBecomesSuccessOrRollback(SecurityAuditOutcome failure)
    {
        await using var f = new Fixture();
        using var review = await f.Review();
        f.Files.Unconfirmed = failure == SecurityAuditOutcome.Unknown;
        f.Audit.Fail = failure;
        var save = () => f.Save(review);
        if (failure == SecurityAuditOutcome.Requested)
        {
            await save.Should().ThrowAsync<IOException>();
            f.Files.Writes.Should().Be(0);
        }
        else
        {
            await save.Should().ThrowAsync<AgentProfilePublicationUnconfirmedException>();
            f.Files.Writes.Should().Be(1);
        }
        f.Audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrePublicationFailureAndCancellationAreNotSaved(bool cancelled)
    {
        await using var f = new Fixture();
        using var review = await f.Review();
        f.Files.Failure = cancelled ? new OperationCanceledException() : new IOException("Native write failed");
        var save = () => f.Save(review);
        await save.Should().ThrowAsync<Exception>();
        f.Files.Writes.Should().Be(0);
        f.Audit.Events.Last().Outcome.Should().Be(cancelled ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostPublicationRevocationAndIncorrectReadbackAreExplicitlyUncertain(bool invalidReadback)
    {
        await using var f = new Fixture();
        using var review = await f.Review();
        f.Files.InvalidReadback = invalidReadback;
        if (!invalidReadback) { f.Files.AfterWrite = () => f.Open = false; }
        var save = () => f.Save(review);
        await save.Should().ThrowAsync<AgentProfilePublicationUnconfirmedException>();
        f.Files.Writes.Should().Be(1);
        f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Unknown);
    }

    [Fact]
    public async Task CancellationIgnoringStorageRemainsOwnedUntilRealCompletion()
    {
        await using var f = new Fixture();
        using var review = await f.Review();
        using var cancellation = new CancellationTokenSource();
        f.Files.BeforeWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Files.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = f.Workflow.SaveAsNewAsync(review, RequestOrigin.LocalUi, _ => Task.FromResult(true), f.Eligible, cancellation.Token);
        await f.Files.BeforeWrite.Task;
        cancellation.Cancel();
        running.IsCompleted.Should().BeFalse();
        f.Files.Release.SetResult();
        var completion = async () => await running;
        await completion.Should().ThrowAsync<OperationCanceledException>();
        f.Files.Writes.Should().Be(0);
    }

    [Fact]
    public async Task ObservationAndDestinationRevocationDoNotDiscloseLateResults()
    {
        await using var f = new Fixture();
        f.Files.AfterObservation = () => f.Open = false;
        var list = () => f.Workflow.ListAsync(f.Eligible, CancellationToken.None);
        await list.Should().ThrowAsync<InvalidOperationException>();
        f.Open = true;
        var read = () => f.Workflow.ReadAsync("example", f.Eligible, CancellationToken.None);
        await read.Should().ThrowAsync<InvalidOperationException>();
        f.Open = true;
        var review = () => f.Review();
        await review.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MismatchedDurableDestinationOrBytesNeverProduceSuccess(bool changedBytes)
    {
        await using var f = new Fixture();
        using var review = await f.Review();
        f.Files.ChangeReadbackBytes = changedBytes;
        f.Files.ChangeReadbackDestination = !changedBytes;
        var save = () => f.Save(review);
        await save.Should().ThrowAsync<AgentProfilePublicationUnconfirmedException>();
        f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Unknown);
    }

    [Fact]
    public async Task StoreCancellationHasACancelledTypedOutcomeNotSuccessfulReadback()
    {
        await using var f = new Fixture();
        using var review = await f.Review();
        f.Files.Cancelled = true;
        (await f.Save(review)).State.Should().Be(AgentProfileWriteState.Cancelled);
        f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Cancelled);
        f.Files.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.ActivatedVoice, true)]
    public async Task ExistingOriginalReviewConfirmsExactBaseAndBytesWithoutCreatingAnotherCopy(RequestOrigin origin, bool bom)
    {
        await using var f = new Fixture();
        var original = await f.CreateOriginal(bom);
        var source = Source + "\nChanged inert guidance.";
        using var review = await f.Workflow.ReviewUpdateAsync(original, source, 2, origin, f.Eligible, CancellationToken.None);
        review.Original.Should().BeSameAs(original);
        review.Destination.Should().Be(original.Destination);
        review.Document.Document.HasBom.Should().Be(bom);
        f.Files.Writes.Should().Be(1);
        var wrongOperation = () => f.Save(review);
        await wrongOperation.Should().ThrowAsync<InvalidOperationException>();
        review.IsCurrent.Should().BeTrue();
        var result = await f.Workflow.SaveAsync(review, RequestOrigin.LocalUi, _ =>
        {
            f.Files.Writes.Should().Be(1);
            return Task.FromResult(true);
        }, f.Eligible, CancellationToken.None);
        result.State.Should().Be(AgentProfileWriteState.Saved);
        result.Snapshot!.Destination.Should().Be(original.Destination);
        result.Snapshot.Document.Document.CopyBytes().Should().Equal(review.Document.Document.CopyBytes());
        (await f.Workflow.ReadAsync("example", f.Eligible, CancellationToken.None)).Should().BeSameAs(result.Snapshot);
        f.Files.Creates.Should().Be(1);
        f.Files.Updates.Should().Be(1);
        f.Audit.Events.TakeLast(2).Select(item => item.ActionId).Should().Equal("definition.agent.update", "definition.agent.update");
        f.Audit.Events.Last().ReasonCode.Should().Be("exact-updated-file-readback");
        var replay = () => f.Workflow.SaveAsync(review, RequestOrigin.LocalUi, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        await replay.Should().ThrowAsync<InvalidOperationException>();
        var foreign = () => f.Workflow.ReviewUpdateAsync(original, source, 3, RequestOrigin.HostSystem, f.Eligible, CancellationToken.None);
        await foreign.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExistingUnchangedSourceRetainsBytesAndDoesNotPublish()
    {
        await using var f = new Fixture();
        var original = await f.CreateOriginal(bom: true);
        using var review = await f.Workflow.ReviewUpdateAsync(original, Source, 2, RequestOrigin.LocalUi, f.Eligible, CancellationToken.None);
        var result = await f.Workflow.SaveAsync(review, RequestOrigin.LocalUi, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        result.State.Should().Be(AgentProfileWriteState.Unchanged);
        result.Snapshot.Should().BeSameAs(original);
        f.Files.Writes.Should().Be(1);
        f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Succeeded);
        f.Audit.Events.Last().ReasonCode.Should().Be("exact-unchanged-file-readback");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidUpdateDraftCannotPrepareAReviewOrWrite(bool malformedUnicode)
    {
        await using var f = new Fixture();
        var original = await f.CreateOriginal();
        var source = malformedUnicode ? Source + "\ud800"
            : Source.Replace("kora-agent-format: 1", "kora-agent-format: 2", StringComparison.Ordinal);
        var review = () => f.Workflow.ReviewUpdateAsync(original, source, 2, RequestOrigin.LocalUi, f.Eligible, CancellationToken.None);
        await review.Should().ThrowAsync<InvalidDataException>();
        f.Files.Writes.Should().Be(1);
        f.Files.Updates.Should().Be(0);
        f.Audit.Events.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("draft")]
    [InlineData("session")]
    [InlineData("read-only-session")]
    [InlineData("foreign-session")]
    [InlineData("inactive-session")]
    [InlineData("privacy")]
    [InlineData("requested-audit")]
    [InlineData("failure")]
    [InlineData("cancelled-io")]
    [InlineData("readback")]
    [InlineData("terminal-audit")]
    [InlineData("conflict")]
    public async Task UpdateRetainsExactAdmissionAuditAndNoUnauthorizedWrite(string scenario)
    {
        await using var f = new Fixture();
        var original = await f.CreateOriginal();
        var current = true;
        using var review = await f.Workflow.ReviewUpdateAsync(original, Source + "\nDraft", 2,
            RequestOrigin.LocalUi, () => f.Open && current, CancellationToken.None);
        if (scenario is "draft") { current = false; }
        if (scenario is "session") { f.Files.BeforeCommit = () => f.Control.Authority = null; }
        if (scenario is "read-only-session") { f.Files.BeforeCommit = () => f.Control.Authority = f.Control.Authority! with { Generation = new(99) }; }
        if (scenario is "foreign-session") { f.Files.BeforeCommit = () => f.Control.Authority = f.Control.Authority! with { SessionId = new(Guid.NewGuid()) }; }
        if (scenario is "inactive-session") { f.Files.BeforeCommit = () => f.Control.Authority = f.Control.Authority! with { IsActive = false }; }
        if (scenario is "privacy") { f.Files.BeforeCommit = () => f.Open = false; }
        if (scenario is "requested-audit") { f.Audit.Fail = SecurityAuditOutcome.Requested; }
        if (scenario is "failure") { f.Files.Failure = new IOException("Synthetic failure"); }
        if (scenario is "cancelled-io") { f.Files.Failure = new OperationCanceledException(); }
        if (scenario is "readback") { f.Files.Unconfirmed = true; }
        if (scenario is "terminal-audit") { f.Audit.Fail = SecurityAuditOutcome.Succeeded; }
        if (scenario is "conflict") { f.Files.Conflict = true; }
        var save = () => f.Workflow.SaveAsync(review, RequestOrigin.LocalUi, _ => Task.FromResult(scenario is not "cancel"),
            f.Eligible, CancellationToken.None);
        if (scenario is "cancel" or "conflict")
        {
            (await save()).State.Should().Be(scenario is "cancel" ? AgentProfileWriteState.Cancelled : AgentProfileWriteState.Conflict);
            if (scenario is "conflict") { f.Audit.Events.Last().ReasonCode.Should().Be("original-base-changed"); }
        }
        else { await save.Should().ThrowAsync<Exception>(); }
        f.Files.Writes.Should().Be(scenario is "readback" or "terminal-audit" ? 2 : 1);
        f.Audit.Events.Skip(2).Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
        f.Files.Creates.Should().Be(1);
    }

    [Fact]
    public async Task UpdateCancellationIgnoringStorageJoinsBeforeReturningWithoutWriting()
    {
        await using var f = new Fixture();
        var original = await f.CreateOriginal();
        using var review = await f.Workflow.ReviewUpdateAsync(original, Source + "\nDraft", 2,
            RequestOrigin.LocalUi, f.Eligible, CancellationToken.None);
        f.Files.BeforeWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Files.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var saving = f.Workflow.SaveAsync(review, RequestOrigin.LocalUi, _ => Task.FromResult(true), f.Eligible, cancellation.Token);
        try
        {
            await f.Files.BeforeWrite.Task;
            cancellation.Cancel();
            saving.IsCompleted.Should().BeFalse();
        }
        finally { f.Files.Release.TrySetResult(); }
        var join = async () => await saving;
        await join.Should().ThrowAsync<OperationCanceledException>();
        f.Files.Writes.Should().Be(1);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal readonly Files Files = new();
        internal readonly Audit Audit = new();
        internal readonly InstructionControlAdmission Admission;
        internal readonly AgentProfileSaveWorkflow Workflow;
        internal readonly AudioControlTestStore Control = new();
        internal bool Open = true;
        internal bool Eligible() => Open;
        internal Fixture()
        {
            Admission = new(Control, Control, new HostTaskCoordinator(Control));
            Workflow = new(Files, Admission, Audit);
        }
        internal Task<AgentProfileSaveReview> Review(RequestOrigin origin = RequestOrigin.LocalUi) =>
            Workflow.ReviewAsync("example", Source, 1, origin, Eligible, CancellationToken.None);
        internal Task<AgentProfileWriteResult> Save(AgentProfileSaveReview review) =>
            Workflow.SaveAsNewAsync(review, RequestOrigin.LocalUi, _ => Task.FromResult(true), Eligible, CancellationToken.None);
        internal async Task<OwnedAgentProfileSnapshot> CreateOriginal(bool bom = false)
        {
            var destination = await Files.PrepareNew("example", CancellationToken.None);
            var document = OwnedAgentProfileDocument.Parse("example", Kora.Core.Artifacts.ArtifactFileDocument.Encode(Source, bom));
            using var review = await Review();
            _ = await Save(review);
            Files.Saved = new(destination, "pinned-file-1", document);
            return Files.Saved;
        }
        public ValueTask DisposeAsync() => Admission.DisposeAsync();
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal readonly List<SecurityAuditEvent> Events = [];
        internal SecurityAuditOutcome? Fail;
        public void Write(SecurityAuditEvent auditEvent)
        {
            if (Fail == auditEvent.Outcome) { throw new IOException("Audit unavailable"); }
            Events.Add(auditEvent);
        }
    }

    private sealed class Files : IOwnedAgentProfileStore
    {
        internal int Writes;
        internal int Creates;
        internal int Updates;
        internal bool Conflict;
        internal bool Unconfirmed;
        internal bool InvalidReadback;
        internal bool ChangeReadbackDestination;
        internal bool ChangeReadbackBytes;
        internal bool Cancelled;
        internal Exception? Failure;
        internal Action? AfterWrite;
        internal Action? AfterObservation;
        internal Action? BeforeCommit;
        internal TaskCompletionSource? BeforeWrite;
        internal TaskCompletionSource? Release;
        internal OwnedAgentProfileSnapshot? Saved;
        public Task<IReadOnlyList<string>> List(CancellationToken token)
        { AfterObservation?.Invoke(); return Task.FromResult<IReadOnlyList<string>>(Saved is null ? [] : ["example"]); }
        public Task<OwnedAgentProfileSnapshot?> Read(string name, CancellationToken token)
        { AfterObservation?.Invoke(); return Task.FromResult(Saved); }
        public Task<OwnedAgentProfileDestination> PrepareNew(string name, CancellationToken token)
        { AfterObservation?.Invoke(); return Task.FromResult(new OwnedAgentProfileDestination(name, "closed-owned-root\\example.agent.md", "pinned-root") { RootBindingIdentity = "pinned-ancestors" }); }
        public Task<AgentProfileWriteResult> Create(OwnedAgentProfileDestination destination, OwnedAgentProfileDocument document,
            Func<bool> eligible, Func<Func<AgentProfileWriteResult>, Task<AgentProfileWriteResult>> commit, CancellationToken token)
        {
            Creates++;
            return Write(destination, null, document, eligible, commit, token);
        }
        public Task<AgentProfileWriteResult> Update(OwnedAgentProfileSnapshot expected, OwnedAgentProfileDocument document,
            Func<bool> eligible, Func<Func<AgentProfileWriteResult>, Task<AgentProfileWriteResult>> commit, CancellationToken token)
        {
            Updates++;
            return Write(expected.Destination, expected, document, eligible, commit, token);
        }
        private async Task<AgentProfileWriteResult> Write(OwnedAgentProfileDestination destination, OwnedAgentProfileSnapshot? expected,
            OwnedAgentProfileDocument document,
            Func<bool> eligible, Func<Func<AgentProfileWriteResult>, Task<AgentProfileWriteResult>> commit, CancellationToken token)
        {
            BeforeWrite?.SetResult();
            if (Release is not null)
            {
#pragma warning disable VSTHRD003 // Deliberately cancellation-ignoring platform barrier.
                await Release.Task;
#pragma warning restore VSTHRD003
            }
            BeforeCommit?.Invoke();
            return await commit(() =>
            {
                token.ThrowIfCancellationRequested();
                if (!eligible()) { throw new InvalidOperationException("Admission revoked"); }
                if (Failure is not null) { throw Failure; }
                if (Conflict || expected is not null && !ReferenceEquals(expected, Saved))
                { return new(AgentProfileWriteState.Conflict, Saved); }
                if (Cancelled) { return new(AgentProfileWriteState.Cancelled, null); }
                if (expected is not null && document.Document.CopyBytes().AsSpan().SequenceEqual(expected.Document.Document.CopyBytes()))
                { return new(AgentProfileWriteState.Unchanged, Saved); }
                Writes++;
                Saved = new(destination, "pinned-file-" + Writes, document);
                AfterWrite?.Invoke();
                if (Unconfirmed) { throw new AgentProfilePublicationUnconfirmedException(new IOException("Readback failed")); }
                var observed = !ChangeReadbackDestination && !ChangeReadbackBytes ? Saved : Saved with
                {
                    Destination = ChangeReadbackDestination ? destination with { DirectoryIdentity = "changed" } : destination,
                    Document = ChangeReadbackBytes ? OwnedAgentProfileDocument.Parse("example", Source + "\nchanged") : document,
                };
                return new(AgentProfileWriteState.Saved, InvalidReadback ? null : observed);
            });
        }
    }
}

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

    private sealed class Fixture : IAsyncDisposable
    {
        internal readonly Files Files = new();
        internal readonly Audit Audit = new();
        internal readonly InstructionControlAdmission Admission;
        internal readonly AgentProfileSaveWorkflow Workflow;
        internal bool Open = true;
        internal bool Eligible() => Open;
        internal Fixture()
        {
            var control = new AudioControlTestStore();
            Admission = new(control, control, new HostTaskCoordinator(control));
            Workflow = new(Files, Admission, Audit);
        }
        internal Task<AgentProfileSaveReview> Review(RequestOrigin origin = RequestOrigin.LocalUi) =>
            Workflow.ReviewAsync("example", Source, 1, origin, Eligible, CancellationToken.None);
        internal Task<AgentProfileWriteResult> Save(AgentProfileSaveReview review) =>
            Workflow.SaveAsNewAsync(review, RequestOrigin.LocalUi, _ => Task.FromResult(true), Eligible, CancellationToken.None);
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
        internal bool Conflict;
        internal bool Unconfirmed;
        internal bool InvalidReadback;
        internal bool ChangeReadbackDestination;
        internal bool ChangeReadbackBytes;
        internal bool Cancelled;
        internal Exception? Failure;
        internal Action? AfterWrite;
        internal Action? AfterObservation;
        internal TaskCompletionSource? BeforeWrite;
        internal TaskCompletionSource? Release;
        private OwnedAgentProfileSnapshot? saved;
        public Task<IReadOnlyList<string>> List(CancellationToken token)
        { AfterObservation?.Invoke(); return Task.FromResult<IReadOnlyList<string>>(saved is null ? [] : ["example"]); }
        public Task<OwnedAgentProfileSnapshot?> Read(string name, CancellationToken token)
        { AfterObservation?.Invoke(); return Task.FromResult(saved); }
        public Task<OwnedAgentProfileDestination> PrepareNew(string name, CancellationToken token)
        { AfterObservation?.Invoke(); return Task.FromResult(new OwnedAgentProfileDestination(name, "closed-owned-root\\example.agent.md", "pinned-root")); }
        public async Task<AgentProfileWriteResult> Create(OwnedAgentProfileDestination destination, OwnedAgentProfileDocument document,
            Func<bool> eligible, Func<Func<AgentProfileWriteResult>, Task<AgentProfileWriteResult>> commit, CancellationToken token)
        {
            BeforeWrite?.SetResult();
            if (Release is not null)
            {
#pragma warning disable VSTHRD003 // Deliberately cancellation-ignoring platform barrier.
                await Release.Task;
#pragma warning restore VSTHRD003
            }
            return await commit(() =>
            {
                token.ThrowIfCancellationRequested();
                if (!eligible()) { throw new InvalidOperationException("Admission revoked"); }
                if (Failure is not null) { throw Failure; }
                if (Conflict) { return new(AgentProfileWriteState.Conflict, null); }
                if (Cancelled) { return new(AgentProfileWriteState.Cancelled, null); }
                Writes++;
                saved = new(destination, "pinned-file", document);
                AfterWrite?.Invoke();
                if (Unconfirmed) { throw new AgentProfilePublicationUnconfirmedException(new IOException("Readback failed")); }
                var observed = !ChangeReadbackDestination && !ChangeReadbackBytes ? saved : saved with
                {
                    Destination = ChangeReadbackDestination ? destination with { DirectoryIdentity = "changed" } : destination,
                    Document = ChangeReadbackBytes ? OwnedAgentProfileDocument.Parse("example", Source + "\nchanged") : document,
                };
                return new(AgentProfileWriteState.Saved, InvalidReadback ? null : observed);
            });
        }
    }
}

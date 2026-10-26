using System.Diagnostics;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task PendingOrdinaryTurnsAcceptBeforePhysicalReleaseThenRunOriginalBytesExactlyOnceInFifo(RequestOrigin origin)
    {
        using var f = new Fixture(origin) { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var completions = new System.Collections.Concurrent.ConcurrentBag<ConversationTurnCompletion>();
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.SubmittedConversationCompleted += (_, completion) =>
        {
            completions.Add(completion);
            if (completions.Count == 3) { published.TrySetResult(); }
        };
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var a = await service.SubmitQueuedAsync(target, "first", () => true, Token, origin: origin);
        var snapshots = new List<ConversationPendingSnapshot>();
        service.PendingConversationChanged += snapshots.Add;
        var stopped = new System.Collections.Concurrent.ConcurrentBag<Activity>();
        using var tracing = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(tracing);
        ActivityContext admittedContext = default;
        var b = await service.SubmitQueuedAsync(target, " B\r\n e\u0301 ", () => true, Token, origin: origin,
            accepted: () => admittedContext = HostActivity.RequireCurrent().Activity!.Context);
        var c = await service.SubmitQueuedAsync(target, "third", () => true, Token, origin: origin);
        b.Position.Should().Be(1);
        c.Position.Should().Be(2);
        b.Receipt.Task.State.Should().Be(HostTaskState.IntentRecorded);
        b.Completion.IsCompleted.Should().BeFalse();
        f.Adapter.Calls.Should().Be(1);
        var wires = new List<string>();
        f.Adapter.OnRun = () => wires.Add(JsonSerializer.Deserialize<JsonElement>(f.Adapter.Wire!).GetProperty("UserRequest").GetString()!);
        f.Adapter.Pending = null;
        provider.SetResult(LateAnswer());
        (await a.Completion).RequireReceipt().Task.State.Should().Be(HostTaskState.Succeeded);
        (await b.Completion).RequireReceipt().Message.Body.Should().Be(" B\r\n e\u0301 ");
        (await c.Completion).RequireReceipt().Message.Request.Origin.Should().Be(origin);
        wires.Should().Equal(" B\r\n e\u0301 ", "third");
        snapshots.SelectMany(item => item.Turns).Should().Contain(item => item.Submission == b.Receipt && item.Position == 1);
        f.PendingRecords.Values.Should().OnlyContain(item => item.Message.AssistantResponse != null && item.Message.Outcome == ModelTurnOutcome.Succeeded);
        f.Messages.Should().NotContain(text => text.Contains(" B\r\n", StringComparison.Ordinal));
        stopped.Should().Contain(activity => activity.OperationName == "session.request" && activity.ParentId == null
            && activity.Links.Any(link => link.Context == admittedContext));
        stopped.SelectMany(activity => activity.TagObjects).Should().NotContain(tag => Equals(tag.Value, b.Receipt.Message.Body));
        stopped.SelectMany(activity => activity.Baggage).Should().BeEmpty();
        (await service.ReadPendingConversationAsync(target, () => true, Token)).Turns.Should().BeEmpty();
        await published.Task.WaitAsync(Token);
        completions.Should().HaveCount(3).And.OnlyContain(item => item.Failure == null && item.Receipt!.Task.State == HostTaskState.Succeeded);
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task PendingCancellationCommitsOnlyExactOriginalCancelledIntentWithoutResourcesOrProviderAndPausesTheRest(RequestOrigin origin)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var observations = new System.Collections.Concurrent.ConcurrentBag<ConversationPendingSnapshot>();
        service.PendingConversationChanged += observations.Add;
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var a = await service.SubmitQueuedAsync(target, "active", () => true, Token);
        var b = await service.SubmitQueuedAsync(target, "cancel me", () => true, Token);
        var c = await service.SubmitQueuedAsync(target, "keep me", () => true, Token);
        var snapshot = await service.ReadPendingConversationAsync(target, () => true, Token);
        var exact = snapshot.Turns.Single(item => item.Submission == b.Receipt);
        var leases = f.Audits.Count(item => item.ActionId is "runtime.resource.acquire");
        var cancelled = await service.CancelPendingConversationAsync(target, exact, snapshot.Revision, () => true, Token, origin);
        cancelled.Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        cancelled.Message.Execution.Should().BeNull();
        cancelled.Task.State.Should().Be(HostTaskState.Cancelled);
        f.Adapter.Calls.Should().Be(1);
        f.Audits.Count(item => item.ActionId is "runtime.resource.acquire").Should().Be(leases);
        a.Completion.IsCompleted.Should().BeFalse();
        c.Completion.IsCompleted.Should().BeFalse();
        await service.Invoking(value => value.CancelPendingConversationAsync(target, exact, snapshot.Revision, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Adapter.Pending = null;
        provider.SetResult(LateAnswer());
        await a.Completion;
        c.Completion.IsCompleted.Should().BeFalse("pending cancellation pauses addressed dispatch, not just the cancelled task");
        var held = await service.ReadPendingConversationAsync(target, () => true, Token);
        held.Paused.Should().BeTrue();
        await service.ResumePendingConversationAsync(target, held.Revision, () => true, Token, origin);
        (await c.Completion).RequireReceipt().Task.State.Should().Be(HostTaskState.Succeeded);
        (await service.ReadPendingConversationAsync(target, () => true, Token)).Turns.Should().BeEmpty();
        observations.Should().Contain(item => item.Paused).And.Contain(item => !item.Paused && item.Turns.Length == 0);
    }

    [Fact]
    public async Task OriginalContextDeadlineExpiresPendingIntentWithoutRenewalOrWaitingForActiveProvider()
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var a = await service.SubmitQueuedAsync(target, "active", () => true, Token);
        var b = await service.SubmitQueuedAsync(target, "original expiring authority", () => true, Token);
        f.Time.FireDeadline();
        var expired = (await b.Completion).RequireReceipt();
        expired.Message.Reason.Should().Be(ModelTurnReason.ContextExpired);
        expired.Message.Outcome.Should().Be(ModelTurnOutcome.Denied);
        expired.Message.Execution.Should().BeNull();
        f.Adapter.Calls.Should().Be(1);
        a.Completion.IsCompleted.Should().BeFalse();
        provider.SetResult(LateAnswer());
        await a.Completion;
    }

    [Theory]
    [InlineData("source")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("metadata")]
    [InlineData("policy")]
    public async Task ChangedQueuedAuthorityCannotDispatchOrRetainAnAnswer(string fault)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var a = await service.SubmitQueuedAsync(target, "active", () => true, Token);
        var source = true;
        var b = await service.SubmitQueuedAsync(target, "bound to original authority", () => source, Token);
        if (fault is "source") { source = false; }
        if (fault is "private") { f.CanControl = false; }
        if (fault is "control") { f.ControlRevision++; }
        if (fault is "session") { f.Session = f.Session with { IsActive = false }; }
        if (fault is "generation") { f.Session = f.Session with { Generation = new(2) }; }
        if (fault is "metadata") { f.Metadata = new(f.Session.SessionId, new(1), new("changed")); }
        if (fault is "policy")
        {
            using var root = f.Root();
            await host.SetPolicyAsync(new(f.Session.SessionId, new(2), ModelProviderMode.LocalOnly,
                ModelProviderSelection.OllamaCandidate, ModelProviderSelection.CopilotCandidate), Token);
        }
        provider.SetResult(LateAnswer());
        await a.Completion;
        if (fault is "source")
        {
            (await b.Completion).Failure.Should().BeOfType<ConversationSubmissionUnconfirmedException>();
        }
        else
        {
            if (fault is "private")
            {
                await service.Invoking(value => value.ReadPendingConversationAsync(new(f.Session, f.Metadata), () => true, Token))
                    .Should().ThrowAsync<InvalidOperationException>();
            }
            else
            {
                var observed = await service.ReadPendingConversationAsync(new(f.Session, f.Metadata), () => true, Token);
                observed.Paused.Should().BeTrue();
            }
        }
        f.Adapter.Calls.Should().Be(1);
        f.PendingRecords[b.Receipt.Message.Request.RequestId].Message.Execution.Should().BeNull();
        f.PendingRecords[b.Receipt.Message.Request.RequestId].Message.AssistantResponse.Should().BeNull();
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("foreign")]
    [InlineData("active")]
    [InlineData("private")]
    [InlineData("origin")]
    [InlineData("missing-audit")]
    [InlineData("audit-request")]
    [InlineData("audit-outcome")]
    [InlineData("audit-revocation")]
    [InlineData("audit-success-revocation")]
    public async Task PendingCancellationFailsClosedForStaleForeignActiveAndUnauditedControl(string fault)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host, cancellationAuditAvailable: fault is not "missing-audit");
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var a = await service.SubmitQueuedAsync(target, "active", () => true, Token);
        var b = await service.SubmitQueuedAsync(target, "pending", () => true, Token);
        var snapshot = await service.ReadPendingConversationAsync(target, () => true, Token);
        var exact = snapshot.Turns.Last();
        if (fault is "active") { exact = snapshot.Turns.First(); }
        if (fault is "foreign") { exact = exact with { Submission = a.Receipt }; }
        if (fault is "private") { f.CanControl = false; }
        if (fault is "audit-request") { f.CancelAudit.FailAt = 0; }
        if (fault is "audit-outcome") { f.CancelAudit.FailAt = 1; }
        if (fault is "audit-revocation") { f.CancelAudit.OnWrite = _ => f.ControlRevision++; }
        if (fault is "audit-success-revocation")
        {
            f.CancelAudit.OnWrite = audit =>
            { if (audit.Outcome == Kora.Core.Auditing.SecurityAuditOutcome.Succeeded) { f.ControlRevision++; } };
        }
        await service.Invoking(value => value.CancelPendingConversationAsync(target, exact,
            snapshot.Revision + (fault is "revision" ? 1 : 0), () => true, Token,
            fault is "origin" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi)).Should().ThrowAsync<Exception>();
        b.Completion.IsCompleted.Should().BeFalse();
        f.Adapter.Calls.Should().Be(1);
        provider.SetResult(LateAnswer());
        await a.Completion;
    }

    [Fact]
    public async Task QueueDisposalJoinsIgnoredActiveCancellationAndPreventsPendingOrLateAnswerPublication()
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        var service = f.ConversationService(host, execution: host);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var a = await service.SubmitQueuedAsync(target, "active", () => true, Token);
        var b = await service.SubmitQueuedAsync(target, "never dispatch", () => true, Token);
        var disposal = service.DisposeAsync().AsTask();
        disposal.IsCompleted.Should().BeFalse("the physical provider ignores cancellation");
        provider.SetResult(LateAnswer());
        await disposal;
        await service.DisposeAsync();
        f.Adapter.Calls.Should().Be(1);
        (await a.Completion).Failure.Should().NotBeNull();
        (await b.Completion).Failure.Should().NotBeNull();
        f.PendingRecords.Values.Should().OnlyContain(item => item.Message.AssistantResponse == null);
        await service.Invoking(value => value.SubmitQueuedAsync(target, "after disposal", () => true, Token))
            .Should().ThrowAsync<ObjectDisposedException>();
        await service.Invoking(value => value.ReadPendingConversationAsync(target, () => true, Token))
            .Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task PendingCapacityRefusesTheEleventhIntentWithoutRetentionEvictionOrProviderAdmission()
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var active = await service.SubmitQueuedAsync(target, "active", () => true, Token);
        try
        {
            for (var i = 0; i < SessionQueueLimits.MaximumPendingPerSession; i++)
            { (await service.SubmitQueuedAsync(target, "pending " + i, () => true, Token)).Position.Should().Be(i + 1); }
            var retained = f.RetainCalls;
            await service.Invoking(value => value.SubmitQueuedAsync(target, "over capacity", () => true, Token))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*full*");
            f.RetainCalls.Should().Be(retained);
            f.Adapter.Calls.Should().Be(1);
        }
        finally { provider.SetResult(LateAnswer()); await active.Completion; }
    }

    [Theory]
    [InlineData("private")]
    [InlineData("source")]
    [InlineData("metadata")]
    [InlineData("inactive")]
    [InlineData("missing")]
    [InlineData("revision")]
    [InlineData("not-paused")]
    public async Task ResumeCannotRenewStaleUnavailableForeignOrAlreadyOpenQueueAuthority(string fault)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var target = new SessionWorkspaceEntry(f.Session, null);
        if (fault is not "missing")
        {
            f.Adapter.Reply = new(fault is "not-paused" ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.Unknown,
                ModelProviderSelection.OllamaCandidate, fault is "not-paused" ? new("answer", null) : null);
            await service.SubmitAsync(target, "original", () => true, Token);
        }
        var observed = await service.ReadPendingConversationAsync(target, () => true, Token);
        if (fault is "private") { f.CanControl = false; }
        if (fault is "metadata") { f.Metadata = new(f.Session.SessionId, new(1), new("changed")); }
        if (fault is "inactive") { target = new(f.Session = f.Session with { IsActive = false }, null); }
        await service.Invoking(value => value.ResumePendingConversationAsync(target,
            observed.Revision + (fault is "revision" ? 1 : 0), () => fault is not "source", Token))
            .Should().ThrowAsync<InvalidOperationException>();
        new ConversationTurnCompletion(null, null).Invoking(value => value.RequireReceipt())
            .Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("inactive")]
    [InlineData("metadata")]
    public async Task PendingCancellationRequiresTheActualCurrentActiveSessionAndIssuedLane(string fault)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var target = new SessionWorkspaceEntry(f.Session, null);
        var completed = await service.SubmitAsync(target, "original", () => true, Token);
        var observed = await service.ReadPendingConversationAsync(target, () => true, Token);
        var expected = new ConversationPendingTurn(completed, f.Time.Now.AddSeconds(15), ConversationPendingPhase.Pending, 1);
        if (fault is "missing")
        {
            await using var other = f.ConversationService(host, execution: host);
            await other.Invoking(value => value.CancelPendingConversationAsync(target, expected, 0, () => true, Token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            if (fault is "inactive") { target = new(f.Session = f.Session with { IsActive = false }, null); }
            if (fault is "metadata") { f.Metadata = new(f.Session.SessionId, new(1), new("changed")); }
            await service.Invoking(value => value.CancelPendingConversationAsync(target, expected, observed.Revision, () => true, Token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task ExactRequestCommandCanAcceptPendingTurnWithoutRetiringAlreadyAcceptedOriginalAuthority(RequestOrigin origin)
    {
        using var f = new Fixture(origin) { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var workspace = new SessionWorkspaceService(f, new(f), f, Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionWorkspaceService>.Instance);
        var commands = new SessionRequestCommandService(workspace, service, f, f.Time);
        var id = f.Session.SessionId.Value.ToString("D");
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var firstReview = await commands.ExecuteAsync(SessionCommand.Parse("session request review " + id + " 1 0 -- first", "Kora")!,
            origin, () => true, Token);
        var first = commands.ExecuteAsync(SessionCommand.Parse("session request confirm " + id + " " + firstReview.RequestReview!.Id, "Kora")!,
            origin, () => true, Token);
        try
        {
            f.Adapter.Calls.Should().Be(1);
            var secondReview = await commands.ExecuteAsync(SessionCommand.Parse("session request review " + id + " 1 0 -- second", "Kora")!,
                origin, () => true, Token);
            var second = await commands.ExecuteAsync(SessionCommand.Parse("session request confirm " + id + " " + secondReview.RequestReview!.Id, "Kora")!,
                origin, () => true, Token);
            second.RequestSubmission!.Message.Outcome.Should().BeNull();
            second.Message.Should().Contain("pending").And.NotContain("terminal");
            f.Adapter.Calls.Should().Be(1);
            f.Adapter.Pending = null;
            provider.SetResult(LateAnswer());
            (await first).RequestSubmission!.Task.State.Should().Be(HostTaskState.Succeeded);
        }
        finally { provider.TrySetResult(LateAnswer()); }
    }

    [Fact]
    public async Task RevokedOriginalProviderProfileBeforeQueuedDispatchIsUnavailableWithoutAnotherProviderCall()
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var first = await service.SubmitQueuedAsync(target, "active", () => true, Token);
        var second = await service.SubmitQueuedAsync(target, "original provider only", () => true, Token);
        f.BeforeExecutionComplete = () => f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Unavailable;
        provider.SetResult(LateAnswer());
        (await first.Completion).RequireReceipt().Task.State.Should().Be(HostTaskState.Succeeded);
        var denied = (await second.Completion).RequireReceipt();
        denied.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        denied.Message.Reason.Should().Be(ModelTurnReason.UnregisteredModel);
        denied.Message.Execution.Should().BeNull();
        denied.Message.AssistantResponse.Should().BeNull();
        f.Adapter.Calls.Should().Be(1);
        await service.Invoking(value => value.ReadPendingConversationAsync(target with
            { Authority = target.Authority with { Generation = new(2) } }, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExplicitResumeWithoutAnyPresenterStillClearsOnlyTheConfirmedFailureDispatchHold()
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        f.Adapter.Reply = new(ModelTurnOutcome.Denied, ModelProviderSelection.OllamaCandidate);
        var target = new SessionWorkspaceEntry(f.Session, null);
        (await service.SubmitAsync(target, "original failed request", () => true, Token))
            .Message.Outcome.Should().Be(ModelTurnOutcome.Denied);
        var held = await service.ReadPendingConversationAsync(target, () => true, Token);
        held.Paused.Should().BeTrue();
        await service.ResumePendingConversationAsync(target, held.Revision, () => true, Token);
        var resumed = await service.ReadPendingConversationAsync(target, () => true, Token);
        resumed.Paused.Should().BeFalse();
        resumed.Turns.Should().BeEmpty();
        resumed.ControlRevision.Should().Be(f.ControlRevision);
        f.Adapter.Calls.Should().Be(1, "resume is neither replay nor a provider call");
    }
}

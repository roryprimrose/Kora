using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task ManualHoldPreventsActualNextDispatchAfterSuccessfulPhysicalJoinAndReleaseContinuesOriginalFifo(RequestOrigin origin)
    {
        using var f = new Fixture(origin) { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var first = await service.SubmitQueuedAsync(target, "running original", () => true, Token, origin: origin);
        var second = await service.SubmitQueuedAsync(target, " second immutable\r\n", () => true, Token, origin: origin);
        try
        {
            var before = await service.ReadPendingConversationAsync(target, () => true, Token);
            var running = await service.ReadRunningConversationAsync(target, () => true, Token);
            var leases = f.Audits.Count(item => item.ActionId is "runtime.resource.acquire");
            await service.SetManualDispatchHoldAsync(before.DispatchControl!, ConversationDispatchHoldReason.OriginalUser,
                () => true, Token, origin);
            (await service.ReadRunningConversationAsync(target, () => true, Token)).Should().Be(running);
            f.Adapter.RunToken.IsCancellationRequested.Should().BeFalse();
            first.Completion.IsCompleted.Should().BeFalse();
            var third = await service.SubmitQueuedAsync(target, "third original", () => true, Token, origin: origin);
            third.Position.Should().Be(2);
            var held = await service.ReadPendingConversationAsync(target, () => true, Token);
            held.ManualHoldReason.Should().Be(ConversationDispatchHoldReason.OriginalUser);
            held.Paused.Should().BeFalse("a healthy deliberate hold is not a recovery hold");
            held.Turns.Single(item => item.Submission == second.Receipt).ExpiresAt
                .Should().Be(before.Turns.Single(item => item.Submission == second.Receipt).ExpiresAt);
            f.Audits.Count(item => item.ActionId is "runtime.resource.acquire").Should().Be(leases);
            f.Adapter.Pending = null;
            provider.SetResult(LateAnswer());
            (await first.Completion).RequireReceipt().Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
            (await service.ReadRunningConversationAsync(target, () => true, Token)).Should().BeNull();
            host.IsQuiescent.Should().BeTrue();
            f.Adapter.Calls.Should().Be(1);
            second.Completion.IsCompleted.Should().BeFalse();
            third.Completion.IsCompleted.Should().BeFalse();
            var joined = await service.ReadPendingConversationAsync(target, () => true, Token);
            joined.Turns.Select(item => item.Position).Should().Equal(1, 2);
            joined.Turns.Should().OnlyContain(item => item.Phase == ConversationPendingPhase.Pending);
            var order = new List<string>();
            f.Adapter.OnRun = () => order.Add(System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                f.Adapter.Wire!).GetProperty("UserRequest").GetString()!);
            await service.SetManualDispatchHoldAsync(joined.DispatchControl!, ConversationDispatchHoldReason.None,
                () => true, Token, origin);
            (await second.Completion).RequireReceipt().Message.Body.Should().Be(" second immutable\r\n");
            (await third.Completion).RequireReceipt().Message.Request.Origin.Should().Be(origin);
            order.Should().Equal(" second immutable\r\n", "third original");
            f.Adapter.Calls.Should().Be(3);
            f.CancelAudit.Events.Where(item => item.ActionId.StartsWith("conversation.manual-dispatch", StringComparison.Ordinal))
                .Should().HaveCount(4).And.OnlyContain(item => item.Initiator ==
                    (origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser));
            f.Messages.Should().NotContain(item => item.Contains("immutable", StringComparison.Ordinal));
        }
        finally { provider.TrySetResult(LateAnswer()); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClearingEitherManualOrRecoveryHoldCannotClearTheOtherOrDispatchPendingWork(bool manualFirst)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var target = new SessionWorkspaceEntry(f.Session, null);
        var before = await service.ReadPendingConversationAsync(target, () => true, Token);
        await service.SetManualDispatchHoldAsync(before.DispatchControl!, ConversationDispatchHoldReason.OriginalUser, () => true, Token);
        var withdrawn = await service.SubmitQueuedAsync(target, "cancel only this pending request", () => true, Token);
        var kept = await service.SubmitQueuedAsync(target, "keep original", () => true, Token);
        var observed = await service.ReadPendingConversationAsync(target, () => true, Token);
        await service.CancelPendingConversationAsync(target, observed.Turns.First(), observed.Revision, () => true, Token);
        (await withdrawn.Completion).RequireReceipt().Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        var mixed = await service.ReadPendingConversationAsync(target, () => true, Token);
        mixed.Paused.Should().BeTrue();
        mixed.ManualHoldReason.Should().Be(ConversationDispatchHoldReason.OriginalUser);
        if (manualFirst)
        { await service.SetManualDispatchHoldAsync(mixed.DispatchControl!, ConversationDispatchHoldReason.None, () => true, Token); }
        else
        { await service.ResumePendingConversationAsync(target, mixed.Revision, () => true, Token); }
        var remaining = await service.ReadPendingConversationAsync(target, () => true, Token);
        remaining.Paused.Should().Be(manualFirst);
        remaining.ManualHoldReason.Should().Be(manualFirst ? ConversationDispatchHoldReason.None : ConversationDispatchHoldReason.OriginalUser);
        kept.Completion.IsCompleted.Should().BeFalse();
        f.Adapter.Calls.Should().Be(0);
        if (manualFirst)
        { await service.ResumePendingConversationAsync(target, remaining.Revision, () => true, Token); }
        else
        { await service.SetManualDispatchHoldAsync(remaining.DispatchControl!, ConversationDispatchHoldReason.None, () => true, Token); }
        (await kept.Completion).RequireReceipt().Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        f.Adapter.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task InterleavedExpiredFixtureCompletionsKeepEachOriginalMessageAndTaskPair(RequestOrigin origin)
    {
        using var f = new Fixture(origin) { TrackPendingRecords = true };
        var head = await f.RetainConversationMessageAsync(f.Request, f.Session.Generation, 0,
            " first immutable\r\n", () => true, Token);
        var nextRequest = new HostRequest(new(Guid.NewGuid()), f.Session.SessionId, new(Guid.NewGuid()), origin);
        var next = await f.RetainConversationMessageAsync(nextRequest, f.Session.Generation, 0,
            "second immutable", () => true, Token);
        Task<ConversationSubmissionReceipt>? interleaved = null;
        f.BeforeSubmissionComplete = request =>
        {
            request.Should().BeSameAs(head.Message.Request);
            f.BeforeSubmissionComplete = null;
            interleaved = f.CompleteConversationSubmissionAsync(nextRequest, ModelTurnOutcome.Denied,
                ModelTurnReason.ContextExpired, () => true, Token).AsTask();
        };
        var completed = await f.CompleteConversationSubmissionAsync(head.Message.Request, ModelTurnOutcome.Denied,
            ModelTurnReason.ContextExpired, () => true, Token);
        interleaved.Should().NotBeNull();
        var nextCompleted = await interleaved!;
        f.PendingRecords.Should().HaveCount(2);
        foreach (var (original, terminal) in new[] { (head, completed), (next, nextCompleted) })
        {
            terminal.Message.Should().Be(original.Message with
                { Revision = 2, Outcome = ModelTurnOutcome.Denied, Reason = ModelTurnReason.ContextExpired });
            terminal.Message.Request.Should().BeSameAs(original.Message.Request);
            terminal.Task.Should().Be(original.Task.Next(HostTaskState.Denied));
            terminal.Task.Request.Should().BeSameAs(original.Task.Request);
            f.PendingRecords[original.Message.Request.RequestId].Should().Be(terminal);
            (await f.ReadConversationMessageAsync(original.Message.Request.SessionId,
                original.Message.Request.RequestId, Token)).Should().Be(terminal.Message);
            (await f.ReadTaskAsync(original.Task.Request.SessionId, original.Task.Request.TaskId, Token))!
                .Task.Should().Be(terminal.Task);
        }
        f.Adapter.Calls.Should().Be(0);
        f.ExecutionStarts.Should().Be(0);
        f.ExecutionCompletions.Should().Be(0);
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("source")]
    [InlineData("provider")]
    [InlineData("permission")]
    public async Task ManualReleaseCannotRenewOrSkipInvalidOriginalHeadAuthority(string fault)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var target = new SessionWorkspaceEntry(f.Session, null);
        var empty = await service.ReadPendingConversationAsync(target, () => true, Token);
        await service.SetManualDispatchHoldAsync(empty.DispatchControl!, ConversationDispatchHoldReason.OriginalUser, () => true, Token);
        var source = true;
        var head = await service.SubmitQueuedAsync(target, "original bound head", () => source, Token);
        var next = await service.SubmitQueuedAsync(target, "later original", () => true, Token);
        var held = await service.ReadPendingConversationAsync(target, () => true, Token);
        var originalNow = f.Time.GetUtcNow();
        if (fault is "expiry")
        {
            held.Turns.Should().HaveCount(2).And.OnlyContain(item =>
                item.ExpiresAt == item.Submission.Message.SubmittedAt.AddSeconds(15));
            head.Completion.IsCompleted.Should().BeFalse();
            next.Completion.IsCompleted.Should().BeFalse();
        }
        if (fault is "expiry") { f.Time.Now = f.Time.Now.AddSeconds(15); }
        if (fault is "source") { source = false; }
        if (fault is "provider") { f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Unavailable; }
        if (fault is "permission")
        {
            using var root = f.Root();
            await host.SetPolicyAsync(new(f.Session.SessionId, new(2), ModelProviderMode.LocalOnly,
                ModelProviderSelection.OllamaCandidate, ModelProviderSelection.CopilotCandidate), Token);
        }
        await service.SetManualDispatchHoldAsync(held.DispatchControl!, ConversationDispatchHoldReason.None, () => true, Token);
        var terminal = await head.Completion;
        if (fault is "source") { terminal.Failure.Should().BeOfType<ConversationSubmissionUnconfirmedException>(); }
        else
        {
            var denied = terminal.RequireReceipt();
            denied.Message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
            denied.Message.Execution.Should().BeNull();
            if (fault is "expiry") { denied.Message.Reason.Should().Be(ModelTurnReason.ContextExpired); }
        }
        f.Adapter.Calls.Should().Be(0);
        f.PendingRecords[head.Receipt.Message.Request.RequestId].Message.AssistantResponse.Should().BeNull();
        var after = await service.ReadPendingConversationAsync(target, () => true, Token);
        after.Paused.Should().BeTrue();
        if (fault is "expiry")
        {
            f.Time.GetUtcNow().Should().Be(originalNow.AddSeconds(15));
            var later = (await next.Completion).RequireReceipt();
            later.Message.Outcome.Should().Be(ModelTurnOutcome.Denied);
            later.Message.Reason.Should().Be(ModelTurnReason.ContextExpired);
            later.Message.Request.Should().BeSameAs(next.Receipt.Message.Request);
            later.Message.Body.Should().Be(next.Receipt.Message.Body);
            later.Message.Execution.Should().BeNull();
            later.Message.AssistantResponse.Should().BeNull();
            f.PendingRecords.Should().HaveCount(2);
            foreach (var original in new[] { head, next })
            {
                var completed = (await original.Completion).RequireReceipt();
                completed.Message.Should().Be(original.Receipt.Message with
                    { Revision = 2, Outcome = ModelTurnOutcome.Denied, Reason = ModelTurnReason.ContextExpired });
                completed.Task.Should().Be(original.Receipt.Task.Next(HostTaskState.Denied));
                var retained = f.PendingRecords[original.Receipt.Message.Request.RequestId];
                retained.Should().Be(completed);
                retained.Message.Request.Should().BeSameAs(original.Receipt.Message.Request);
                retained.Task.Request.Should().BeSameAs(original.Receipt.Task.Request);
                retained.Message.SubmittedAt.Should().Be(original.Receipt.Message.SubmittedAt);
                retained.Message.SubmittedAt.Offset.Should().Be(TimeSpan.Zero);
                retained.Message.Generation.Should().Be(original.Receipt.Message.Generation);
                retained.Message.Sequence.Should().Be(original.Receipt.Message.Sequence);
                retained.Message.Version.Should().Be(1);
                retained.Message.Revision.Should().Be(2);
                retained.Task.Revision.Value.Should().Be(2);
                retained.Task.State.Should().Be(HostTaskState.Denied);
                retained.Message.Outcome.Should().Be(ModelTurnOutcome.Denied);
                retained.Message.Body.Should().Be(original.Receipt.Message.Body);
                retained.Message.Reason.Should().Be(ModelTurnReason.ContextExpired);
                retained.Message.Execution.Should().BeNull();
                retained.Message.AssistantResponse.Should().BeNull();
                (await f.ReadConversationMessageAsync(target.Authority.SessionId,
                    original.Receipt.Message.Request.RequestId, Token)).Should().Be(retained.Message);
                (await f.ReadTaskAsync(target.Authority.SessionId, original.Receipt.Task.Request.TaskId, Token))!
                    .Task.Should().Be(retained.Task);
            }
            f.Time.GetUtcNow().Should().Be(originalNow.AddSeconds(15));
            (await service.ReadPendingConversationAsync(target, () => true, Token)).Turns.Should().BeEmpty();
            f.ExecutionStarts.Should().Be(0);
            f.ExecutionCompletions.Should().Be(0);
            f.Adapter.Calls.Should().Be(0);
        }
        else { next.Completion.IsCompleted.Should().BeFalse("an invalid head cannot be skipped"); }
    }

    [Theory]
    [InlineData("source")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("metadata")]
    [InlineData("inactive")]
    [InlineData("admission")]
    [InlineData("revision")]
    [InlineData("foreign")]
    [InlineData("missing")]
    [InlineData("origin")]
    [InlineData("enum")]
    [InlineData("cancelled")]
    [InlineData("missing-audit")]
    [InlineData("audit-request")]
    [InlineData("audit-outcome")]
    [InlineData("audit-revocation")]
    [InlineData("audit-success-revocation")]
    [InlineData("read-revocation")]
    [InlineData("disposed")]
    public async Task ManualHoldRejectsStaleForeignUnauditedOrRetiredHostControlWithoutEffects(string fault)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host, cancellationAuditAvailable: fault is not "missing-audit");
        var target = new SessionWorkspaceEntry(f.Session, null);
        var snapshot = await service.ReadPendingConversationAsync(target, () => true, Token);
        var exact = snapshot.DispatchControl!;
        if (fault is "private") { f.Inspect = false; }
        if (fault is "control") { f.ControlRevision++; }
        if (fault is "metadata") { f.Metadata = new(f.Session.SessionId, new(1), new("changed")); }
        if (fault is "inactive") { f.Session = f.Session with { IsActive = false }; }
        if (fault is "admission") { exact = exact with { AdmissionId = Guid.NewGuid() }; }
        if (fault is "revision") { exact = exact with { LaneRevision = exact.LaneRevision + 1 }; }
        if (fault is "foreign") { exact = exact with { Target = target with { Authority = target.Authority with { SessionId = new(Guid.NewGuid()) } } }; }
        if (fault is "missing") { await using var other = f.ConversationService(host, execution: host);
            await other.Invoking(value => value.SetManualDispatchHoldAsync(exact, ConversationDispatchHoldReason.OriginalUser, () => true, Token))
                .Should().ThrowAsync<InvalidOperationException>(); return; }
        if (fault is "audit-request") { f.CancelAudit.FailAt = 0; }
        if (fault is "audit-outcome") { f.CancelAudit.FailAt = 1; }
        if (fault is "audit-revocation") { f.CancelAudit.OnWrite = _ => f.ControlRevision++; }
        if (fault is "audit-success-revocation")
        { f.CancelAudit.OnWrite = audit => { if (audit.Outcome == SecurityAuditOutcome.Succeeded) { f.ControlRevision++; } }; }
        if (fault is "read-revocation") { f.OnRead = () => f.ControlRevision++; }
        if (fault is "disposed") { await service.DisposeAsync(); }
        using var cancellation = new CancellationTokenSource();
        if (fault is "cancelled") { await cancellation.CancelAsync(); }
        await service.Invoking(value => value.SetManualDispatchHoldAsync(exact,
            fault is "enum" ? (ConversationDispatchHoldReason)99 : ConversationDispatchHoldReason.OriginalUser,
            () => fault is not "source", cancellation.Token,
            fault is "origin" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi)).Should().ThrowAsync<Exception>();
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        f.ExecutionStarts.Should().Be(0);
    }

    [Fact]
    public async Task CurrentRunAndGenerationHoldObservationsNeverBecomeRestartOrStaleGenerationAuthority()
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var target = new SessionWorkspaceEntry(f.Session, null);
        var before = await service.ReadPendingConversationAsync(target, () => true, Token);
        await service.SetManualDispatchHoldAsync(before.DispatchControl!, ConversationDispatchHoldReason.OriginalUser, () => true, Token);
        var held = await service.ReadPendingConversationAsync(target, () => true, Token);
        await using var restarted = f.ConversationService(host, execution: host);
        var fresh = await restarted.ReadPendingConversationAsync(target, () => true, Token);
        fresh.ManualHoldReason.Should().Be(ConversationDispatchHoldReason.None);
        fresh.DispatchControl!.AdmissionId.Should().NotBe(held.DispatchControl!.AdmissionId);
        await restarted.Invoking(value => value.SetManualDispatchHoldAsync(held.DispatchControl!,
            ConversationDispatchHoldReason.None, () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
        var forged = target with { Authority = target.Authority with { Generation = new(2) } };
        await service.Invoking(value => value.SubmitQueuedAsync(forged, "stale generation", () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        (await service.ReadPendingConversationAsync(target, () => true, Token)).ManualHoldReason
            .Should().Be(ConversationDispatchHoldReason.OriginalUser);
        f.Session = f.Session with { Generation = new(2) };
        var newGeneration = await service.ReadPendingConversationAsync(new(f.Session, null), () => true, Token);
        newGeneration.ManualHoldReason.Should().Be(ConversationDispatchHoldReason.None);
        newGeneration.DispatchControl!.AdmissionId.Should().NotBe(held.DispatchControl.AdmissionId);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task ExactDispatchCommandsUseTheSharedHostManualHoldWithoutRetainingBodiesOrAdmittingModels(RequestOrigin origin)
    {
        using var f = new Fixture(origin);
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var workspace = new SessionWorkspaceService(f, new(f), f, NullLogger<SessionWorkspaceService>.Instance);
        var commands = new SessionRequestCommandService(workspace, service, f, f.Time);
        var id = f.Session.SessionId.Value.ToString("D");
        foreach (var verb in new[] { "status", "pause", "status", "resume" })
        {
            var result = await commands.ExecuteAsync(SessionCommand.Parse("Kora, session dispatch " + verb + " " + id, "Kora")!,
                origin, () => true, Token);
            result.Message.Should().Contain("No activity renewal, action approval, replay or diagnostic queue change.");
            result.RequestSubmission.Should().BeNull();
        }
        f.CancelAudit.Events.Should().HaveCount(4);
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ManualHoldRacingAnAlreadySelectedDispatchNeverClaimsToStopThatTurn()
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var target = new SessionWorkspaceEntry(f.Session, null);
        Task? hold = null;
        var entered = false;
        service.PendingConversationChanged += snapshot =>
        {
            if (entered || !snapshot.Turns.Any(item => item.Phase == ConversationPendingPhase.Dispatching)) { return; }
            entered = true;
            hold = service.SetManualDispatchHoldAsync(snapshot.DispatchControl!, ConversationDispatchHoldReason.OriginalUser, () => true, Token);
        };
        var first = await service.SubmitQueuedAsync(target, "already selected original", () => true, Token);
        await hold!;
        (await first.Completion).RequireReceipt().Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        f.Adapter.Calls.Should().Be(1);
        var observed = await service.ReadPendingConversationAsync(target, () => true, Token);
        observed.ManualHoldReason.Should().Be(ConversationDispatchHoldReason.OriginalUser);
        observed.Paused.Should().BeFalse();
    }

    [Fact]
    public async Task UnqualifiedProviderWhileManuallyHeldReturnsUnavailableInsteadOfQueuedSuccess()
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = f.Production();
        await using var service = f.ConversationService(host, execution: host);
        var target = new SessionWorkspaceEntry(f.Session, null);
        var empty = await service.ReadPendingConversationAsync(target, () => true, Token);
        await service.SetManualDispatchHoldAsync(empty.DispatchControl!, ConversationDispatchHoldReason.OriginalUser, () => true, Token);
        var submitted = await service.SubmitQueuedAsync(target, "no provider authority", () => true, Token);
        var receipt = (await submitted.Completion).RequireReceipt();
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        receipt.Message.Execution.Should().BeNull();
        receipt.Message.AssistantResponse.Should().BeNull();
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task PendingOriginalExpiresWhileManualHoldRemainsAndReleaseCannotReplayIt()
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var target = new SessionWorkspaceEntry(f.Session, null);
        var empty = await service.ReadPendingConversationAsync(target, () => true, Token);
        await service.SetManualDispatchHoldAsync(empty.DispatchControl!, ConversationDispatchHoldReason.OriginalUser, () => true, Token);
        var submitted = await service.SubmitQueuedAsync(target, "expiring original", () => true, Token);
        f.Time.FireDeadline();
        var expired = (await submitted.Completion).RequireReceipt();
        expired.Message.Outcome.Should().Be(ModelTurnOutcome.Denied);
        expired.Message.Reason.Should().Be(ModelTurnReason.ContextExpired);
        expired.Message.Execution.Should().BeNull();
        var held = await service.ReadPendingConversationAsync(target, () => true, Token);
        held.ManualHoldReason.Should().Be(ConversationDispatchHoldReason.OriginalUser);
        held.Paused.Should().BeTrue();
        held.Turns.Should().BeEmpty();
        await service.SetManualDispatchHoldAsync(held.DispatchControl!, ConversationDispatchHoldReason.None, () => true, Token);
        (await service.ReadPendingConversationAsync(target, () => true, Token)).Paused.Should().BeTrue();
        f.PendingRecords[expired.Message.Request.RequestId].Message.Should().Be(expired.Message);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("missing-id")]
    [InlineData("foreign")]
    [InlineData("inactive")]
    [InlineData("source")]
    [InlineData("expired")]
    [InlineData("clock-back")]
    [InlineData("late-receipt")]
    public async Task DispatchCommandNeverCreatesResumesOrRenewsUnknownOrExpiredOriginalInput(string fault)
    {
        using var f = new Fixture();
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var workspace = new SessionWorkspaceService(f, new(f), f, NullLogger<SessionWorkspaceService>.Instance);
        var commands = new SessionRequestCommandService(workspace, service, f, f.Time);
        if (fault is "inactive") { f.Session = f.Session with { IsActive = false }; }
        if (fault is "expired") { f.OnRead = () => f.Time.Now = f.Time.Now.AddSeconds(10); }
        if (fault is "clock-back") { f.OnRead = () => f.Time.Now = f.Time.Now.AddSeconds(-1); }
        var sourceLive = true;
        var retireAfterObservation = false;
        if (fault is "late-receipt")
        { service.PendingConversationChanged += _ => retireAfterObservation = true; }
        bool Admitted()
        {
            if (!sourceLive || fault is "source") { return false; }
            if (retireAfterObservation) { sourceLive = false; }
            return true;
        }
        var id = fault is "missing-id" ? (Guid?)null : fault is "foreign" ? Guid.NewGuid() : f.Session.SessionId.Value;
        await commands.Invoking(value => value.ExecuteAsync(new(SessionCommandOperation.ConversationDispatchPause, id),
            RequestOrigin.LocalUi, Admitted, Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
    }

    [Fact]
    public async Task PrecedingGenerationPendingRecoveryRemainsReadableWithoutNewGenerationControlAuthority()
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var target = new SessionWorkspaceEntry(f.Session, null);
        var empty = await service.ReadPendingConversationAsync(target, () => true, Token);
        await service.SetManualDispatchHoldAsync(empty.DispatchControl!, ConversationDispatchHoldReason.OriginalUser, () => true, Token);
        var original = await service.SubmitQueuedAsync(target, "old pending source", () => true, Token);
        f.Session = f.Session with { Generation = new(2) };
        var recovered = await service.ReadPendingConversationAsync(new(f.Session, null), () => true, Token);
        recovered.DispatchControl.Should().BeNull();
        recovered.Turns.Single().Submission.Should().Be(original.Receipt);
        recovered.ManualHoldReason.Should().Be(ConversationDispatchHoldReason.OriginalUser);
        var workspace = new SessionWorkspaceService(f, new(f), f, NullLogger<SessionWorkspaceService>.Instance);
        var commands = new SessionRequestCommandService(workspace, service, f, f.Time);
        await commands.Invoking(value => value.ExecuteAsync(
            new(SessionCommandOperation.ConversationDispatchResume, f.Session.SessionId.Value),
            RequestOrigin.LocalUi, () => true, Token)).Should().ThrowAsync<InvalidOperationException>().WithMessage("*preceding generation*");
        f.Adapter.Calls.Should().Be(0);
    }
}

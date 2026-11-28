using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Fact]
    public async Task OrdinaryHostedTurnOutlivesFormerFixedLimitsAndRetiresItsUnansweredCheckIn()
    {
        using var f = new Fixture();
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        settings.Preferences.Save(new(1));
        f.Time.CheckInDue = TimeSpan.FromMinutes(1);
        var runtime = new HostClaudeStream();
        var host = f.ClaudeProfiles(runtime, new(f, f.Time));
        await using var service = DeadlineConversation(f, host, settings);
        ConversationAnswerPresentation? preview = null;
        service.ProvisionalAnswerChanged += (value, _) => { preview = value; return ValueTask.CompletedTask; };
        var submitted = service.SubmitClaudeAnswerStreamAsync(new(f.Session, null), "long hosted request", () => true, Token);
        await runtime.Entered.Task.WaitAsync(Token);
        f.Time.Now += TimeSpan.FromMinutes(3);
        f.Time.FireCheckIn();
        var pending = service.CapturePendingCheckIns().Should().ContainSingle().Subject;
        pending.CheckIn.Interval.Minutes.Should().Be(1);
        pending.CheckIn.ActiveElapsed.Should().Be(TimeSpan.FromMinutes(3));
        preview!.IsCurrent().Should().BeTrue("a pending check-in neither pauses nor expires the running turn");
        submitted.IsCompleted.Should().BeFalse();
        runtime.Release.TrySetResult();
        var receipt = await submitted;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse!.Body.Should().Be("safe preview complete");
        service.CapturePendingCheckIns().Should().BeEmpty();
        service.IsCheckInCurrent(pending).Should().BeFalse();
        f.CancelAudit.Events.Select(item => (item.ActionId, item.Outcome, item.ReasonCode)).Should().Equal(
            ("conversation.check-in", SecurityAuditOutcome.Requested, null),
            ("conversation.check-in", SecurityAuditOutcome.Cancelled, "turn-ended"));
        f.CancelAudit.Contexts.Should().OnlyContain(request => request == receipt.Message.Request);
    }

    [Theory]
    [InlineData(ConversationCheckInOperation.KeepGoing, RequestOrigin.LocalUi)]
    [InlineData(ConversationCheckInOperation.KeepGoingFor, RequestOrigin.LocalUi)]
    [InlineData(ConversationCheckInOperation.KeepRunning, RequestOrigin.ActivatedVoice)]
    [InlineData(ConversationCheckInOperation.KeepGoing, RequestOrigin.ActivatedVoice)]
    public async Task CheckInIssuesAtTheActiveIntervalAndEachKeepGoingAnswerReschedulesWithoutStoppingTheTurn(
        ConversationCheckInOperation operation, RequestOrigin origin)
    {
        using var f = new Fixture();
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        settings.Preferences.Save(new(1));
        f.Time.CheckInDue = TimeSpan.FromMinutes(1);
        var host = ConfigureCancellation(f);
        await using var service = DeadlineConversation(f, host, settings);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var submission = service.SubmitAsync(target, "long local request", static () => true, Token);
        try
        {
            f.Time.Now += TimeSpan.FromSeconds(59);
            f.Time.FireCheckIn();
            service.CapturePendingCheckIns().Should().BeEmpty("the interval has not elapsed");
            f.Time.Now += TimeSpan.FromSeconds(1);
            f.Time.FireCheckIn();
            var first = service.CapturePendingCheckIns().Should().ContainSingle().Subject;
            first.CheckIn.ActiveElapsed.Should().Be(TimeSpan.FromMinutes(1));
            var status = (await service.ReadRunningConversationAsync(target, static () => true, Token))!;
            status.CheckIn.Should().Be(first.CheckIn);
            var choice = operation == ConversationCheckInOperation.KeepGoingFor
                ? new ConversationCheckInChoice(operation, new OrdinaryTurnDeadline(2)) : new ConversationCheckInChoice(operation);
            var port = Kora.Application.ViewModels.MainViewModel.TurnCheckInPort.For(service);
            port.Capture().Should().Equal(first);
            port.IsCurrent(first).Should().BeTrue();
            var answered = await port.AnswerAsync(first, choice, static () => true, origin);
            answered.Phase.Should().Be(ConversationExecutionPhase.Running);
            answered.CheckIn.Should().BeNull();
            await service.Invoking(value => value.AnswerCheckInAsync(first, choice, static () => true, Token, origin))
                .Should().ThrowAsync<InvalidOperationException>("one check-in accepts exactly one answer");
            var next = operation switch
            {
                ConversationCheckInOperation.KeepGoingFor => TimeSpan.FromMinutes(2),
                ConversationCheckInOperation.KeepRunning => TimeSpan.FromMinutes(3),
                _ => TimeSpan.FromMinutes(1),
            };
            f.Time.Now += next - TimeSpan.FromSeconds(1);
            f.Time.FireCheckIn();
            service.CapturePendingCheckIns().Should().BeEmpty();
            f.Time.Now += TimeSpan.FromSeconds(1);
            f.Time.FireCheckIn();
            if (operation == ConversationCheckInOperation.KeepRunning)
            { service.CapturePendingCheckIns().Should().BeEmpty("keep running removes later check-ins"); }
            else
            {
                service.CapturePendingCheckIns().Should().ContainSingle()
                    .Which.CheckIn.Id.Should().NotBe(first.CheckIn.Id);
            }
            submission.IsCompleted.Should().BeFalse();
        }
        finally { provider.TrySetResult(LateAnswer()); }
        var receipt = await submission;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        var initiator = origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser;
        var reason = new ConversationCheckInChoice(operation, operation == ConversationCheckInOperation.KeepGoingFor
            ? new OrdinaryTurnDeadline(2) : null).ReasonCode;
        f.CancelAudit.Events.Take(4).Select(item => (item.ActionId, item.Outcome, item.Initiator, item.ReasonCode)).Should().Equal(
            ("conversation.check-in", SecurityAuditOutcome.Requested, SecurityAuditInitiator.System, null),
            ("conversation.check-in-answer", SecurityAuditOutcome.Requested, initiator, reason),
            ("conversation.check-in", SecurityAuditOutcome.Succeeded, SecurityAuditInitiator.System, reason),
            ("conversation.check-in-answer", SecurityAuditOutcome.Succeeded, initiator, reason));
    }

    [Fact]
    public async Task StopAnswerCancelsThroughTheRunningConversationPathWithTruthfulOutcomeAndAudit()
    {
        using var f = new Fixture();
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        settings.Preferences.Save(new(1));
        f.Time.CheckInDue = TimeSpan.FromMinutes(1);
        var host = ConfigureCancellation(f);
        await using var service = DeadlineConversation(f, host, settings);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var submission = service.SubmitAsync(new(f.Session, null), "long local request", static () => true, Token);
        f.Time.Now += TimeSpan.FromMinutes(1);
        f.Time.FireCheckIn();
        var checkIn = service.CapturePendingCheckIns().Should().ContainSingle().Subject;
        var stopped = await service.AnswerCheckInAsync(checkIn, new(ConversationCheckInOperation.Stop), static () => true, Token);
        stopped.Phase.Should().Be(ConversationExecutionPhase.Cancelling);
        submission.IsCompleted.Should().BeFalse("cancellation requested is not resource quiescence");
        provider.TrySetResult(LateAnswer());
        var receipt = await submission;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        receipt.Message.Reason.Should().Be(ModelTurnReason.CallerCancelled);
        receipt.Message.AssistantResponse.Should().BeNull();
        host.IsQuiescent.Should().BeTrue();
        var events = f.CancelAudit.Events.Select(item => (item.ActionId, item.Outcome, item.ReasonCode)).ToArray();
        events.Take(4).Should().Equal(
            ("conversation.check-in", SecurityAuditOutcome.Requested, null),
            ("conversation.check-in-answer", SecurityAuditOutcome.Requested, "stop"),
            ("conversation.cancel-request", SecurityAuditOutcome.Requested, null),
            ("conversation.cancel-request", SecurityAuditOutcome.Succeeded, null));
        // The stop receipt and the check-in's retirement with the stopped turn may complete in either order.
        events.Skip(4).Should().HaveCount(2).And.Contain(("conversation.check-in-answer", SecurityAuditOutcome.Succeeded, "stop"))
            .And.ContainSingle(item => item.ActionId == "conversation.check-in"
                && (item.Outcome == SecurityAuditOutcome.Succeeded && item.ReasonCode == "stop"
                    || item.Outcome == SecurityAuditOutcome.Cancelled && item.ReasonCode == "turn-ended"));
    }

    [Theory]
    [InlineData("control")]
    [InlineData("cancel-refused")]
    public async Task RefusedOrFailedCheckInAnswersLeaveTheTurnRunningAndRecordNoSuccess(string fault)
    {
        using var f = new Fixture();
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        settings.Preferences.Save(new(1));
        f.Time.CheckInDue = TimeSpan.FromMinutes(1);
        var host = ConfigureCancellation(f);
        await using var service = DeadlineConversation(f, host, settings);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var submission = service.SubmitAsync(new(f.Session, null), "long local request", static () => true, Token);
        f.Time.Now += TimeSpan.FromMinutes(1);
        f.Time.FireCheckIn();
        var checkIn = service.CapturePendingCheckIns().Should().ContainSingle().Subject;
        if (fault is "control")
        {
            await service.Invoking(value => value.AnswerCheckInAsync(checkIn,
                new(ConversationCheckInOperation.KeepGoing), static () => false, Token)).Should().ThrowAsync<InvalidOperationException>();
            await service.Invoking(value => value.AnswerCheckInAsync(checkIn,
                new(ConversationCheckInOperation.Stop), static () => false, Token)).Should().ThrowAsync<InvalidOperationException>();
            await service.Invoking(value => value.AnswerCheckInAsync(checkIn with { Execution = checkIn.Execution with { LeaseId = Guid.NewGuid() } },
                new(ConversationCheckInOperation.KeepGoing), static () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
            await service.Invoking(value => value.AnswerCheckInAsync(checkIn,
                new(ConversationCheckInOperation.KeepGoing), static () => true, new CancellationToken(true)))
                .Should().ThrowAsync<OperationCanceledException>();
            f.Session = f.Session with { Generation = new(2) };
            await service.Invoking(value => value.AnswerCheckInAsync(checkIn,
                new(ConversationCheckInOperation.KeepGoing), static () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
            f.Session = f.Session with { Generation = new(1), IsActive = false };
            await service.Invoking(value => value.AnswerCheckInAsync(checkIn,
                new(ConversationCheckInOperation.KeepGoing), static () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
            f.Session = f.Session with { IsActive = true };
            f.CancelAudit.Events.Should().ContainSingle("refused answers record nothing beyond the issued check-in");
        }
        else
        {
            var calls = 0;
            await service.Invoking(value => value.AnswerCheckInAsync(checkIn,
                new(ConversationCheckInOperation.Stop), () => ++calls == 1, Token)).Should().ThrowAsync<InvalidOperationException>();
            f.CancelAudit.Events.Select(item => (item.ActionId, item.Outcome)).Should().Equal(
                ("conversation.check-in", SecurityAuditOutcome.Requested),
                ("conversation.check-in-answer", SecurityAuditOutcome.Requested),
                ("conversation.check-in-answer", SecurityAuditOutcome.Failed));
        }
        service.CapturePendingCheckIns().Should().ContainSingle().Which.Should().Be(checkIn);
        submission.IsCompleted.Should().BeFalse();
        provider.TrySetResult(LateAnswer());
        (await submission).Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
    }

    [Fact]
    public async Task MissingTrustedAuditStopsTheTurnInsteadOfRunningWithoutACheckIn()
    {
        using var f = new Fixture();
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        settings.Preferences.Save(new(1));
        f.Time.CheckInDue = TimeSpan.FromMinutes(1);
        var host = ConfigureCancellation(f);
        await using var service = new SessionConversationService(f, f, new(f, new(f), f, NullLogger<SessionWorkspaceService>.Instance),
            f, host, f.Time, f, host, cancellationAudit: null, interactions: f.Questions, ordinaryDeadlines: settings.Service);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var submission = service.SubmitAsync(new(f.Session, null), "long local request", static () => true, Token);
        f.Time.Now += TimeSpan.FromMinutes(1);
        f.Time.FireCheckIn();
        service.CapturePendingCheckIns().Should().BeEmpty();
        f.Time.FireCheckIn();
        provider.TrySetResult(LateAnswer());
        var receipt = await submission;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        receipt.Message.AssistantResponse.Should().BeNull();
    }

    [Fact]
    public async Task HostWaitPausesTheCheckInClockAndAnAnswerDuringAWaitArmsOnlyAfterResume()
    {
        using var f = new Fixture();
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        settings.Preferences.Save(new(1));
        f.Time.CheckInDue = TimeSpan.FromMinutes(1);
        var host = ConfigureCancellation(f);
        await using var service = DeadlineConversation(f, host, settings);
        ModelTurnLifetime? lifetime = null;
        f.Adapter.OnRun = () => lifetime = ModelTurnLifetime.Current;
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var submission = service.SubmitAsync(new(f.Session, null), "long local request", static () => true, Token);
        lifetime!.UsesCheckIns.Should().BeTrue();
        using (lifetime.BeginHostWait())
        {
            f.Time.Now += TimeSpan.FromMinutes(2);
            f.Time.FireCheckIn();
            service.CapturePendingCheckIns().Should().BeEmpty("a permission card pauses the ordinary check-in clock");
        }
        f.Time.Now += TimeSpan.FromSeconds(59);
        f.Time.FireCheckIn();
        service.CapturePendingCheckIns().Should().BeEmpty();
        f.Time.Now += TimeSpan.FromSeconds(1);
        f.Time.FireCheckIn();
        var checkIn = service.CapturePendingCheckIns().Should().ContainSingle().Subject;
        checkIn.CheckIn.ActiveElapsed.Should().Be(TimeSpan.FromMinutes(1));
        using (lifetime.BeginHostWait())
        {
            await service.AnswerCheckInAsync(checkIn, new(ConversationCheckInOperation.KeepGoing), static () => true, Token);
            f.Time.Now += TimeSpan.FromSeconds(30);
            f.Time.FireCheckIn();
            service.CapturePendingCheckIns().Should().BeEmpty("an answer during a host wait arms nothing until it ends");
        }
        f.Time.Now += TimeSpan.FromMinutes(1);
        f.Time.FireCheckIn();
        service.CapturePendingCheckIns().Should().ContainSingle().Which.CheckIn.Id.Should().NotBe(checkIn.CheckIn.Id);
        provider.TrySetResult(LateAnswer());
        (await submission).Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
    }

    [Fact]
    public async Task ShortToolFreeOrdinaryTurnIsUnchangedAndItsRetiredScheduleCannotPostLater()
    {
        using var f = new Fixture();
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        var host = ConfigureCancellation(f);
        await using var service = DeadlineConversation(f, host, settings);
        var receipt = await service.SubmitAsync(new(f.Session, null), "short", static () => true, Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse!.Body.Should().Be("answer");
        f.Time.CheckInTimers.Should().Be(1);
        f.Time.Now += TimeSpan.FromHours(1);
        f.Time.FireCheckIn();
        service.CapturePendingCheckIns().Should().BeEmpty();
        f.CancelAudit.Events.Should().BeEmpty("an unissued check-in leaves no audit or presentation");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostWaitPausesContextExpiryAndRuntimeDeadlineOfAFixedTurn(bool paused)
    {
        using var f = new Fixture();
        var host = ConfigureCancellation(f);
        using var root = f.Root();
        var context = f.Context(toolMode: ModelToolMode.None);
        var turn = (await host.AdmitAsync(ModelProviderSelection.OllamaCandidate, context, Token)).Turn!;
        f.Adapter.OnTool = _ =>
        {
            ModelTurnLifetime.Current.Should().BeSameAs(ModelTurnLifetime.For(turn.Provenance.Request));
            using (paused ? turn.BeginHostWait() : null) { f.Time.Now += TimeSpan.FromMinutes(2); }
            return Task.CompletedTask;
        };
        var result = await host.RunAsync(turn, Token);
        result.Outcome.Should().Be(paused ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.Denied);
        if (!paused) { result.Reason.Should().Be(ModelTurnReason.ContextExpired); }
        context.ExpiresAt.Should().Be(DateTimeOffset.UnixEpoch.AddMinutes(1), "pausing never rewrites the original envelope");
        turn.Lifetime.UsesCheckIns.Should().BeFalse();
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("question")]
    [InlineData("special-tools")]
    [InlineData("again")]
    [InlineData("undispatched")]
    public async Task CheckInsCannotAttachToForeignOwnerUserWaitSpecializedOrRepeatedTurns(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var context = f.Context(toolMode: fault is "special-tools" ? ModelToolMode.LocalWebPage : ModelToolMode.None);
        var turn = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, context, Token)).Turn!;
        if (fault is not "undispatched") { turn.ConversationTarget = new(f.Session, null); }
        if (fault is "question")
        { turn.QuestionContinuation = new(f.Questions, NeverPresent, f.Request, f.Session.Generation, context.ExpiresAt, () => true, f.Time); }
        if (fault is "again") { f.Host.UseOrdinaryCheckIns(turn); }
        var host = fault is "foreign" ? f.PolicyHost : f.Host;
        host.Invoking(value => value.UseOrdinaryCheckIns(turn)).Should().Throw<InvalidOperationException>();
        turn.Lifetime.UsesCheckIns.Should().Be(fault is "again");
    }

    [Fact]
    public async Task PendingOrdinaryRequestCapturesLatestIntervalOnlyWhenOriginalRequestDispatches()
    {
        using var f = new Fixture { TrackPendingRecords = true };
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        settings.Preferences.Save(new(60));
        f.Time.CheckInDue = TimeSpan.FromMinutes(60);
        var host = ConfigureCancellation(f);
        await using var service = DeadlineConversation(f, host, settings);
        var captures = new List<(HostRequest Request, string? Minutes)>();
        settings.Store.Before = (operation, name) =>
        {
            if (operation is "read" && name.Contains("minutes", StringComparison.Ordinal)
                && Kora.Core.Diagnostics.HostActivity.Current?.Request is { } request
                && f.PendingRecords.TryGetValue(request.RequestId, out var record))
            {
                record.Task.State.Should().Be(HostTaskState.DispatchRecorded);
                captures.Add((request, settings.Store.Values.GetValueOrDefault(name)));
            }
        };
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var first = await service.SubmitQueuedAsync(target, "first", () => true, Token);
        var second = await service.SubmitQueuedAsync(target, "second", () => true, Token);
        second.Receipt.Message.Execution.Should().BeNull();
        captures.Should().ContainSingle().Which.Should().Be((first.Receipt.Message.Request, "60"));
        var observation = await settings.Refresh();
        (await settings.Save(observation, new(1))).Should().BeTrue();
        f.Time.CheckInDue = TimeSpan.FromMinutes(1);
        captures.Should().HaveCount(1, "pending work has neither a running schedule nor a captured interval");
        f.Adapter.Pending = null;
        provider.TrySetResult(LateAnswer());
        (await first.Completion).RequireReceipt().Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        (await second.Completion).RequireReceipt().Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        captures.Should().Equal((first.Receipt.Message.Request, "60"), (second.Receipt.Message.Request, "1"));
    }

    [Fact]
    public async Task RequestOnlyLocalProfileKeepsItsFixedLimitWhileTheNextOrdinaryDispatchUsesCheckIns()
    {
        using var f = new Fixture();
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        settings.Preferences.Save(new(1));
        f.Time.CheckInDue = TimeSpan.FromMinutes(1);
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfileCatalogue.FieldCatalogue = true;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        var preferences = f.ProfilePreferences.Value.Serialize();
        var policy = f.SessionOverrides.Serialize();
        var host = f.ConfiguredProfiles();
        await using var service = DeadlineConversation(f, host, settings);
        var captures = new List<HostRequest>();
        settings.Store.Before = (operation, name) =>
        {
            if (operation is "read" && name.Contains("minutes", StringComparison.Ordinal)
                && Kora.Core.Diagnostics.HostActivity.Current?.Request is { } request)
            {
                f.Task!.Task.State.Should().Be(HostTaskState.DispatchRecorded);
                captures.Add(request);
            }
        };
        var commands = LocalProfileCommands(f, service);
        var chosen = Profile() with { Reasoning = new("exact-effort"), Context = new("exact-window") };
        var review = (await commands.ExecuteAsync(LocalProfileCommand(f, "request-only profile", chosen),
            RequestOrigin.LocalUi, static () => true, Token)).RequestReview!;
        var explicitRequest = (await commands.ExecuteAsync(LocalProfileConfirm(review),
            RequestOrigin.LocalUi, static () => true, Token)).RequestSubmission!;
        explicitRequest.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        f.Adapter.Profile.Should().Be(chosen);
        captures.Should().BeEmpty("the reviewed request-only route retains its existing fixed limit");

        var ordinary = await service.SubmitLocalAnswerAsync(new(f.Session, null), "next ordinary request",
            static () => true, Token);
        ordinary.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        captures.Should().ContainSingle().Which.Should().Be(ordinary.Message.Request);
        f.Adapter.Profile.Should().Be(Profile());
        f.Adapter.Calls.Should().Be(2);
        f.ProfilePreferences.Value.Serialize().Should().Be(preferences);
        f.ProfilePreferences.Writes.Should().Be(0);
        f.SessionOverrides.Serialize().Should().Be(policy);
    }

    [Fact]
    public async Task ShorteningFutureIntervalCannotChangeAnAlreadyDispatchedTurn()
    {
        using var f = new Fixture();
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        settings.Preferences.Save(new(60));
        f.Time.CheckInDue = TimeSpan.FromMinutes(60);
        var runtime = new HostClaudeStream();
        var host = f.ClaudeProfiles(runtime, new(f, f.Time));
        await using var service = DeadlineConversation(f, host, settings);
        var submitted = service.SubmitClaudeAnswerStreamAsync(new(f.Session, null), "original", () => true, Token);
        await runtime.Entered.Task.WaitAsync(Token);
        var observation = await settings.Refresh();
        (await settings.Save(observation, new(1))).Should().BeTrue();
        f.Time.Now += TimeSpan.FromSeconds(90);
        f.Time.FireCheckIn();
        service.CapturePendingCheckIns().Should().BeEmpty("the captured 60-minute interval still governs this turn");
        runtime.Release.TrySetResult();
        var receipt = await submitted;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        settings.Service.Capture().Minutes.Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClarificationAndSpecializedTurnsRetainTheirFixedBudgetWithoutConsumingOrdinaryPreference(bool question)
    {
        using var f = new Fixture();
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        settings.Preferences.BeginWrite();
        var host = ConfigureCancellation(f);
        await using var service = DeadlineConversation(f, host, settings);
        if (question)
        {
            // No ask_user is produced by this adapter. Its no-wait final answer still uses the fixed clarification path.
            var receipt = await service.SubmitAsync(new(f.Session, null), "question route", () => true, Token,
                questionPresenter: (_, _, _, _, _, _) => throw new InvalidOperationException("No question should be presented."));
            receipt.Message.Outcome.Should().NotBe(ModelTurnOutcome.TimedOut);
        }
        else
        {
            var receipt = await service.SubmitOriginalLocalAsync(new(f.Session, null), "specialized source", () => true,
                _ => Task.CompletedTask, [], RequestOrigin.LocalUi, Token);
            receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        }
        f.Time.CheckInTimers.Should().Be(0);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("expiry-during-read")]
    [InlineData("expiry-during-audit")]
    [InlineData("check-in-turn")]
    public async Task CancellationObservationExpiresWithAFixedTurnButNotWithACheckInTurn(string fault)
    {
        using var f = new Fixture();
        var host = ConfigureCancellation(f);
        var service = f.ConversationService(host, execution: host);
        var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = pending.Task;
        var session = new SessionWorkspaceEntry(f.Session, null);
        var submission = fault is "check-in-turn"
            ? service.SubmitAsync(session, "original", static () => true, Token)
            : service.SubmitOriginalLocalAsync(session, "original", static () => true, _ => Task.CompletedTask, [],
                RequestOrigin.LocalUi, Token);
        var original = (await service.ReadRunningConversationAsync(session, static () => true, Token))!.Target;
        if (fault is "expired" or "check-in-turn") { f.Time.Now = original.ExpiresAt; }
        if (fault is "expiry-during-read") { f.OnRead = () => f.Time.Now = original.ExpiresAt; }
        f.CancelAudit.OnWrite = audit =>
        {
            if (audit.Outcome == SecurityAuditOutcome.Requested && fault is "expiry-during-audit") { f.Time.Now = original.ExpiresAt; }
        };
        if (fault is "check-in-turn")
        {
            (await service.CancelRunningConversationAsync(original, static () => true, Token)).Phase
                .Should().Be(ConversationExecutionPhase.Cancelling, "Stop stays available for a long check-in turn");
            pending.SetResult(LateAnswer());
            (await submission).Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
            return;
        }
        await service.Invoking(value => value.CancelRunningConversationAsync(original, static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.OnRead = null;
        f.Time.Now = original.CreatedAt;
        (await service.ReadRunningConversationAsync(session, static () => true, Token))!.Phase
            .Should().Be(ConversationExecutionPhase.Running);
        pending.SetResult(LateAnswer());
        (await submission).Task.State.Should().Be(HostTaskState.Succeeded);
    }

    [Fact]
    public async Task ProviderSuccessThatRacesTheFixedRuntimeDeadlineIsReportedAsTimedOut()
    {
        using var f = new Fixture();
        var ran = false;
        var checks = 0;
        var host = f.Qualified(ModelProviderSelection.OllamaCandidate, remainsQualified: () =>
        {
            // The final post-adapter revalidation observes the deadline only after its last cancellation check.
            if (ran && ++checks == 2) { f.Time.FireDeadline(); }
            return true;
        });
        using var root = f.Root();
        var turn = (await host.AdmitAsync(ModelProviderSelection.OllamaCandidate, f.Context(), Token)).Turn!;
        f.Adapter.OnRun = () => ran = true;
        var result = await host.RunAsync(turn, Token);
        result.Outcome.Should().Be(ModelTurnOutcome.TimedOut);
        result.Reason.Should().Be(ModelTurnReason.DeadlineExceeded);
        result.Response.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostedFinalByteReviewPausesItsTurnClockUntilDecidedButNeverBeforeCreation(bool bound)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var authorization = reviews.PrepareTurn(f.Context(toolMode: ModelToolMode.None), Profile(Kora.Core.Providers.ProviderService.Copilot));
        var lifetime = new ModelTurnLifetime(f.Request, f.Time);
        if (bound) { authorization.Lifetime = lifetime; }
        CopilotTransmissionReview? offered = null;
        reviews.Offered += review => offered = review;
        var transmission = new CopilotTransmission(new(new(Guid.NewGuid()), f.Request, ModelProviderSelection.CopilotCandidate),
            authorization.Profile, new("https://example.com/final"), new byte[] { 1 });
        var pending = reviews.ReviewAsync(authorization, transmission, _ => ValueTask.FromResult(true), Token).AsTask();
        offered.Should().NotBeNull();
        lifetime.IsWaitingForHost.Should().Be(bound, "the blocking final-byte review pauses its owning turn");
        f.Time.Now = f.Time.Now.AddMinutes(5);
        reviews.Confirm(offered!, true, RequestOrigin.LocalUi).Should().BeTrue();
        (await pending).Should().Be(bound, "an unbound review keeps the original fixed context expiry");
        lifetime.IsWaitingForHost.Should().BeFalse();
        reviews.Current(authorization).Should().Be(bound);
        f.Time.Now = authorization.Context.CreatedAt.AddTicks(-1);
        reviews.Current(authorization).Should().BeFalse("pausing never admits a context before it was created");
    }

    [Fact]
    public async Task TurnContextIsNeverCurrentBeforeItsCreation()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var context = f.Context(toolMode: ModelToolMode.None);
        var turn = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, context, Token)).Turn!;
        turn.ContextCurrent(f.Time).Should().BeTrue();
        f.Time.Now = context.CreatedAt.AddTicks(-1);
        turn.ContextCurrent(f.Time).Should().BeFalse();
    }

    [Fact]
    public async Task ReviewedTaskOutputOfACheckInTurnStaysCurrentAfterTheTurnOutlivesItsContextWindow()
    {
        using var f = new Fixture();
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        settings.Preferences.Save(new(1));
        f.Time.CheckInDue = TimeSpan.FromMinutes(1);
        var host = ConfigureCancellation(f);
        await using var service = DeadlineConversation(f, host, settings);
        var commands = LocalProfileCommands(f, service);
        var presented = 0;
        commands.BindTaskResponseOutput((mode, _) => new(mode, Kora.Core.Voice.ResponseOutputMode.Hybrid, () => true,
            (_, _, current, _) =>
            {
                current().Should().BeTrue("the check-in turn's clock, not its original context window, governs output freshness");
                presented++;
                return Task.CompletedTask;
            }));
        // The ordinary turn runs for three minutes: far beyond its 15-second context window.
        f.Adapter.OnRun = () => f.Time.Now += TimeSpan.FromMinutes(3);
        var review = (await commands.ExecuteAsync(new(Kora.Core.Commands.SessionCommandOperation.RequestReview,
            f.Session.SessionId.Value, f.Session.Generation.Value, f.Metadata?.Revision.Value ?? 0)
            { RequestBody = "long task", RequestTaskResponseMode = Kora.Core.Voice.TaskResponseMode.VisualOnly },
            RequestOrigin.LocalUi, () => true, Token)).RequestReview!;
        var result = await commands.ExecuteAsync(LocalProfileConfirm(review), RequestOrigin.LocalUi, () => true, Token);
        result.RequestSubmission!.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        presented.Should().Be(1);
    }

    private static SessionConversationService DeadlineConversation(Fixture f, ModelTurnHost host,
        OrdinaryDeadlineConfigurationFixture settings) =>
        new(f, f, new(f, new(f), f, NullLogger<SessionWorkspaceService>.Instance),
            f, host, f.Time, f, host, f.CancelAudit, interactions: f.Questions, ordinaryDeadlines: settings.Service);
}

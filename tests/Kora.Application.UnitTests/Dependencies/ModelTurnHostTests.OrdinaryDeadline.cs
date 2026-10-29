using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(59999, ModelTurnOutcome.Succeeded)]
    [InlineData(60000, ModelTurnOutcome.TimedOut)]
    [InlineData(60001, ModelTurnOutcome.TimedOut)]
    public async Task OrdinaryHostedTurnEnforcesCapturedPreferenceBeforeEqualAndAfterThresholdWithoutTimerRace(
        int elapsedMilliseconds, ModelTurnOutcome expected)
    {
        using var f = new Fixture();
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        settings.Preferences.Save(new(1));
        var runtime = new HostClaudeStream();
        var host = f.ClaudeProfiles(runtime, new(f, f.Time));
        f.Time.ExpectedDeadline = TimeSpan.FromMinutes(1);
        await using var service = DeadlineConversation(f, host, settings);
        var submitted = service.SubmitClaudeAnswerStreamAsync(new(f.Session, null), "original", () => true, Token);
        await runtime.Entered.Task.WaitAsync(Token);
        var request = f.Retained!.Request;
        // The fake does not fire timers: terminal validation itself must reject equal/late success.
        f.Time.Now += TimeSpan.FromMilliseconds(elapsedMilliseconds);
        runtime.Release.TrySetResult();
        var receipt = await submitted;
        receipt.Message.Outcome.Should().Be(expected);
        receipt.Message.Reason.Should().Be(expected == ModelTurnOutcome.Succeeded ? ModelTurnReason.ProviderOutcome : ModelTurnReason.DeadlineExceeded);
        receipt.Message.Request.Should().Be(request);
        receipt.Message.AssistantResponse?.Body.Should().Be(expected == ModelTurnOutcome.Succeeded ? "safe preview complete" : null);
        host.IsQuiescent.Should().BeTrue();
        f.ExecutionCompletions.Should().Be(1);
    }

    [Fact]
    public async Task DeadlineCancellationSuppressesLateAnswerAndRetainsLeaseUntilIgnoringRuntimeActuallyJoins()
    {
        using var f = new Fixture();
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        settings.Preferences.Save(new(1));
        var runtime = new HostClaudeStream();
        var host = f.ClaudeProfiles(runtime, new(f, f.Time));
        f.Time.ExpectedDeadline = TimeSpan.FromMinutes(1);
        await using var service = DeadlineConversation(f, host, settings);
        var completing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.RunningConversationChanged += status =>
        {
            if (status.Phase == ConversationExecutionPhase.Completing) { completing.TrySetResult(); }
        };
        ConversationAnswerPresentation? preview = null;
        service.ProvisionalAnswerChanged += (value, _) => { preview = value; return ValueTask.CompletedTask; };
        var submitted = service.SubmitClaudeAnswerStreamAsync(new(f.Session, null), "deadline request", () => true, Token);
        await runtime.Entered.Task.WaitAsync(Token);
        try
        {
            f.Time.Now += TimeSpan.FromMinutes(1);
            f.Time.FireDeadline();
            await completing.Task.WaitAsync(Token);
            submitted.IsCompleted.Should().BeFalse();
            host.IsQuiescent.Should().BeFalse();
            preview!.IsCurrent().Should().BeFalse();
            f.ExecutionCompletions.Should().Be(0);
            f.Retained!.AssistantResponse.Should().BeNull();
            await service.Invoking(value => value.SubmitClaudeAnswerStreamAsync(new(f.Session, null),
                "cannot steal lease", () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
        }
        finally { runtime.Release.TrySetResult(); }
        var receipt = await submitted;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.TimedOut);
        receipt.Message.Reason.Should().Be(ModelTurnReason.DeadlineExceeded);
        receipt.Message.AssistantResponse.Should().BeNull();
        receipt.Task.State.Should().Be(HostTaskState.Unknown);
        host.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(1, 60)]
    [InlineData(5, 120)]
    [InlineData(60, 120)]
    public async Task ProviderMaximumAlwaysWinsAndPreferenceChangesCannotReplaceRunningCapture(int minutes, int seconds)
    {
        using var f = new Fixture();
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        settings.Preferences.Save(new(minutes));
        var runtime = new HostClaudeStream();
        var host = f.ClaudeProfiles(runtime, new(f, f.Time));
        f.Time.ExpectedDeadline = TimeSpan.FromSeconds(seconds);
        await using var service = DeadlineConversation(f, host, settings);
        var submitted = service.SubmitClaudeAnswerStreamAsync(new(f.Session, null), "original", () => true, Token);
        await runtime.Entered.Task.WaitAsync(Token);
        var snapshot = await settings.Refresh();
        (await settings.Save(snapshot, new(minutes == 1 ? 60 : 1))).Should().BeTrue();
        f.Time.Now += TimeSpan.FromSeconds(seconds);
        f.Time.FireDeadline();
        runtime.Release.TrySetResult();
        var receipt = await submitted;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.TimedOut);
        receipt.Message.AssistantResponse.Should().BeNull();
    }

    [Fact]
    public async Task OrdinaryLocalToolTurnKeepsFixedFifteenSecondMaximumAndOriginalContextExpiry()
    {
        using var f = new Fixture();
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        settings.Preferences.Save(new(60));
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var host = f.ConfiguredProfiles();
        await using var service = DeadlineConversation(f, host, settings);
        f.Adapter.OnTool = async invoke =>
        {
            (await invoke(Kora.Core.Tools.ReadOnlyCapabilityCatalog.Version, "{}", Token))
                .Outcome.Should().Be(Kora.Core.Tools.CapabilityOutcome.Succeeded);
            f.Time.Now += TimeSpan.FromSeconds(15);
        };
        var receipt = await service.SubmitAsync(new(f.Session, null), "version", () => true, Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Denied);
        receipt.Message.Reason.Should().Be(ModelTurnReason.ContextExpired);
        receipt.Message.AssistantResponse.Should().BeNull();
        f.Adapter.Calls.Should().Be(1);
    }

    [Fact]
    public async Task CapturedBudgetCannotRenewEarlierContextOrStartBeforeDispatch()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var context = new ModelContextEnvelope(f.Request, "policy", "body", [], f.Time.Now,
            f.Time.Now.AddSeconds(3), ModelToolMode.None);
        var turn = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, context, Token)).Turn!;
        turn.OrdinaryActiveBudget.Should().BeNull();
        f.Host.Invoking(host => host.CaptureOrdinaryActiveBudget(turn, new(5))).Should().Throw<InvalidOperationException>();
        f.Time.Now += TimeSpan.FromSeconds(1);
        turn.ConversationTarget = new(f.Session, null);
        f.Host.CaptureOrdinaryActiveBudget(turn, new(60));
        turn.OrdinaryActiveBudget!.Limit.Should().Be(TimeSpan.FromSeconds(2));
        context.ExpiresAt.Should().Be(DateTimeOffset.UnixEpoch.AddSeconds(3));
        f.Host.Invoking(host => host.CaptureOrdinaryActiveBudget(turn, new(1))).Should().Throw<InvalidOperationException>();
        f.Time.Now += TimeSpan.FromSeconds(2);
        f.Time.ExpectedDeadline = TimeSpan.Zero;
        var result = await f.Host.RunAsync(turn, Token);
        result.Outcome.Should().Be(ModelTurnOutcome.TimedOut);
        result.Reason.Should().Be(ModelTurnReason.DeadlineExceeded);
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task PendingOrdinaryRequestCapturesLatestDefaultOnlyWhenOriginalRequestDispatches()
    {
        using var f = new Fixture { TrackPendingRecords = true };
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        settings.Preferences.Save(new(60));
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
        captures.Should().HaveCount(1, "pending work has neither a running budget nor a captured preference");
        f.Adapter.Pending = null;
        provider.TrySetResult(LateAnswer());
        (await first.Completion).RequireReceipt().Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        (await second.Completion).RequireReceipt().Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        captures.Should().Equal((first.Receipt.Message.Request, "60"), (second.Receipt.Message.Request, "1"));
    }

    [Fact]
    public async Task ExpiryAfterRuntimeSuccessButBeforeTerminalRetentionCannotPublishSuccess()
    {
        using var f = new Fixture();
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        settings.Preferences.Save(new(1));
        var runtime = new HostClaudeStream();
        var host = f.ClaudeProfiles(runtime, new(f, f.Time));
        f.Time.ExpectedDeadline = TimeSpan.FromMinutes(1);
        await using var service = DeadlineConversation(f, host, settings);
        service.RunningConversationChanged += status =>
        {
            if (status.Phase == ConversationExecutionPhase.Completing) { f.Time.Now += TimeSpan.FromMinutes(1); }
        };
        var submitted = service.SubmitClaudeAnswerStreamAsync(new(f.Session, null), "original", () => true, Token);
        await runtime.Entered.Task.WaitAsync(Token);
        runtime.Release.TrySetResult();
        var receipt = await submitted;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.TimedOut);
        receipt.Message.Reason.Should().Be(ModelTurnReason.DeadlineExceeded);
        receipt.Message.AssistantResponse.Should().BeNull();
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("question")]
    [InlineData("special-tools")]
    public async Task OrdinaryBudgetCannotAttachToForeignOwnerUserWaitOrSpecializedToolMode(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var context = f.Context(toolMode: fault is "special-tools" ? ModelToolMode.LocalWebPage : ModelToolMode.None);
        var turn = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, context, Token)).Turn!;
        turn.ConversationTarget = new(f.Session, null);
        if (fault is "question")
        { turn.QuestionContinuation = new(f.Questions, NeverPresent, f.Request, f.Session.Generation, context.ExpiresAt, () => true, f.Time); }
        var host = fault is "foreign" ? f.PolicyHost : f.Host;
        host.Invoking(value => value.CaptureOrdinaryActiveBudget(turn, new(5))).Should().Throw<InvalidOperationException>();
        turn.OrdinaryActiveBudget.Should().BeNull();
    }

    [Fact]
    public async Task MonotonicExpiryAcrossAwaitedSourceValidationBlocksDispatchWithoutRenewingContext()
    {
        using var f = new Fixture();
        using var root = f.Root();
        f.Time.Timestamp = 0;
        var context = f.Context(toolMode: ModelToolMode.None);
        var turn = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, context, Token)).Turn!;
        turn.ConversationTarget = new(f.Session, null);
        f.Host.CaptureOrdinaryActiveBudget(turn, new(5));
        f.BeforeTaskRead = () =>
        {
            f.Time.Timestamp = TimeSpan.FromSeconds(15).Ticks;
            return Task.CompletedTask;
        };
        var result = await f.Host.RunAsync(turn, Token);
        result.Outcome.Should().Be(ModelTurnOutcome.TimedOut);
        result.Reason.Should().Be(ModelTurnReason.DeadlineExceeded);
        f.Adapter.Calls.Should().Be(0);
        f.Time.Now.Should().Be(DateTimeOffset.UnixEpoch);
        context.ExpiresAt.Should().Be(DateTimeOffset.UnixEpoch.AddMinutes(1));
    }

    [Fact]
    public async Task ShorteningFutureDefaultCannotShortenAnAlreadyDispatchedTurn()
    {
        using var f = new Fixture();
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        settings.Preferences.Save(new(60));
        var runtime = new HostClaudeStream();
        var host = f.ClaudeProfiles(runtime, new(f, f.Time));
        await using var service = DeadlineConversation(f, host, settings);
        var submitted = service.SubmitClaudeAnswerStreamAsync(new(f.Session, null), "original", () => true, Token);
        await runtime.Entered.Task.WaitAsync(Token);
        var observation = await settings.Refresh();
        (await settings.Save(observation, new(1))).Should().BeTrue();
        f.Time.Now += TimeSpan.FromSeconds(90);
        runtime.Release.TrySetResult();
        var receipt = await submitted;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        settings.Service.Capture().Minutes.Should().Be(1);
    }

    [Fact]
    public async Task DeadlineCannotPublishTerminalSuccessOrFreeAnUnconfirmedRuntimeLease()
    {
        using var f = new Fixture();
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        var host = ConfigureCancellation(f);
        f.DetachExpectedFailedHost(host);
        await using var service = DeadlineConversation(f, host, settings);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        f.Adapter.OnRun = () => f.Adapter.JoinConfirmed = false;
        Exception? disposalFailure;
        try
        {
            var submitted = await service.SubmitQueuedAsync(new(f.Session, null), "original", () => true, Token);
            f.Time.Now += TimeSpan.FromSeconds(15);
            f.Time.FireDeadline();
            provider.TrySetResult(LateAnswer());
            var completion = await submitted.Completion;
            completion.Failure.Should().BeOfType<ConversationSubmissionUnconfirmedException>();
            f.Retained!.Outcome.Should().BeNull();
            f.Retained.AssistantResponse.Should().BeNull();
            f.ExecutionCompletions.Should().Be(0);
            f.Task!.Task.State.Should().Be(HostTaskState.DispatchRecorded);
            host.IsQuiescent.Should().BeFalse();
        }
        finally
        {
            provider.TrySetResult(LateAnswer());
            disposalFailure = await Record.ExceptionAsync(async () => await host.DisposeAsync());
            f.Adapter.JoinConfirmed = true;
        }
        disposalFailure.Should().BeOfType<AggregateException>();
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
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BudgetUsesMonotonicActiveElapsedAndFailsClosedOnReversedClock(bool reverse)
    {
        var clock = new BudgetClock();
        var budget = new OrdinaryTurnActiveBudget(new(1), TimeSpan.FromMinutes(2), clock);
        budget.Preference.Minutes.Should().Be(1);
        clock.Timestamp = reverse ? -1 : TimeSpan.FromSeconds(30).Ticks;
        budget.Remaining.Should().Be(reverse ? TimeSpan.Zero : TimeSpan.FromSeconds(30));
        budget.Expired.Should().Be(reverse);
    }

    private static SessionConversationService DeadlineConversation(Fixture f, ModelTurnHost host,
        OrdinaryDeadlineConfigurationFixture settings) =>
        new(f, f, new(f, new(f), f, NullLogger<SessionWorkspaceService>.Instance),
            f, host, f.Time, f, host, f.CancelAudit, interactions: f.Questions, ordinaryDeadlines: settings.Service);

    private sealed class BudgetClock : TimeProvider
    {
        internal long Timestamp { get; set; }
        public override long GetTimestamp() => Timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}

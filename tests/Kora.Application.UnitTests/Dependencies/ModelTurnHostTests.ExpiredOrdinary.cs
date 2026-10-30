using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Commands;
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
    [InlineData(RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.ActivatedVoice, true)]
    public async Task ActualOrdinaryExpiryRequiresFreshReviewAndSeparateNewNativeIntentBeforeGenuineAnswer(RequestOrigin oldOrigin, bool named)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        if (named) { f.Metadata = new(f.Session.SessionId, new(1), new("named active conversation")); }
        var host = ConfigureCancellation(f);
        await using var conversation = f.ConversationService(host, execution: host);
        var target = new SessionWorkspaceEntry(f.Session, f.Metadata);
        var expired = await ExpireOrdinaryAsync(f, conversation, target, oldOrigin);
        var original = f.PendingRecords[expired.Request.RequestId];
        expired.OrdinaryText.Should().BeTrue();
        expired.Execution.Should().BeNull();
        expired.Outcome.Should().Be(ModelTurnOutcome.Denied);
        expired.Reason.Should().Be(ModelTurnReason.ContextExpired);
        var snapshot = await conversation.ReadPendingConversationAsync(target, () => true, Token);
        snapshot.Paused.Should().BeTrue();
        await conversation.ResumePendingConversationAsync(target, snapshot.Revision, () => true, Token);
        var commands = conversation.CreateRequestCommands();
        var writes = f.RetainCalls;
        var calls = f.Adapter.Calls;
        var review = (await commands.ExecuteAsync(ExpiredReview(expired) with { MetadataRevision = f.Metadata?.Revision.Value ?? 0 },
            RequestOrigin.LocalUi, () => true, Token)).RequestReview!;
        review.ExpiredSource.Should().Be(new ExpiredOrdinaryRequestSource(expired, original.Task));
        review.CurrentLocalProvider.Should().Be(ModelProviderSelection.OllamaCandidate);
        review.RetryProviderProfile.Should().Be(Profile());
        review.Origin.Should().Be(RequestOrigin.LocalUi);
        review.Body.Should().Be(expired.Body);
        f.RetainCalls.Should().Be(writes);
        f.Adapter.Calls.Should().Be(calls);
        f.Time.Now = f.Time.Now.AddSeconds(5);
        var confirmedAt = f.Time.Now;
        DateTimeOffset? freshExpiry = null;
        conversation.PendingConversationChanged += snapshot =>
        {
            foreach (var turn in snapshot.Turns.Where(item => item.Submission.Message.OriginalTextSource == expired.Request))
            { freshExpiry = turn.ExpiresAt; }
        };
        var result = await commands.ExecuteAsync(FailedConfirm(review), RequestOrigin.LocalUi, () => true, Token);
        var fresh = result.RequestSubmission!;
        fresh.Message.Body.Should().Be(expired.Body);
        fresh.Message.OriginalTextSource.Should().Be(expired.Request);
        fresh.Message.Request.Origin.Should().Be(RequestOrigin.LocalUi);
        fresh.Message.Request.RequestId.Should().NotBe(expired.Request.RequestId);
        fresh.Message.Request.TaskId.Should().NotBe(expired.Request.TaskId);
        fresh.Message.AssistantResponse!.Body.Should().NotBeNullOrWhiteSpace();
        fresh.Task.State.Should().Be(HostTaskState.Succeeded);
        var wire = JsonSerializer.Deserialize<JsonElement>(f.Adapter.Wire!);
        freshExpiry.Should().Be(confirmedAt.AddSeconds(15));
        wire.GetProperty("Tools").GetArrayLength().Should().Be(0);
        wire.GetProperty("Evidence").GetArrayLength().Should().Be(0);
        f.PendingRecords[expired.Request.RequestId].Should().Be(original);
        f.Adapter.Calls.Should().Be(calls + 1);
        await commands.Invoking(value => value.ExecuteAsync(FailedConfirm(review), RequestOrigin.LocalUi, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(writes + 1);
    }

    [Theory]
    [InlineData("body")]
    [InlineData("task")]
    [InlineData("profile")]
    [InlineData("default")]
    [InlineData("account")]
    [InlineData("generation")]
    [InlineData("metadata")]
    [InlineData("expiry")]
    [InlineData("clock-rollback")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("source")]
    [InlineData("retire")]
    [InlineData("work")]
    [InlineData("resume")]
    public async Task FreshExpiredReviewRefusesChangedSourcesAndCurrentAuthorityWithoutNewIntentOrDispatch(string fault)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var source = SeedExpiredOrdinary(f);
        var host = ConfigureCancellation(f);
        await using var conversation = f.ConversationService(host, execution: host);
        var commands = conversation.CreateRequestCommands();
        var current = true;
        var review = (await commands.ExecuteAsync(ExpiredReview(source), RequestOrigin.LocalUi, () => current, Token)).RequestReview!;
        var original = f.PendingRecords[source.Request.RequestId];
        ConversationQueuedSubmission? peer = null;
        var peerProvider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (fault is "body") { f.PendingRecords[source.Request.RequestId] = original with { Message = source with { Body = "changed" } }; }
        if (fault is "task") { f.PendingRecords[source.Request.RequestId] = original with { Task = new(source.Request, new(3), HostTaskState.Denied) }; }
        if (fault is "profile") { f.SessionProfileRevision++; }
        if (fault is "default") { f.ProfilePreferences.Value = new(ProviderService.Unset, []); }
        if (fault is "account") { f.ProfileCatalogue.Status = ProviderCatalogueStatus.AuthenticationRequired; }
        if (fault is "generation") { f.Session = f.Session with { Generation = new(2) }; }
        if (fault is "metadata") { f.Metadata = new(f.Session.SessionId, new(1), new("changed")); }
        if (fault is "expiry") { f.Time.Now = review.ExpiresAt; }
        if (fault is "clock-rollback") { f.Time.Now = f.Time.Now.AddSeconds(-1); }
        if (fault is "private") { f.Current = false; }
        if (fault is "control") { f.ControlRevision++; }
        if (fault is "source") { current = false; }
        if (fault is "retire") { commands.Retire(); }
        if (fault is "work" or "resume")
        {
            f.Adapter.Pending = peerProvider.Task;
            peer = await conversation.SubmitQueuedAsync(review.Target, "independent new work", () => true, Token);
            var snapshot = await conversation.ReadPendingConversationAsync(review.Target, () => true, Token);
            var exact = snapshot.Turns.Single();
            if (fault is "resume")
            {
                var second = await conversation.SubmitQueuedAsync(review.Target, "pending peer", () => true, Token);
                snapshot = await conversation.ReadPendingConversationAsync(review.Target, () => true, Token);
                await conversation.CancelPendingConversationAsync(review.Target, snapshot.Turns.Last(), snapshot.Revision, () => true, Token);
                await second.Completion;
                snapshot = await conversation.ReadPendingConversationAsync(review.Target, () => true, Token);
                await conversation.ResumePendingConversationAsync(review.Target, snapshot.Revision, () => true, Token);
            }
            exact.Submission.Should().Be(peer.Receipt);
        }
        var writes = f.RetainCalls;
        var calls = f.Adapter.Calls;
        try
        {
            await commands.Invoking(value => value.ExecuteAsync(FailedConfirm(review), RequestOrigin.LocalUi, () => true, Token))
                .Should().ThrowAsync<InvalidOperationException>();
            f.RetainCalls.Should().Be(writes);
            f.Adapter.Calls.Should().Be(calls);
        }
        finally
        {
            peerProvider.TrySetResult(LateAnswer());
            if (peer is not null) { await peer.Completion; }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiredReviewCancelOrRetireNeverRetainsOrDispatchesAndCannotBeConfirmed(bool retire)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var source = SeedExpiredOrdinary(f);
        var host = ConfigureCancellation(f);
        await using var conversation = f.ConversationService(host, execution: host);
        var commands = conversation.CreateRequestCommands();
        var review = (await commands.ExecuteAsync(ExpiredReview(source), RequestOrigin.LocalUi, () => true, Token)).RequestReview!;
        if (retire) { commands.Retire(); }
        else
        {
            (await commands.ExecuteAsync(FailedConfirm(review) with { Operation = SessionCommandOperation.RequestCancel },
                RequestOrigin.LocalUi, () => true, Token)).Outcome.Should().Be("cancelled");
        }
        await commands.Invoking(value => value.ExecuteAsync(FailedConfirm(review), RequestOrigin.LocalUi, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
        f.PendingRecords.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreshOrdinaryJoinsNormalFifoTailAndItsOwnExpiryCannotRenewOrDispatchIt(bool expireFresh)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var conversation = f.ConversationService(host, execution: host);
        var target = new SessionWorkspaceEntry(f.Session, null);
        var source = await ExpireOrdinaryAsync(f, conversation, target, RequestOrigin.LocalUi);
        var original = f.PendingRecords[source.Request.RequestId];
        var held = await conversation.ReadPendingConversationAsync(target, () => true, Token);
        await conversation.ResumePendingConversationAsync(target, held.Revision, () => true, Token);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var active = await conversation.SubmitQueuedAsync(target, "new active peer", () => true, Token);
        var older = await conversation.SubmitQueuedAsync(target, "earlier FIFO peer", () => true, Token);
        var commands = conversation.CreateRequestCommands();
        var review = (await commands.ExecuteAsync(ExpiredReview(source), RequestOrigin.LocalUi, () => true, Token)).RequestReview!;
        var completed = new TaskCompletionSource<ConversationTurnCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        conversation.SubmittedConversationCompleted += (_, completion) =>
        {
            if (completion.Receipt?.Message.OriginalTextSource == source.Request) { completed.TrySetResult(completion); }
        };
        try
        {
            var fresh = (await commands.ExecuteAsync(FailedConfirm(review), RequestOrigin.LocalUi, () => true, Token)).RequestSubmission!;
            var queue = await conversation.ReadPendingConversationAsync(target, () => true, Token);
            queue.Turns.Select(item => item.Submission.Message.Request).Should().Equal(active.Receipt.Message.Request,
                older.Receipt.Message.Request, fresh.Message.Request);
            queue.Turns.Last().Position.Should().Be(2);
            queue.Turns.Last().ExpiresAt.Should().Be(f.Time.Now.AddSeconds(15));
            f.Adapter.Calls.Should().Be(2, "fresh acceptance never jumps past either existing peer");
            if (expireFresh) { f.Time.Now = f.Time.Now.AddSeconds(15); }
            f.Adapter.Pending = null;
            provider.SetResult(LateAnswer());
            await active.Completion;
            await older.Completion;
            var terminal = (await completed.Task.WaitAsync(Token)).RequireReceipt();
            terminal.Message.Outcome.Should().Be(expireFresh ? ModelTurnOutcome.Denied : ModelTurnOutcome.Succeeded);
            terminal.Message.Reason.Should().Be(expireFresh ? ModelTurnReason.ContextExpired : ModelTurnReason.ProviderOutcome);
            terminal.Message.AssistantResponse?.Body.Should().Be(expireFresh ? null : "answer");
            f.Adapter.Calls.Should().Be(expireFresh ? 2 : 4);
            f.PendingRecords[source.Request.RequestId].Should().Be(original);
        }
        finally { provider.TrySetResult(LateAnswer()); await active.Completion; await older.Completion; }
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("failure")]
    [InlineData("body")]
    [InlineData("profile")]
    [InlineData("provider")]
    [InlineData("foreground")]
    [InlineData("tools")]
    public async Task ExpiredOrdinaryReviewRejectsMixedIntentBeforeAnyRetentionOrProviderReview(string fault)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var source = SeedExpiredOrdinary(f);
        var host = ConfigureCancellation(f);
        await using var conversation = f.ConversationService(host, execution: host);
        var command = ExpiredReview(source);
        if (fault is "failure") { command = command with { RequestFailedMessage = source }; }
        if (fault is "body") { command = command with { RequestBody = "replacement" }; }
        if (fault is "profile") { command = command with { RequestLocalProfile = Profile() }; }
        if (fault is "provider") { command = command with { RequestProvider = SessionRequestReview.ProviderChoice.Copilot }; }
        if (fault is "foreground") { command = command with { SessionId = null, RequestForegroundTarget = true }; }
        if (fault is "tools") { command = command with { RequestClaudeTools = ["not-authority"] }; }
        var commands = conversation.CreateRequestCommands();
        await commands.Invoking(value => value.ExecuteAsync(command,
            fault is "origin" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi, () => true, Token,
            new(new(f.Session, null), () => true))).Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("final-task")]
    [InlineData("source")]
    [InlineData("control")]
    public async Task ExpiredOrdinarySourceRevalidatesAfterBothTaskReadsWithoutCreatingNewWork(string fault)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var source = SeedExpiredOrdinary(f);
        var original = f.PendingRecords[source.Request.RequestId];
        var workspace = new SessionWorkspaceService(f, new(f), f, NullLogger<SessionWorkspaceService>.Instance);
        var reads = 0;
        var current = true;
        f.BeforeTaskRead = () =>
        {
            reads++;
            if (fault is "missing") { f.PendingRecords.TryRemove(source.Request.RequestId, out _); }
            if (reads == 2)
            {
                if (fault is "final-task") { f.PendingRecords[source.Request.RequestId] = original with { Task = new(source.Request, new(3), HostTaskState.Denied) }; }
                if (fault is "source") { current = false; }
                if (fault is "control") { f.CanControl = false; }
            }
            return Task.CompletedTask;
        };
        using var root = f.Root();
        await workspace.Invoking(value => value.ReadExpiredOrdinarySourceAsync(new(f.Session, null),
            source, () => current, Token)).Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task FreshExpiredOrdinaryRetainsItsOwnTruthfulUnavailableWithoutFallbackOrOldHistoryChanges()
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var source = SeedExpiredOrdinary(f);
        var original = f.PendingRecords[source.Request.RequestId];
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var host = f.ConfiguredProfiles(production: true);
        await using var conversation = f.ConversationService(host, execution: host);
        var commands = conversation.CreateRequestCommands();
        var review = (await commands.ExecuteAsync(ExpiredReview(source), RequestOrigin.LocalUi, () => true, Token)).RequestReview!;
        var receipt = (await commands.ExecuteAsync(FailedConfirm(review), RequestOrigin.LocalUi, () => true, Token)).RequestSubmission!;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        receipt.Task.State.Should().Be(HostTaskState.Unavailable);
        receipt.Message.AssistantResponse.Should().BeNull();
        receipt.Message.OriginalTextSource.Should().Be(source.Request);
        receipt.Message.Request.RequestId.Should().NotBe(source.Request.RequestId);
        f.PendingRecords[source.Request.RequestId].Should().Be(original);
        f.Adapter.Calls.Should().Be(0);
        f.ExecutionStarts.Should().Be(0);
    }

    [Fact]
    public async Task FreshExpiredOrdinaryReviewsCurrentExplicitLocalPolicyWithoutInventingAnAccountProfile()
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var source = SeedExpiredOrdinary(f);
        var original = f.PendingRecords[source.Request.RequestId];
        await using var conversation = f.ConversationService(f.PolicyHost, execution: f.PolicyHost);
        var commands = conversation.CreateRequestCommands();
        var review = (await commands.ExecuteAsync(ExpiredReview(source), RequestOrigin.LocalUi, () => true, Token)).RequestReview!;
        review.CurrentLocalProvider.Should().Be(ModelProviderSelection.OllamaCandidate);
        review.RetryProviderProfile.Should().BeNull();
        var result = (await commands.ExecuteAsync(FailedConfirm(review), RequestOrigin.LocalUi, () => true, Token)).RequestSubmission!;
        result.Message.AssistantResponse!.Body.Should().Be("answer");
        result.Message.OriginalTextSource.Should().Be(source.Request);
        f.PendingRecords[source.Request.RequestId].Should().Be(original);
        f.Adapter.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("unknown-profile")]
    [InlineData("hosted-default")]
    [InlineData("missing-model-host")]
    public async Task FreshExpiredOrdinaryNeverGuessesOrFallsBackFromUnavailableCurrentProviderReview(string fault)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var source = SeedExpiredOrdinary(f);
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = fault is "hosted-default"
            ? new(ProviderService.Copilot, [Profile(ProviderService.Copilot)])
            : new(ProviderService.Unset, []);
        var host = f.ConfiguredProfiles();
        await using var conversation = f.ConversationService(fault is "missing-model-host"
            ? new TestAdmission(ModelTurnOutcome.Unavailable) : host, execution: host);
        var commands = conversation.CreateRequestCommands();
        await commands.Invoking(value => value.ExecuteAsync(ExpiredReview(source), RequestOrigin.LocalUi, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
        f.PendingRecords.Should().ContainSingle();
    }

    [Fact]
    public async Task FreshExpiredOrdinaryCallerCancellationRetainsCurrentWorkerUntilItsPhysicalProviderJoins()
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var source = SeedExpiredOrdinary(f);
        var original = f.PendingRecords[source.Request.RequestId];
        var host = ConfigureCancellation(f);
        await using var conversation = f.ConversationService(host, execution: host);
        var commands = conversation.CreateRequestCommands();
        var review = (await commands.ExecuteAsync(ExpiredReview(source), RequestOrigin.LocalUi, () => true, Token)).RequestReview!;
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        f.Adapter.OnRun = () => started.TrySetResult();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var operation = commands.ExecuteAsync(FailedConfirm(review), RequestOrigin.LocalUi, () => true, cancellation.Token);
        await started.Task.WaitAsync(Token);
        try
        {
            await cancellation.CancelAsync();
            await this.Invoking(_ => operation).Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
            var joining = conversation.DisposeAsync().AsTask();
            joining.IsCompleted.Should().BeFalse();
            host.IsQuiescent.Should().BeFalse();
            provider.SetResult(LateAnswer());
            await joining;
            f.PendingRecords[source.Request.RequestId].Should().Be(original);
            f.PendingRecords.Values.Should().NotContain(item => item.Message.AssistantResponse != null);
            host.IsQuiescent.Should().BeTrue();
        }
        finally { provider.TrySetResult(LateAnswer()); await conversation.DisposeAsync(); }
    }

    private static SessionCommand ExpiredReview(ConversationMessage source) => new(SessionCommandOperation.RequestReview,
        source.Request.SessionId.Value, source.Generation.Value) { RequestExpiredMessage = source };

    private static ConversationMessage SeedExpiredOrdinary(Fixture f)
    {
        var source = new ConversationMessage(f.Request, f.Session.Generation, 1, 2, f.Time.Now,
            "  immutable expired USER e\u0301\r\n ", ModelTurnOutcome.Denied, ModelTurnReason.ContextExpired) { OrdinaryText = true };
        f.PendingRecords[source.Request.RequestId] = new(source, new(source.Request, new(2), HostTaskState.Denied));
        return source;
    }

    private static async Task<ConversationMessage> ExpireOrdinaryAsync(Fixture f, SessionConversationService conversation,
        SessionWorkspaceEntry target, RequestOrigin origin)
    {
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        f.Adapter.OnRun = () => started.TrySetResult();
        var first = await conversation.SubmitQueuedAsync(target, "earlier active request", () => true, Token);
        await started.Task.WaitAsync(Token);
        var pending = await conversation.SubmitQueuedAsync(target, "  immutable expired USER e\u0301\r\n ", () => true,
            Token, origin: origin, localAnswer: true);
        pending.Position.Should().Be(1);
        var calls = f.Adapter.Calls;
        f.Time.Now = f.Time.Now.AddSeconds(15);
        f.Adapter.Pending = null;
        f.Adapter.OnRun = null;
        provider.SetResult(LateAnswer());
        await first.Completion;
        var expired = (await pending.Completion).RequireReceipt();
        f.Adapter.Calls.Should().Be(calls);
        expired.Task.State.Should().Be(HostTaskState.Denied);
        return expired.Message;
    }
}

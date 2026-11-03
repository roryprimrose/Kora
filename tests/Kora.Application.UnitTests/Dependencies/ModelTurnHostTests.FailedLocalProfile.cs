using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;

using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, ModelProviderIdentity.Ollama, false)]
    [InlineData(RequestOrigin.ActivatedVoice, ModelProviderIdentity.Claude, false)]
    [InlineData(RequestOrigin.LocalUi, ModelProviderIdentity.Copilot, true)]
    public async Task FailedSourceAlternateLocalReviewRunsOnlyExplicitTupleAndRetainsOriginalFailureAndUnavailableReadback(
        RequestOrigin origin, ModelProviderIdentity originalProvider, bool unqualified)
    {
        using var f = new Fixture(origin) { TrackPendingRecords = true };
        var source = SeedFailedRetry(f);
        source = source with { Execution = source.Execution! with
        { Selection = source.Execution.Selection with { Provider = originalProvider } } };
        f.PendingRecords[source.Request.RequestId] = f.PendingRecords[source.Request.RequestId] with { Message = source };
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfileCatalogue.FieldCatalogue = true;
        f.ProfilePreferences.Value = new(ProviderService.Claude, []);
        f.SessionOverrides = new(ProviderService.Copilot);
        var preferences = f.ProfilePreferences.Value.Serialize();
        var policy = f.SessionOverrides.Serialize();
        var chosen = Profile() with { Reasoning = new("exact-effort"), Context = new("exact-window") };
        var host = f.ConfiguredProfiles(production: unqualified);
        await using var conversation = f.ConversationService(host, execution: host);
        var commands = conversation.CreateRequestCommands();
        var review = (await commands.ExecuteAsync(FailedReview(source) with { RequestLocalProfile = chosen },
            origin, () => true, Token)).RequestReview!;
        review.FailedSource!.Message.Should().Be(source);
        review.LocalRequestProfile.Should().Be(chosen);
        review.Body.Should().Be(source.Body);
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        var receipt = (await commands.ExecuteAsync(FailedConfirm(review), RequestOrigin.LocalUi, () => true, Token)).RequestSubmission!;
        receipt.Message.Body.Should().Be(source.Body);
        receipt.Message.Request.Origin.Should().Be(origin);
        receipt.Message.Request.SessionId.Should().Be(source.Request.SessionId);
        receipt.Message.Request.RequestId.Should().NotBe(source.Request.RequestId);
        receipt.Message.Outcome.Should().Be(unqualified ? ModelTurnOutcome.Unavailable : ModelTurnOutcome.Succeeded);
        receipt.Task.State.Should().Be(unqualified ? HostTaskState.Unavailable : HostTaskState.Succeeded);
        receipt.Message.AssistantResponse?.Body.Should().Be(unqualified ? null : "answer");
        f.Adapter.Calls.Should().Be(unqualified ? 0 : 1);
        if (!unqualified) { f.Adapter.Profile.Should().Be(chosen); }
        f.PendingRecords[source.Request.RequestId].Message.Should().Be(source);
        f.ProfilePreferences.Value.Serialize().Should().Be(preferences);
        f.SessionOverrides.Serialize().Should().Be(policy);
        f.ProfilePreferences.Writes.Should().Be(0);
        f.SessionProfileRevision.Should().Be(0);
        await commands.Invoking(value => value.ExecuteAsync(FailedConfirm(review), origin, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Messages.Should().NotContain(message => message.Contains(source.Body, StringComparison.Ordinal));
        host.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("task")]
    [InlineData("catalogue")]
    [InlineData("account")]
    [InlineData("policy")]
    [InlineData("private")]
    [InlineData("expiry")]
    [InlineData("cancel")]
    public async Task FailedSourceAlternateLocalConfirmationRefusesDriftOrDeclineWithoutNewIntentOrProviderEffect(string fault)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var source = SeedFailedRetry(f);
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        var host = f.ConfiguredProfiles();
        await using var conversation = f.ConversationService(host, execution: host);
        var commands = conversation.CreateRequestCommands();
        var review = (await commands.ExecuteAsync(FailedReview(source) with { RequestLocalProfile = Profile() },
            RequestOrigin.LocalUi, () => true, Token)).RequestReview!;
        switch (fault)
        {
            case "source": f.PendingRecords.TryRemove(source.Request.RequestId, out _); break;
            case "task": f.PendingRecords[source.Request.RequestId] = f.PendingRecords[source.Request.RequestId] with
                { Task = new(source.Request, new(4), HostTaskState.Denied) }; break;
            case "catalogue": f.ProfileCatalogue.Revision++; break;
            case "account": f.ProfileCatalogue.Account = "changed"; break;
            case "policy": f.SessionProfileRevision++; break;
            case "private": f.Current = false; break;
            case "expiry": f.Time.Now = review.ExpiresAt; break;
            case "cancel":
                await commands.ExecuteAsync(new(SessionCommandOperation.RequestCancel, source.Request.SessionId.Value)
                    { RequestReviewId = review.Id }, RequestOrigin.LocalUi, () => true, Token);
                break;
        }
        await commands.Invoking(value => value.ExecuteAsync(FailedConfirm(review), RequestOrigin.LocalUi, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
        f.ExecutionStarts.Should().Be(0);
        f.ProfilePreferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedSourceAlternateLocalQueuesBehindPeerAndRevalidatesExactSourceBeforeItsOnlyDispatch(bool deleted)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var source = SeedFailedRetry(f);
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var host = f.ConfiguredProfiles();
        await using var conversation = f.ConversationService(host, execution: host);
        var reply = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = reply.Task;
        var peer = await conversation.SubmitQueuedAsync(new(f.Session, null), "independent running peer", () => true, Token);
        var terminal = new TaskCompletionSource<ConversationTurnCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        conversation.SubmittedConversationCompleted += (_, completion) =>
        {
            if (string.Equals(completion.Receipt?.Message.Body, source.Body, StringComparison.Ordinal)
                || completion.Failure is ConversationSubmissionUnconfirmedException)
            { terminal.TrySetResult(completion); }
        };
        var commands = conversation.CreateRequestCommands();
        try
        {
            var review = (await commands.ExecuteAsync(FailedReview(source) with { RequestLocalProfile = Profile() },
                RequestOrigin.LocalUi, () => true, Token)).RequestReview!;
            var accepted = (await commands.ExecuteAsync(FailedConfirm(review), RequestOrigin.LocalUi, () => true, Token)).RequestSubmission!;
            accepted.Message.Outcome.Should().BeNull();
            accepted.Message.Execution.Should().BeNull();
            f.Adapter.Calls.Should().Be(1);
            peer.Completion.IsCompleted.Should().BeFalse();
            if (deleted) { f.PendingRecords.TryRemove(source.Request.RequestId, out _); }
            reply.SetResult(LateAnswer());
            await peer.Completion;
            var completed = await terminal.Task.WaitAsync(Token);
            if (deleted)
            {
                completed.Failure.Should().BeOfType<ConversationSubmissionUnconfirmedException>();
                f.Adapter.Calls.Should().Be(1, "withdrawn failed content cannot dispatch the alternate");
                f.PendingRecords[accepted.Message.Request.RequestId].Message.AssistantResponse.Should().BeNull();
            }
            else
            {
                completed.RequireReceipt().Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
                completed.RequireReceipt().Message.Body.Should().Be(source.Body);
                f.Adapter.Calls.Should().Be(2);
                f.PendingRecords[source.Request.RequestId].Message.Should().Be(source);
            }
            host.IsQuiescent.Should().BeTrue();
        }
        finally { reply.TrySetResult(LateAnswer()); await peer.Completion; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedSourceAlternateLocalCancellationKeepsIgnoringWorkerOwnedAndSuppressesLateAnswer(bool callerCancellation)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var source = SeedFailedRetry(f);
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        var host = f.ConfiguredProfiles();
        await using var conversation = f.ConversationService(host, execution: host);
        var commands = conversation.CreateRequestCommands();
        var review = (await commands.ExecuteAsync(FailedReview(source) with { RequestLocalProfile = Profile() },
            RequestOrigin.LocalUi, () => true, Token)).RequestReview!;
        var reply = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = reply.Task;
        f.Adapter.OnRun = () => started.TrySetResult();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var operation = commands.ExecuteAsync(FailedConfirm(review), RequestOrigin.LocalUi, () => true, cancellation.Token);
        await started.Task.WaitAsync(Token);
        try
        {
            if (callerCancellation) { await cancellation.CancelAsync(); }
            else { f.Current = false; }
            var disposal = conversation.DisposeAsync().AsTask();
            disposal.IsCompleted.Should().BeFalse();
            host.IsQuiescent.Should().BeFalse();
            reply.SetResult(LateAnswer());
            await disposal;
            await this.Invoking(_ => operation).Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
            f.PendingRecords[source.Request.RequestId].Message.Should().Be(source);
            f.PendingRecords.Values.Should().OnlyContain(item => item.Message.AssistantResponse == null);
            f.Adapter.Calls.Should().Be(1);
            host.IsQuiescent.Should().BeTrue();
        }
        finally { reply.TrySetResult(LateAnswer()); }
    }
}

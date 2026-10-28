using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(ModelTurnOutcome.Succeeded)]
    [InlineData(ModelTurnOutcome.Unknown)]
    [InlineData(ModelTurnOutcome.TimedOut)]
    [InlineData(ModelTurnOutcome.Cancelled)]
    [InlineData(ModelTurnOutcome.AuthenticationRequired)]
    public async Task VerifiedLocalHandoffNeverSubstitutesAnotherLocalOutcomeOrAReasonForActualProof(ModelTurnOutcome outcome)
    {
        using var f = new InteractionStorageFixture();
        f.Time.UseFrozenTimers = true;
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var local = new LocalUnavailableAdapter { Outcome = outcome };
        var hosted = new CopilotConversationRuntime();
        await using var composed = new Composition(f, adapter: local, copilot: hosted, localFirst: true);
        var offered = 0;
        composed.LocalHandoff!.ReviewRequested += () => offered++;
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var result = await composed.LocalHandoff.RunAsync(target, "only this original", static () => true, f.Token);
        result.Message.Outcome.Should().Be(outcome);
        result.Message.Handoff.Should().BeNull();
        offered.Should().Be(0);
        f.Count("conversation_messages").Should().Be(1);
        f.Count("host_questions").Should().Be(0);
        hosted.Sends.Should().Be(0);
        local.Calls.Should().Be(1);
        composed.Turns.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("decline")]
    [InlineData("cancel")]
    [InlineData("expiry")]
    [InlineData("private")]
    [InlineData("channel")]
    [InlineData("context")]
    [InlineData("source")]
    public async Task VerifiedLocalHandoffRejectsChangedReviewWithZeroSuccessorSendOrHostedAnswer(string change)
    {
        using var f = new InteractionStorageFixture();
        f.Time.UseFrozenTimers = true;
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var hosted = new CopilotConversationRuntime();
        await using var composed = new Composition(f, adapter: new LocalUnavailableAdapter(), copilot: hosted, localFirst: true);
        var presented = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        composed.LocalHandoff!.ReviewRequested += () => presented.TrySetResult();
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var running = composed.LocalHandoff.RunAsync(target, "exact local failure", static () => true, f.Token);
        await presented.Task.WaitAsync(f.Token);
        var offer = (await composed.HandoffPresentation!.ReadPending(RequestOrigin.LocalUi, static () => true, f.Token)).Single();
        var review = composed.HandoffPresentation.Open(offer, RequestOrigin.LocalUi, static () => true)!;
        await review.Refresh(f.Token);
        if (change is "expiry") { f.Time.Now = offer.Context.ExpiresAt; }
        if (change is "private") { composed.Access.ControlRevision++; composed.Access.CanControl = false; }
        if (change is "source")
        {
            using var activity = HostActivity.BeginRoot(offer.Context.Request, HostActivityLayer.Application, HostOperation.Request);
            var task = (await f.Store.ReadTaskAsync(offer.Context.Request.SessionId, offer.Context.Request.TaskId, f.Token))!.Task;
            await f.Tasks.CommitAsync(task.Next(HostTaskState.Unknown), task.Revision.Value, f.Token);
        }
        Exception? reviewFailure;
        if (change is "context")
        {
            using var activity = composed.HandoffWorkflow!.Continuation(offer)!();
            var counterfeit = new ModelContextEnvelope(offer.Context.Request, offer.Context.SystemPolicy, "changed private input",
                [], offer.Context.CreatedAt, offer.Context.ExpiresAt, offer.Context.ToolMode);
            reviewFailure = await Record.ExceptionAsync(() => composed.HandoffWorkflow.ReviewAsync(offer,
                offer.Policy, counterfeit, ModelHandoffDecision.Approve, RequestOrigin.LocalUi, f.Token));
        }
        else
        {
            reviewFailure = await Record.ExceptionAsync(() => review.Decide(offer,
                change is "decline" ? ModelHandoffDecision.Decline : change is "cancel" ? ModelHandoffDecision.Cancel : ModelHandoffDecision.Approve,
                change is "channel" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi, f.Token));
        }
        var operationFailure = await Record.ExceptionAsync(async () => await running);
        var disposeFailure = await Record.ExceptionAsync(async () => await review.DisposeAsync());
        if (change is "private" or "source")
        {
            operationFailure.Should().BeOfType<ConversationSubmissionUnconfirmedException>();
            reviewFailure.Should().NotBeNull();
            disposeFailure.Should().NotBeNull();
        }
        else
        {
            operationFailure.Should().BeNull();
            reviewFailure.Should().BeNull();
            disposeFailure.Should().BeNull();
        }
        var messages = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages;
        messages.Should().ContainSingle();
        messages[0].Handoff.Should().BeNull();
        messages[0].AssistantResponse.Should().BeNull();
        hosted.Sends.Should().Be(0);
        (await f.Store.ReadGrantsAsync(f.Token)).Should().BeEmpty();
        composed.Turns.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VerifiedLocalHandoffRollsBackSourceNewIntentLineageAndAuditTogether(bool failAtCommit)
    {
        using var f = new InteractionStorageFixture();
        f.Time.UseFrozenTimers = true;
        var checkpoint = new InteractionTransactionCheckpoint();
        f.Reopen(checkpoint);
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var hosted = new CopilotConversationRuntime();
        await using var composed = new Composition(f, adapter: new LocalUnavailableAdapter(), copilot: hosted, localFirst: true);
        var presented = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        composed.LocalHandoff!.ReviewRequested += () => presented.TrySetResult();
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var running = composed.LocalHandoff.RunAsync(target, "atomic original", static () => true, f.Token);
        await presented.Task.WaitAsync(f.Token);
        var offer = (await composed.HandoffPresentation!.ReadPending(RequestOrigin.LocalUi, static () => true, f.Token)).Single();
        var review = composed.HandoffPresentation.Open(offer, RequestOrigin.LocalUi, static () => true)!;
        await review.Refresh(f.Token);
        var tasksBefore = f.Count("host_tasks");
        void FailAfterSourceWrite(Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction transaction)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT state FROM host_tasks WHERE task_id=$task;";
            command.Parameters.AddWithValue("$task", offer.Context.Request.TaskId.Value.ToString("D"));
            if ((long)command.ExecuteScalar()! == (long)HostTaskState.Unavailable)
            { throw new IOException("injected atomic handoff failure"); }
        }
        if (failAtCommit) { checkpoint.Commit = FailAfterSourceWrite; }
        else { checkpoint.Audit = FailAfterSourceWrite; }
        var failure = await Record.ExceptionAsync(() => review.Decide(offer, ModelHandoffDecision.Approve, RequestOrigin.LocalUi, f.Token));
        failure.Should().BeOfType<ConversationSubmissionUnconfirmedException>();
        await Assert.ThrowsAsync<ConversationSubmissionUnconfirmedException>(async () => await running);
        await Assert.ThrowsAsync<ConversationSubmissionUnconfirmedException>(async () => await review.DisposeAsync());
        checkpoint.Commit = null;
        checkpoint.Audit = null;
        f.Count("host_tasks").Should().Be(tasksBefore);
        var messages = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages;
        messages.Should().ContainSingle().Which.Outcome.Should().BeNull();
        messages[0].Handoff.Should().BeNull();
        var observed = await f.Store.ReadTaskAsync(offer.Context.Request.SessionId, offer.Context.Request.TaskId, f.Token);
        observed!.Task.State.Should().Be(HostTaskState.DispatchRecorded);
        observed.Task.Revision.Should().Be(new HostRevision(2));
        hosted.Sends.Should().Be(0);
        composed.Turns.IsQuiescent.Should().BeTrue();
    }
}

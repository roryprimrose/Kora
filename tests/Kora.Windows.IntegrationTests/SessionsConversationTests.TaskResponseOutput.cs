using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using AwesomeAssertions;

using Kora.Core.Voice;
using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false, TaskResponseMode.Inherit)]
    [InlineData(true, TaskResponseMode.VisualOnly)]
    [InlineData(false, TaskResponseMode.Hybrid)]
    [InlineData(true, TaskResponseMode.VoiceOnly)]
    public async Task NativeOneTaskOutputBothSizesReviewThenActuallySubmitRetainAndRenderWithoutPreferenceWrites(
        bool compact, TaskResponseMode mode)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f, adapter: new LocalProfileAdapter());
            using var native = new NativeUxFixtureSession(Path.GetFullPath("."));
            await native.InitializeAsync();
            native.Main.BindSessionCommands(composed.Workspace,
                new(composed.Workspace, composed.Conversation, composed.Access, f.Time));
            var model = composed.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            var owner = new SessionsPresentationOwner(model, native.Main);
            Window window = compact ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
            window.Show();
            try
            {
                var panel = window.GetLogicalDescendants().OfType<TaskResponseOutputPanel>().Single();
                const string body = "  Original e\u0301🙂\r\nend -- data  ";
                model.ComposerBody = body;
                panel.GetLogicalDescendants().OfType<ComboBox>().Single().SelectedItem = mode;
                model.TaskOutputMode.Should().Be(mode);
                f.Count("conversation_messages").Should().Be(0);
                panel.GetLogicalDescendants().OfType<Button>().Single(button => string.Equals(button.Name, "ReviewTaskOutput", StringComparison.Ordinal))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await panel.Operation;
                model.TaskOutputReviewText.Should().Contain(body).And.Contain("Declared output: " + mode);
                f.Count("conversation_messages").Should().Be(0);
                panel.GetLogicalDescendants().OfType<Button>().Single(button => string.Equals(button.Name, "ConfirmTaskOutput", StringComparison.Ordinal))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await panel.Operation;
                model.ConversationStatus.Should().Contain("Succeeded");
                var retained = model.ConversationMessages.Single();
                retained.Body.Should().Be(body);
                retained.AssistantResponse!.Body.Should().Be("Genuine chosen local answer");
                native.Main.ResponseBody.Should().Be(retained.AssistantResponse.Body);
                native.Main.IsVisualResponseVisible.Should().BeTrue("the headless host has no output endpoint; VoiceOnly must fall back");
                native.Main.TaskResponseMode.Should().BeNull();
                native.Main.DefaultResponseMode.Should().Be(ResponseOutputMode.Hybrid);
                model.ComposerBody.Should().BeEmpty();
                model.CanConfirmTaskOutput.Should().BeFalse();
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
            }
            finally { owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("input")]
    [InlineData("mode")]
    [InlineData("replacement")]
    [InlineData("close")]
    [InlineData("privacy")]
    public async Task NativeOneTaskOutputReviewRetiresOldCallbacksWithoutSubmissionOrPrivateReplay(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f, adapter: new LocalProfileAdapter());
            using var native = new NativeUxFixtureSession(Path.GetFullPath("."));
            await native.InitializeAsync();
            native.Main.BindSessionCommands(composed.Workspace,
                new(composed.Workspace, composed.Conversation, composed.Access, f.Time));
            var model = composed.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            model.ComposerBody = "Exact original input";
            model.TaskOutputMode = TaskResponseMode.VoiceOnly;
            await model.ReviewTaskOutputAsync();
            var stale = model.CaptureTaskOutputDecision(true);
            if (change is "cancel") { await model.CaptureTaskOutputDecision(false)(); }
            if (change is "input") { model.ComposerBody = model.ComposerBody; }
            if (change is "mode") { model.TaskOutputMode = TaskResponseMode.VisualOnly; }
            if (change is "replacement") { await model.ReviewTaskOutputAsync(); }
            if (change is "close") { model.Close(); }
            if (change is "privacy") { composed.Access.CanInspect = false; }
            await stale();
            f.Count("conversation_messages").Should().Be(0);
            native.Main.ResponseBody.Should().NotBe("Genuine chosen local answer");
            if (change is "close" or "privacy") { model.TaskOutputReviewText.Should().NotContain("Exact original input"); }
            model.Close();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TaskOutputSeparateSessionsAndQueuedTurnsOwnImmutableChoicesWithoutProviderPayloadFields(bool overlap)
    {
        using var f = new InteractionStorageFixture();
        f.Time.UseFrozenTimers = true;
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        f.Time.UseFrozenTimers = true;
        var original = f.Request.SessionId;
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        await WindowsSqliteConversationTests.FinishInitial(f);
        var adapter = new PendingTurnAdapter();
        await using var composed = new Composition(f, adapter: adapter);
        var firstTarget = await f.Store.ReadMetadataAsync(original, f.Token);
        var otherTarget = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var observed = new List<(ConversationSubmissionReceipt Receipt, TaskResponseMode Mode)>();
        var allOutput = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        composed.Conversation.BindTaskResponseOutput((mode, _) => new(mode, ResponseOutputMode.Hybrid, () => true,
            (receipt, output, current, _) =>
            {
                current().Should().BeTrue();
                lock (observed)
                {
                    observed.Add((receipt, output.Declared));
                    if (observed.Count == 3) { allOutput.TrySetResult(); }
                }
                return Task.CompletedTask;
            }));
        var commands = composed.Conversation.CreateRequestCommands();
        var otherCommands = composed.Conversation.CreateRequestCommands();
        try
        {
            var firstReview = await ReviewTaskOutput(commands, firstTarget, "first", TaskResponseMode.VisualOnly, f.Token);
            var firstOperation = ConfirmTaskOutput(commands, firstReview, f.Token);
            var first = await adapter.NextAsync().WaitAsync(TimeSpan.FromSeconds(20), f.Token);
            var queuedReview = await ReviewTaskOutput(commands, firstTarget, "queued", TaskResponseMode.VoiceOnly, f.Token);
            var queued = (await ConfirmTaskOutput(commands, queuedReview, f.Token)).RequestSubmission!;
            queued.Message.Outcome.Should().BeNull();
            var queuedCompletion = new TaskCompletionSource<ConversationTurnCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
            composed.Conversation.SubmittedConversationCompleted += (_, completion) =>
            {
                if (completion.Receipt?.Message.Request == queued.Message.Request)
                { queuedCompletion.TrySetResult(completion); }
            };
            Task<SessionCommandResult>? otherOperation = null;
            if (overlap)
            {
                var otherReview = await ReviewTaskOutput(otherCommands, otherTarget, "other session", TaskResponseMode.Hybrid, f.Token);
                otherOperation = ConfirmTaskOutput(otherCommands, otherReview, f.Token);
                var busy = (await otherOperation).RequestSubmission!;
                busy.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
                busy.Message.Reason.Should().Be(ModelTurnReason.ProviderBusy);
                busy.Message.AssistantResponse.Should().BeNull();
                adapter.Calls.Should().Be(1);
                first.Completion.Task.IsCompleted.Should().BeFalse();
            }
            first.Context.EnumerateObject().Select(property => property.Name).Should().NotContain("ResponseMode")
                .And.NotContain("TaskResponseMode").And.NotContain("Output");
            first.Release();
            var next = await adapter.NextAsync().WaitAsync(TimeSpan.FromSeconds(20), f.Token);
            next.Provenance.Request.Should().Be(queued.Message.Request);
            next.Context.GetProperty("Evidence").GetArrayLength().Should().Be(0);
            next.Context.EnumerateObject().Select(property => property.Name).Should().NotContain("TaskResponseMode");
            next.Release();
            await firstOperation;
            (await queuedCompletion.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Token)).RequireReceipt()
                .Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
            composed.Turns.IsQuiescent.Should().BeTrue();
            if (otherOperation is null)
            {
                var otherReview = await ReviewTaskOutput(otherCommands, otherTarget, "other session", TaskResponseMode.Hybrid, f.Token);
                otherOperation = ConfirmTaskOutput(otherCommands, otherReview, f.Token);
                var other = await adapter.NextAsync().WaitAsync(TimeSpan.FromSeconds(20), f.Token);
                other.Provenance.Request.SessionId.Should().Be(otherTarget.Authority.SessionId);
                other.Provenance.Request.Should().NotBe(queued.Message.Request);
                other.Context.GetProperty("Evidence").GetArrayLength().Should().Be(0);
                other.Context.EnumerateObject().Select(property => property.Name).Should().NotContain("TaskResponseMode");
                other.Release();
            }
            await otherOperation;
            await allOutput.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Token);
            observed.Should().HaveCount(3);
            adapter.Calls.Should().Be(overlap ? 2 : 3);
            observed.Single(item => item.Mode == TaskResponseMode.Hybrid).Receipt.Message.Outcome
                .Should().Be(overlap ? ModelTurnOutcome.Unavailable : ModelTurnOutcome.Succeeded);
            observed.Single(item => string.Equals(item.Receipt.Message.Body, "first", StringComparison.Ordinal)).Mode.Should().Be(TaskResponseMode.VisualOnly);
            observed.Single(item => string.Equals(item.Receipt.Message.Body, "queued", StringComparison.Ordinal)).Mode.Should().Be(TaskResponseMode.VoiceOnly);
            observed.Single(item => string.Equals(item.Receipt.Message.Body, "other session", StringComparison.Ordinal)).Mode.Should().Be(TaskResponseMode.Hybrid);
            observed.Select(item => item.Receipt.Message.Request.TaskId).Should().OnlyHaveUniqueItems();
            observed.Single(item => item.Mode == TaskResponseMode.Hybrid).Receipt.Message.Request.SessionId
                .Should().Be(otherTarget.Authority.SessionId);
            f.Count("conversation_drafts").Should().Be(0);
            f.Count("host_questions").Should().Be(0);
        }
        finally { adapter.ReleaseAll(); await composed.Conversation.DisposeAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TaskOutputQueueNeverRenewsExpiryOrSpeaksCancelledPendingWork(bool cancel)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        f.Time.UseFrozenTimers = true;
        var adapter = new PendingTurnAdapter();
        await using var composed = new Composition(f, adapter: adapter);
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var outputs = 0;
        TaskResponseOutputReview? outputReview = null;
        composed.Conversation.BindTaskResponseOutput((mode, _) => outputReview = new(mode, ResponseOutputMode.Hybrid, () => true,
            (_, _, _, _) => { outputs++; return Task.CompletedTask; }));
        var commands = composed.Conversation.CreateRequestCommands();
        var active = await composed.Conversation.SubmitQueuedAsync(target, "blocking original", () => true, f.Token);
        var invocation = await adapter.NextAsync();
        try
        {
            var review = await ReviewTaskOutput(commands, target, "queued one-task choice", TaskResponseMode.VoiceOnly, f.Token);
            var queued = (await ConfirmTaskOutput(commands, review, f.Token)).RequestSubmission!;
            var completed = new TaskCompletionSource<ConversationTurnCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
            composed.Conversation.SubmittedConversationCompleted += (_, completion) =>
            {
                if (completion.Receipt?.Message.Request == queued.Message.Request) { completed.TrySetResult(completion); }
            };
            var snapshot = await composed.Conversation.ReadPendingConversationAsync(target, () => true, f.Token);
            var pending = snapshot.Turns.Single(item => item.Submission.Message.Request == queued.Message.Request);
            if (cancel)
            {
                await composed.Conversation.CancelPendingConversationAsync(target, pending, snapshot.Revision, () => true, f.Token);
            }
            else { f.Time.Now = f.Time.Now.AddSeconds(20); }
            invocation.Release();
            await active.Completion;
            var terminal = (await completed.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Token)).RequireReceipt();
            terminal.Message.Outcome.Should().Be(cancel ? ModelTurnOutcome.Cancelled : ModelTurnOutcome.Denied);
            terminal.Message.Reason.Should().Be(cancel ? ModelTurnReason.CallerCancelled : ModelTurnReason.ContextExpired);
            terminal.Message.AssistantResponse.Should().BeNull();
            outputReview!.Status.Should().Contain("withheld");
            adapter.Calls.Should().Be(1);
            outputs.Should().Be(0);
        }
        finally { adapter.ReleaseAll(); await composed.Conversation.DisposeAsync(); }
    }

    private static async Task<SessionRequestReview> ReviewTaskOutput(SessionRequestCommandService commands,
        SessionWorkspaceEntry target, string body, TaskResponseMode mode, CancellationToken token) =>
        (await commands.ExecuteAsync(new(SessionCommandOperation.RequestReview, target.Authority.SessionId.Value,
            target.Authority.Generation.Value, target.Metadata?.Revision.Value ?? 0)
        { RequestBody = body, RequestTaskResponseMode = mode }, RequestOrigin.LocalUi, () => true, token)).RequestReview!;

    private static Task<SessionCommandResult> ConfirmTaskOutput(SessionRequestCommandService commands,
        SessionRequestReview review, CancellationToken token) => commands.ExecuteAsync(
            new(SessionCommandOperation.RequestConfirm, review.Target.Authority.SessionId.Value) { RequestReviewId = review.Id },
            RequestOrigin.LocalUi, () => true, token);
}

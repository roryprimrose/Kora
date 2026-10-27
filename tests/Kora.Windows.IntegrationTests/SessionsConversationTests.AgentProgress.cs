using System.Collections.Concurrent;
using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application;
using Kora.Application.Agents;
using Kora.Application.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("cancel")]
    [InlineData("source")]
    [InlineData("input")]
    [InlineData("stop")]
    [InlineData("dispose")]
    [InlineData("privacy")]
    [InlineData("expiry")]
    public async Task NativeReviewedAgentShowsExactHostStepsAndOnlyJoinedFinalOrCancellation(string ending)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new HeldAgentStepAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            var sessions = new SessionsWindow(model);
            sessions.Show();
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent);
            await native.InitializeAsync();
            var reader = ObservationAgentReader();
            await using var workflow = new BoundedAgentWorkflow(reader, f.Store, composed.Access,
                composed.Conversation, composed.Turns, composed.Audit, f.Time);
            var controller = new AgentRunController(native.Main, workflow, composed.Workspace,
                () => true, () => 1, NullLogger<AgentRunController>.Instance);
            controller.Bind();
            var statuses = new ConcurrentQueue<ConversationExecutionStatus>();
            composed.Conversation.RunningConversationChanged += statuses.Enqueue;
            AgentRunWindow? opened = null;
            using var observation = Window.WindowOpenedEvent.AddClassHandler<AgentRunWindow>((window, _) => opened = window);
            Task? closing = null;
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                model.ComposerBody = "unrelated unsent input must survive the agent";
                native.Main.CommandText = "review agent";
                await native.Main.RunTypedCommand.ExecuteAsync();
                var window = opened ?? throw new InvalidOperationException("The original native agent route did not open.");
                await PrepareObservationAgentAsync(window, controller);
                adapter.Calls.Should().Be(0);
                window.Status.Text.Should().Contain("Separately consent");
                model.ConversationMessages.Should().BeEmpty();
                window.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var provenance = await adapter.Started.Task.WaitAsync(f.Token);
                await WaitForAgentStatusAsync(model, text => text.Contains("model answer generation", StringComparison.Ordinal), f.Token);
                model.RunningConversationStatus.Should().Contain(provenance.Request.TaskId.Value.ToString("D"));
                model.CanCancelRunningConversation.Should().BeTrue();
                controller.ActiveOperation.IsCompleted.Should().BeFalse();
                reader.Released.Should().BeFalse();
                adapter.ReadObservation.SetResult();
                (await adapter.Observed.Task.WaitAsync(f.Token)).Outcome.Should().Be(CapabilityOutcome.Succeeded);
                await model.RefreshRunningConversationAsync();
                model.RunningConversationStatus.Should().Contain("model answer generation").And.Contain("not agent reasoning");
                statuses.Where(status => status.Progress?.Step == ConversationExecutionStep.ReadingObservation)
                    .Should().ContainSingle().Which.Progress!.ObservationCount.Should().Be(1);
                statuses.Should().OnlyContain(status => status.Target.Request == provenance.Request);
                if (ending is "cancel")
                {
                    sessions.FindControl<Button>("CancelRunningConversation")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await sessions.ConversationCancellationOperation;
                    model.RunningConversationStatus.Should().Contain("Cancelling").And.Contain("waiting for owned resources");
                    model.CanCancelRunningConversation.Should().BeFalse();
                }
                else if (ending is "source") { reader.Text += "\nThe source was replaced."; }
                else if (ending is "input")
                {
                    window.Input.Text = "replacement input is not the admitted task";
                    model.RunningConversationStatus.Should().NotContain("model answer generation");
                    model.CanCancelRunningConversation.Should().BeFalse();
                }
                else if (ending is "stop")
                {
                    native.Main.CommandText = "agent stop";
                    closing = native.Main.RunTypedCommand.ExecuteAsync();
                }
                else if (ending is "dispose") { closing = native.Main.ExitAsync(); }
                else if (ending is "privacy")
                {
                    composed.Access.CanInspect = composed.Access.CanControl = false;
                    composed.Access.ControlRevision++;
                    model.RunningConversationStatus.Should().Contain("cleared");
                    model.CanCancelRunningConversation.Should().BeFalse();
                }
                else if (ending is "expiry") { f.Time.Now = f.Time.Now.AddMinutes(2); }
                if (ending is "cancel" or "input" or "stop" or "dispose")
                { await adapter.Cancelled.Task.WaitAsync(f.Token); }
                if (ending is not "success")
                {
                    controller.ActiveOperation.IsCompleted.Should().BeFalse("revocation is not a physical provider join");
                    reader.Released.Should().BeFalse();
                    composed.Turns.IsQuiescent.Should().BeFalse();
                    if (closing is not null) { closing.IsCompleted.Should().BeFalse(); }
                }
                adapter.Release.SetResult(ending is not "success");
                await controller.ActiveOperation;
                if (closing is not null) { await closing; }
                composed.Turns.IsQuiescent.Should().BeTrue();
                reader.Released.Should().BeTrue();
                var retained = await f.Store.ReadConversationMessageAsync(provenance.Request.SessionId,
                    provenance.Request.RequestId, f.Token);
                retained.Body.Should().Be("Original bounded native task.");
                if (ending is "success")
                {
                    retained.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
                    retained.AssistantResponse!.Body.Should().Be("Genuine bounded final answer.");
                    window.Result.Text.Should().Be(retained.AssistantResponse.Body);
                    await WaitForAgentStatusAsync(model, text => text.Contains("Terminal Succeeded", StringComparison.Ordinal), f.Token);
                    model.RunningConversationStatus.Should().Contain("resources joined");
                }
                else
                {
                    retained.AssistantResponse.Should().BeNull();
                    retained.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
                    adapter.LateReply.Should().NotBeNull();
                    adapter.LateReply!.Outcome.Should().Be(CapabilityOutcome.Denied);
                    if (ending is "cancel")
                    {
                        window.Result.Text.Should().Be("No assistant answer was produced.");
                        retained.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
                        await WaitForAgentStatusAsync(model, text => text.Contains("Terminal Cancelled", StringComparison.Ordinal), f.Token);
                    }
                    else { window.Result.Text.Should().BeEmpty(); }
                    composed.Audit.Events.Should().NotContain(item => item.ActionId == "agents.revision.invoke"
                        && item.Outcome == Kora.Core.Auditing.SecurityAuditOutcome.Succeeded);
                }
                statuses.Count(status => status.Progress?.Step == ConversationExecutionStep.ReadingObservation)
                    .Should().Be(1, "late callbacks cannot start another observation or revive step progress");
                if (ending is "success" or "cancel")
                {
                    sessions.FindControl<Button>("ReadConversation")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await sessions.ConversationOperation;
                    model.ConversationMessages.Should().ContainSingle().Which.Should().Be(retained);
                }
                adapter.Calls.Should().Be(1);
                f.Count("conversation_messages").Should().Be(1);
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                if (ending is not "privacy") { model.ComposerBody.Should().Be("unrelated unsent input must survive the agent"); }
            }
            finally
            {
                adapter.ReadObservation.TrySetResult();
                adapter.Release.TrySetResult(false);
                await controller.ActiveOperation;
                if (closing is not null) { await closing; }
                await native.Main.ExitAsync();
                sessions.ClosePrivate();
            }
        });
    }

    [Theory]
    [InlineData("current")]
    [InlineData("switch")]
    [InlineData("aba")]
    [InlineData("revision")]
    [InlineData("privacy")]
    [InlineData("input")]
    [InlineData("close")]
    public async Task DeferredActualAgentStepsCannotReviveRetiredSourceOrReplaceNewerProgress(string retirement)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var original = f.Request.SessionId;
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new HeldAgentStepAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var dispatcher = new ReorderedAgentDispatcher();
            var model = CreateDeferredPresentation(f, composed, dispatcher);
            var reader = ObservationAgentReader();
            await using var workflow = new BoundedAgentWorkflow(reader, f.Store, composed.Access,
                composed.Conversation, composed.Turns, composed.Audit, f.Time);
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
            using var root = Kora.Core.Diagnostics.HostActivity.BeginRoot(
                Kora.Core.Hosting.HostRequest.Create(Kora.Core.Hosting.RequestOrigin.LocalUi),
                Kora.Core.Diagnostics.HostActivityLayer.Desktop, Kora.Core.Diagnostics.HostOperation.Request);
            using var review = await workflow.ReviewAsync("selected.agent.md", model.SelectedSessionRecord!,
                "Original bounded native task.", Kora.Core.Hosting.RequestOrigin.LocalUi, () => true, f.Token);
            var running = Task.Run(() => workflow.InvokeAsync(review, f.Token));
            try
            {
                var provenance = await adapter.Started.Task.WaitAsync(f.Token);
                adapter.ReadObservation.SetResult();
                await adapter.Observed.Task.WaitAsync(f.Token);
                dispatcher.Count.Should().BeGreaterThan(1);
                if (retirement is "switch" or "aba")
                {
                    await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != original));
                    model.ComposerBody = "the other conversation's unsent input";
                    if (retirement is "aba")
                    { await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original)); }
                }
                else if (retirement is "revision") { composed.Access.ControlRevision++; }
                else if (retirement is "privacy") { composed.Access.CanInspect = composed.Access.CanControl = false; }
                else if (retirement is "input") { review.Dispose(); }
                else if (retirement is "close") { model.Close(); }
                dispatcher.DrainNewestFirst();
                if (retirement is "current")
                {
                    model.RunningConversationStatus.Should().Contain("model answer generation")
                        .And.NotContain("Running admitted observation");
                    model.CanCancelRunningConversation.Should().BeTrue();
                }
                else
                {
                    model.RunningConversationStatus.Should().NotContain(provenance.Request.TaskId.Value.ToString("D"));
                    model.CanCancelRunningConversation.Should().BeFalse();
                }
                if (retirement is "switch")
                { model.ComposerBody.Should().Be("the other conversation's unsent input"); }
                model.ConversationMessages.Should().BeEmpty();
                model.ProvisionalAnswer.Should().BeEmpty();
                adapter.Release.SetResult(retirement is "input" or "privacy" or "revision");
                if (retirement is "input" or "privacy" or "revision")
                {
                    await running.Invoking(value => value).Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
                    (await f.Store.ReadConversationMessageAsync(original, provenance.Request.RequestId, f.Token))
                        .AssistantResponse.Should().BeNull();
                }
                else { await running; }
                await workflow.RevokeAsync(review);
                reader.Released.Should().BeTrue();
                composed.Turns.IsQuiescent.Should().BeTrue();
                composed.Audit.Events.Should().NotContain(item => item.ActionId == "conversation.cancel-request");
                f.Count("conversation_messages").Should().Be(1);
                f.Count("conversation_drafts").Should().Be(0);
            }
            finally
            {
                adapter.ReadObservation.TrySetResult();
                adapter.Release.TrySetResult(false);
                try { await running; }
                catch (ConversationSubmissionUnconfirmedException) when (retirement is "input" or "privacy" or "revision") { }
                model.Close();
                dispatcher.DrainNewestFirst();
            }
        });
    }

    private static NativeAgentReader ObservationAgentReader()
    {
        var reader = new NativeAgentReader();
        reader.Text = reader.Text.Replace("tools: none", "tools: application.get_version", StringComparison.Ordinal)
            .Replace("maximum-calls: 0", "maximum-calls: 1", StringComparison.Ordinal)
            .Replace("maximum-rounds: 1", "maximum-rounds: 2", StringComparison.Ordinal);
        return reader;
    }

    private static async Task PrepareObservationAgentAsync(AgentRunWindow window, AgentRunController controller)
    {
        var buttons = ((StackPanel)((ScrollViewer)window.Content!).Content!).Children.OfType<Button>().ToArray();
        buttons.Single(button => Equals(button.Content, "Refresh existing Active conversations"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await controller.ActiveOperation;
        window.Targets.SelectedIndex = 0;
        window.Source.Text = @"C:\Users\owner\existing.agent.md";
        window.Input.Text = "Original bounded native task.";
        buttons.Single(button => Equals(button.Content, "Read source and review exact invocation"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await controller.ActiveOperation;
        window.Confirm.IsEnabled.Should().BeTrue();
    }

    private static async Task WaitForAgentStatusAsync(SessionsViewModel model, Func<string, bool> matches, CancellationToken token)
    {
        var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, PropertyChangedEventArgs args)
        {
            if (string.Equals(args.PropertyName, nameof(SessionsViewModel.RunningConversationStatus), StringComparison.Ordinal)
                && matches(model.RunningConversationStatus))
            { seen.TrySetResult(); }
        }
        model.PropertyChanged += Changed;
        try
        {
            if (matches(model.RunningConversationStatus)) { return; }
            await seen.Task.WaitAsync(token);
        }
        finally { model.PropertyChanged -= Changed; }
    }

    private sealed class ReorderedAgentDispatcher : IUiDispatcher
    {
        private readonly ConcurrentStack<Action> callbacks = new();
        internal int Count => callbacks.Count;
        public Task InvokeAsync(Func<Task> action) => throw new NotSupportedException("Execution notification posts only.");
        public void Post(Action action) => callbacks.Push(action);
        internal void DrainNewestFirst()
        {
            while (callbacks.TryPop(out var callback)) { callback(); }
        }
    }

    private sealed class HeldAgentStepAdapter : IModelTurnAdapter
    {
        internal int Calls { get; private set; }
        internal TaskCompletionSource<ModelTurnProvenance> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReadObservation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<CapabilityReply> Observed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CapabilityReply? LateReply { get; private set; }
        public async Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken token)
        {
            Calls++;
            using var registration = token.Register(() => Cancelled.TrySetResult());
            Started.SetResult(provenance);
            await ReadObservation.Task;
            Observed.SetResult(await invokeTool(ReadOnlyCapabilityCatalog.Version, "{}", token));
            if (await Release.Task)
            {
                try { LateReply = await invokeTool(ReadOnlyCapabilityCatalog.Version, "{}", CancellationToken.None); }
                catch (Exception exception) when (exception is OperationCanceledException or InvalidDataException or InvalidOperationException)
                { LateReply = new(CapabilityOutcome.Denied, "late-callback-rejected"); }
            }
            return new(ModelTurnOutcome.Succeeded, provenance.Selection, new("Genuine bounded final answer.", null));
        }
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeExactRunningTaskCancellationShowsPendingJoinThenTruthfulTerminalWithoutLateAnswer(bool closePrivate)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new GateableConversationAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                model.ComposerBody = "original admitted conversation request";
                window.FindControl<Button>("SubmitConversation")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var provenance = await adapter.Started.Task;
                await model.RefreshRunningConversationAsync();
                model.RunningConversationStatus.Should().Contain("Running").And.Contain(provenance.Request.TaskId.Value.ToString("D"));
                var cancel = window.FindControl<Button>("CancelRunningConversation")!;
                cancel.IsEnabled.Should().BeTrue();
                cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.ConversationCancellationOperation;
                model.RunningConversationStatus.Should().Contain("Cancelling").And.Contain("waiting for owned resources");
                window.ConversationOperation.IsCompleted.Should().BeFalse();
                model.CanCancelRunningConversation.Should().BeFalse();
                model.ConversationMessages.Should().ContainSingle().Which.Outcome.Should().BeNull(
                    "accepted original intent is visible, but neither success nor terminal cancellation precedes the physical join");
                composed.Audit.Events.Count(value => string.Equals(value.ActionId, "conversation.cancel-request", StringComparison.Ordinal)).Should().Be(2);
                Task? physicalJoin = null;
                if (closePrivate)
                {
                    window.ClosePrivate();
                    model.RunningConversationStatus.Should().Contain("cleared");
                    physicalJoin = composed.Conversation.DisposeAsync().AsTask();
                    physicalJoin.IsCompleted.Should().BeFalse("retiring UI observation cannot release the host-owned physical worker");
                }
                adapter.Release.SetResult(new(ModelTurnOutcome.Succeeded, provenance.Selection, new("late forbidden answer", null)));
                await window.ConversationOperation;
                if (physicalJoin is not null) { await physicalJoin; }
                var stored = await f.Store.ReadConversationMessageAsync(provenance.Request.SessionId, provenance.Request.RequestId, f.Token);
                stored.AssistantResponse.Should().BeNull();
                if (closePrivate)
                {
                    model.ConversationMessages.Should().BeEmpty();
                    model.RunningConversationStatus.Should().Contain("cleared");
                    model.CanCancelRunningConversation.Should().BeFalse();
                }
                else
                {
                    stored.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
                    model.ConversationMessages.Single().Outcome.Should().Be(ModelTurnOutcome.Cancelled);
                    model.RunningConversationStatus.Should().Contain("Terminal Cancelled").And.Contain("resources joined");
                    model.ConversationStatus.Should().Contain("No assistant answer retained");
                    model.ComposerBody.Should().BeEmpty();
                }
            }
            finally
            {
                adapter.Release.TrySetResult(new(ModelTurnOutcome.Unknown, ModelProviderSelection.OllamaCandidate));
                window.ClosePrivate();
            }
        });
    }

    private sealed class GateableConversationAdapter : IModelTurnAdapter
    {
        internal TaskCompletionSource<ModelTurnProvenance> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<ModelProviderReply> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken token)
        {
            Started.SetResult(provenance);
            return Release.Task;
        }
    }
}

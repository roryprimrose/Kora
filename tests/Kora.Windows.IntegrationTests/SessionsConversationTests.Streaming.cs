using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("primary", "complete")]
    [InlineData("sessions", "complete")]
    [InlineData("compact", "complete")]
    [InlineData("primary", "cancel")]
    [InlineData("sessions", "cancel")]
    [InlineData("compact", "cancel")]
    [InlineData("primary", "privacy")]
    [InlineData("sessions", "privacy")]
    [InlineData("compact", "privacy")]
    [InlineData("primary", "control")]
    public async Task NativeLocalAnswerUsesNormalConversationAndNeverStoresOrKeepsRevokedProvisionalText(string surface, string terminal)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new ProvisionalConversationAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            var view = surface is "primary" ? new SessionInteractionView { DataContext = model } : null;
            Window window = surface switch
            {
                "sessions" => new SessionsWindow(model, owner),
                "compact" => new CompactSessionWindow(model, owner),
                _ => new Window { Content = view },
            };
            window.Show();
            Task operation = Task.CompletedTask;
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                model.ComposerBody = "original local no-tools question";
                var controls = (Control?)view ?? window;
                controls.FindControl<Button>("SubmitLocalAnswer")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                operation = window switch
                {
                    SessionsWindow sessions => sessions.ConversationOperation,
                    CompactSessionWindow compact => compact.ConversationOperation,
                    _ => view!.Operation,
                };
                var first = await Task.WhenAny(adapter.Started.Task, operation).WaitAsync(f.Token);
                if (ReferenceEquals(first, operation))
                { throw new InvalidOperationException("Streaming did not start: " + model.ConversationStatus); }
                var provenance = await adapter.Started.Task;
                model.ProvisionalAnswer.Should().Be("provisional answer");
                model.HasProvisionalAnswer.Should().BeTrue();
                model.RunningConversationStatus.Should().Contain("Running");
                model.CanCancelRunningConversation.Should().BeTrue();
                operation.IsCompleted.Should().BeFalse();
                var stored = await f.Store.ReadConversationMessageAsync(provenance.Request.SessionId,
                    provenance.Request.RequestId, f.Token);
                stored.AssistantResponse.Should().BeNull();
                stored.Outcome.Should().BeNull();
                if (terminal is "cancel")
                {
                    if (view is not null)
                    {
                        view.FindControl<Button>("StopLocalAnswer")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await view.CancellationOperation;
                        view.Operation.Should().BeSameAs(operation, "Stop must not replace the owned answer join");
                    }
                    else { await model.CancelRunningConversationAsync(); }
                    model.ProvisionalAnswer.Should().BeEmpty();
                    model.HasProvisionalAnswer.Should().BeFalse();
                    model.RunningConversationStatus.Should().Contain("Cancelling");
                    operation.IsCompleted.Should().BeFalse();
                }
                if (terminal is "privacy")
                {
                    composed.Access.CanInspect = false;
                    composed.Access.CanControl = false;
                    owner.Revoke();
                    model.ProvisionalAnswer.Should().BeEmpty();
                }
                if (terminal is "control")
                {
                    composed.Access.ControlRevision++;
                    model.ProvisionalAnswer.Should().BeEmpty();
                }
                adapter.Release.TrySetResult();
                await operation;
                stored = await f.Store.ReadConversationMessageAsync(provenance.Request.SessionId, provenance.Request.RequestId, f.Token);
                model.ProvisionalAnswer.Should().BeEmpty();
                if (terminal is "complete")
                {
                    stored.AssistantResponse!.Body.Should().Be("provisional answer completed");
                    model.ConversationMessages.Single().Should().Be(stored);
                    model.RunningConversationStatus.Should().Contain("Terminal Succeeded");
                }
                else { stored.AssistantResponse.Should().BeNull(); }
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                adapter.BufferedCalls.Should().Be(0);
            }
            finally
            {
                adapter.Release.TrySetResult();
                await operation;
                owner.Revoke();
                window.Close();
            }
        });
    }

    private sealed class ProvisionalConversationAdapter : IModelTurnAdapter, IModelAnswerTurnAdapter
    {
        internal TaskCompletionSource<ModelTurnProvenance> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int BufferedCalls { get; private set; }

        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken cancellationToken)
        {
            BufferedCalls++;
            return Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Denied, provenance.Selection));
        }

        public async Task<ModelProviderReply> RunWithAnswerAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            Func<CancellationToken, ValueTask<ModelTurnReason>> validateContinuation,
            Func<ModelAnswerUpdate, CancellationToken, ValueTask> publish, CancellationToken cancellationToken)
        {
            ModelToolProtocol.ReadContext(context.Span, provenance).ToolMode.Should().Be(ModelToolMode.None);
            await publish(new(provenance, "provisional answer"), cancellationToken);
            Started.TrySetResult(provenance);
            await Release.Task;
            await publish(new(provenance, "provisional answer completed"), CancellationToken.None);
            return new(ModelTurnOutcome.Succeeded, provenance.Selection, new("provisional answer completed", null));
        }
    }
}

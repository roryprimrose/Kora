using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

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
            var terminalPresented = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void ObserveTerminal(object? sender, PropertyChangedEventArgs args)
            {
                if (string.Equals(args.PropertyName, nameof(model.RunningConversationStatus), StringComparison.Ordinal)
                    && (model.RunningConversationStatus.Contains("Terminal Succeeded", StringComparison.Ordinal)
                        || model.RunningConversationStatus.Contains("Terminal Cancelled", StringComparison.Ordinal)))
                { terminalPresented.TrySetResult(); }
            }
            model.PropertyChanged += ObserveTerminal;
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
                if (terminal is "complete" or "cancel") { await terminalPresented.Task.WaitAsync(f.Token); }
                stored = await f.Store.ReadConversationMessageAsync(provenance.Request.SessionId, provenance.Request.RequestId, f.Token);
                model.ProvisionalAnswer.Should().BeEmpty();
                if (terminal is "complete")
                {
                    stored.AssistantResponse!.Body.Should().Be("provisional answer completed");
                    stored.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
                    model.ConversationMessages.Single().Should().Be(stored);
                    model.RunningConversationStatus.Should().Contain("Terminal Succeeded")
                        .And.Contain(provenance.Request.TaskId.Value.ToString("D")).And.Contain("resources joined");
                }
                else { stored.AssistantResponse.Should().BeNull(); }
                if (terminal is "cancel")
                {
                    stored.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
                    model.RunningConversationStatus.Should().Contain("Terminal Cancelled")
                        .And.Contain(provenance.Request.TaskId.Value.ToString("D")).And.Contain("resources joined");
                }
                composed.Turns.IsQuiescent.Should().BeTrue();
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                adapter.BufferedCalls.Should().Be(0);
            }
            finally
            {
                adapter.Release.TrySetResult();
                await operation;
                model.PropertyChanged -= ObserveTerminal;
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
        internal string? BufferedAnswer { get; init; }
        internal byte[]? Wire { get; private set; }

        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken cancellationToken)
        {
            BufferedCalls++;
            return Task.FromResult(BufferedAnswer is { } answer
                ? new ModelProviderReply(ModelTurnOutcome.Succeeded, provenance.Selection, new(answer, null))
                : new ModelProviderReply(ModelTurnOutcome.Denied, provenance.Selection));
        }

        public async Task<ModelProviderReply> RunWithAnswerAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            Func<CancellationToken, ValueTask<ModelTurnReason>> validateContinuation,
            Func<ModelAnswerUpdate, CancellationToken, ValueTask> publish, CancellationToken cancellationToken)
        {
            ModelToolProtocol.ReadContext(context.Span, provenance).ToolMode.Should().Be(ModelToolMode.None);
            Wire = context.ToArray();
            await publish(new(provenance, "provisional answer"), cancellationToken);
            Started.TrySetResult(provenance);
            await Release.Task;
            await publish(new(provenance, "provisional answer completed"), CancellationToken.None);
            return new(ModelTurnOutcome.Succeeded, provenance.Selection, new("provisional answer completed", null));
        }
    }

    [Theory]
    [InlineData("primary", "complete")]
    [InlineData("sessions", "complete")]
    [InlineData("compact", "complete")]
    [InlineData("primary", "cancel")]
    [InlineData("primary", "privacy")]
    [InlineData("primary", "control")]
    [InlineData("primary", "expiry")]
    public async Task ExactSourceReplyStreamsProvisionallyWithoutPlainSubmitFallbackAndRetainsOnlyCurrentFinalAnswer(string surface, string terminal)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new ProvisionalConversationAdapter { BufferedAnswer = "Exact retained ASSISTANT source." };
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            var view = surface is "primary" ? new SessionInteractionView { DataContext = model } : null;
            Window window = surface switch
            {
                "sessions" => new SessionsWindow(model, owner),
                "compact" => new CompactSessionWindow(model, owner),
                _ => new Window { Content = view, Width = 1200, Height = 1000 },
            };
            window.Show();
            Task operation = Task.CompletedTask;
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.ComposerBody = "Earlier USER is not reply evidence.";
                await model.SubmitConversationAsync();
                await model.ReadConversationAsync();
                var source = model.ConversationMessages.Single();
                const string followup = "  expand this exact answer\r\n e\u0301  ";
                model.ComposerBody = followup;
                if (view is null) { window.FindControl<ListBox>("AnswerSourceRecords")!.ScrollIntoView(source); }
                window.UpdateLayout();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.GetVisualDescendants().OfType<Button>().Single(button =>
                    string.Equals(button.Name, "ReplyToAssistantAnswer", StringComparison.Ordinal)
                    && ReferenceEquals(button.DataContext, source)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await (view?.Operation ?? owner.ReplyOperation);
                model.HasAssistantReply.Should().BeTrue(model.ConversationStatus);
                await model.SubmitLocalAnswerAsync();
                model.ConversationStatus.Should().Contain("Reply mode requires");
                adapter.BufferedCalls.Should().Be(1);
                adapter.Started.Task.IsCompleted.Should().BeFalse("local answer must not bypass reply review");
                model.ComposerBody.Should().Be(followup);
                await model.SubmitConversationAsync();
                model.CanConfirmAssistantReply.Should().BeTrue(model.ConversationStatus);
                model.ReplyReviewText.Should().Contain(source.AssistantResponse!.Body).And.Contain(followup).And.NotContain(source.Body);
                var panel = window.GetVisualDescendants().OfType<AssistantReplyPanel>().Single();
                panel.GetVisualDescendants().OfType<Button>().Single(button =>
                    string.Equals(button.Name, "ConfirmAssistantReply", StringComparison.Ordinal))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                operation = panel.Operation;
                var first = await Task.WhenAny(adapter.Started.Task, operation).WaitAsync(f.Token);
                first.Should().BeSameAs(adapter.Started.Task, model.ConversationStatus);
                var provenance = await adapter.Started.Task;
                operation.IsCompleted.Should().BeFalse();
                model.ProvisionalAnswer.Should().Be("provisional answer");
                model.HasAssistantReply.Should().BeTrue();
                model.ComposerBody.Should().Be(followup);
                var pending = await f.Store.ReadConversationMessageAsync(provenance.Request.SessionId, provenance.Request.RequestId, f.Token);
                pending.Body.Should().Be(followup);
                pending.AssistantResponse.Should().BeNull();
                pending.Outcome.Should().BeNull();
                using var wire = System.Text.Json.JsonDocument.Parse(adapter.Wire!);
                wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(followup);
                wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
                var evidence = wire.RootElement.GetProperty("Evidence").EnumerateArray().Single();
                evidence.GetProperty("Disclosure").GetInt32().Should().Be((int)ModelEvidenceDisclosure.LocalOnly);
                using var content = System.Text.Json.JsonDocument.Parse(evidence.GetProperty("Content").GetString()!);
                content.RootElement.GetProperty("Role").GetString().Should().Be("ASSISTANT");
                content.RootElement.GetProperty("Sequence").GetInt64().Should().Be(source.Sequence);
                content.RootElement.GetProperty("Body").GetString().Should().Be(source.AssistantResponse.Body);
                if (terminal is "cancel") { await model.CancelRunningConversationAsync(); }
                if (terminal is "privacy")
                {
                    composed.Access.CanInspect = false;
                    composed.Access.CanControl = false;
                    owner.Revoke();
                }
                if (terminal is "control") { composed.Access.ControlRevision++; }
                if (terminal is "expiry") { f.Time.Now = f.Time.Now.AddMinutes(1); }
                if (terminal is not "complete") { model.ProvisionalAnswer.Should().BeEmpty(); }
                adapter.Release.TrySetResult();
                await operation;
                await composed.Turns.WaitForQuiescenceAsync(f.Token);
                var retained = await f.Store.ReadConversationMessageAsync(provenance.Request.SessionId, provenance.Request.RequestId, f.Token);
                retained.Body.Should().Be(followup);
                retained.Request.SessionId.Should().Be(source.Request.SessionId);
                retained.Generation.Should().Be(source.Generation);
                if (terminal is "complete")
                {
                    retained.AssistantResponse!.Body.Should().Be("provisional answer completed");
                    retained.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
                    model.HasAssistantReply.Should().BeFalse();
                    model.ComposerBody.Should().BeEmpty();
                }
                else { retained.AssistantResponse.Should().BeNull(); }
                model.ProvisionalAnswer.Should().BeEmpty();
                (await f.Store.ReadConversationMessageAsync(source.Request.SessionId, source.Request.RequestId, f.Token)).Should().Be(source);
                adapter.BufferedCalls.Should().Be(1, "no fallback or automatic retry");
                f.Count("conversation_messages").Should().Be(2);
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                f.Count("reviewed_memory").Should().Be(0);
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
}

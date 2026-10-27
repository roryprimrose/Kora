using System.ComponentModel;
using System.Text.Json;
using System.Threading.Channels;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task NativeAcceptsAndCancelsExactSubmittedPendingRequestWhileFirstProviderIsPhysicallyHeld()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new PendingTurnAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            PendingTurnInvocation? first = null;
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.ComposerBody = "first original request";
                window.FindControl<Button>("SubmitConversation")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var firstOperation = window.ConversationOperation;
                first = await adapter.NextAsync();
                await WhenModelAsync(model, () => model.CanCompose && !model.ComposerDirty);
                model.ComposerBody = "  second immutable submitted input\r\n";
                window.FindControl<Button>("SubmitConversation")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.ConversationOperation;
                firstOperation.IsCompleted.Should().BeFalse();
                adapter.Calls.Should().Be(1);
                model.ComposerBody.Should().BeEmpty("only the real accepted second intent clears the composer");
                await model.RefreshPendingConversationAsync();
                var queued = model.PendingConversationTurns.Should().ContainSingle().Subject;
                queued.Position.Should().Be(1);
                queued.Submission.Message.Body.Should().Be("  second immutable submitted input\r\n");
                queued.Submission.Task.State.Should().Be(HostTaskState.IntentRecorded);
                model.ConversationStatus.Should().Contain("queued at position 1");
                model.ComposerBody = "unsent next request stays in memory";
                model.SelectedPendingConversation = queued;
                var panel = window.GetLogicalDescendants().OfType<PendingConversationPanel>().Single();
                panel.FindControl<Button>("CancelPending")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await panel.Operation;
                var cancelled = await f.Store.ReadConversationMessageAsync(queued.Submission.Message.Request.SessionId,
                    queued.Submission.Message.Request.RequestId, f.Token);
                cancelled.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
                cancelled.Execution.Should().BeNull();
                cancelled.AssistantResponse.Should().BeNull();
                adapter.Calls.Should().Be(1, "pending cancellation cannot acquire resources or invoke a provider");
                firstOperation.IsCompleted.Should().BeFalse("cancelling a pending request does not stop unrelated active work");
                model.ComposerBody.Should().Be("unsent next request stays in memory");
                (await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token)).Draft.Body.Should().BeNull();
                first.Release();
                await firstOperation;
                model.PendingConversationStatus.Should().Contain("held");
            }
            finally { first?.Release(); window.ClosePrivate(); await composed.Conversation.DisposeAsync(); }
        });
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task ActualServiceRetainsFifoOriginalBytesAndChannelWithoutAddingFutureHistory(RequestOrigin origin)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var adapter = new PendingTurnAdapter();
        await using var composed = new Composition(f, adapter: adapter);
        composed.Audit.ExpectedOrigin = origin;
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var a = await composed.Conversation.SubmitQueuedAsync(target, "first", () => true, f.Token, origin: origin);
        var first = await adapter.NextAsync();
        var b = await composed.Conversation.SubmitQueuedAsync(target, " B\r\n e\u0301 ", () => true, f.Token, origin: origin);
        var c = await composed.Conversation.SubmitQueuedAsync(target, "third", () => true, f.Token, origin: origin);
        try
        {
            b.Receipt.Message.Outcome.Should().BeNull("qualified pending admission must not return " + b.Receipt.Message.Reason);
            b.Position.Should().Be(1);
            c.Position.Should().Be(2);
            adapter.Calls.Should().Be(1);
            b.Completion.IsCompleted.Should().BeFalse();
            first.Release();
            (await a.Completion).RequireReceipt().Task.State.Should().Be(HostTaskState.Succeeded);
            var second = await adapter.NextAsync();
            second.Body.Should().Be(" B\r\n e\u0301 ");
            second.Context.GetProperty("Evidence").GetArrayLength().Should().Be(0);
            second.Provenance.Request.Should().Be(b.Receipt.Message.Request);
            second.Provenance.Request.Origin.Should().Be(origin);
            second.Release();
            (await b.Completion).RequireReceipt().Message.AssistantResponse!.Body.Should().Be("answer:  B\r\n e\u0301 ");
            var third = await adapter.NextAsync();
            third.Provenance.Request.Should().Be(c.Receipt.Message.Request);
            third.Release();
            (await c.Completion).RequireReceipt().Message.AssistantResponse!.Body.Should().Be("answer: third");
            adapter.Calls.Should().Be(3);
            var page = await f.Store.ReadConversationPageAsync(target.Authority.SessionId, null, 25, f.Token);
            page.Messages.Select(item => item.Body).Should().Equal("first", " B\r\n e\u0301 ", "third");
            page.Messages.Should().OnlyContain(item => item.Request.Origin == origin && item.Outcome == ModelTurnOutcome.Succeeded);
            f.Count("conversation_drafts").Should().Be(0);
            f.Count("scoped_grants").Should().Be(0);
        }
        finally { adapter.ReleaseAll(); await composed.Conversation.DisposeAsync(); }
    }

    [Fact]
    public async Task QueueHasAuthoritativeTenPendingBoundAndCancellingOneDoesNotEvictOrDispatchTheRest()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var adapter = new PendingTurnAdapter();
        await using var composed = new Composition(f, adapter: adapter);
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        await composed.Conversation.SubmitQueuedAsync(target, "active", () => true, f.Token);
        await adapter.NextAsync();
        try
        {
            for (var i = 0; i < 10; i++)
            { (await composed.Conversation.SubmitQueuedAsync(target, "pending " + i, () => true, f.Token)).Position.Should().Be(i + 1); }
            await composed.Conversation.Invoking(service => service.SubmitQueuedAsync(target, "overflow", () => true, f.Token))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*full*");
            f.Count("conversation_messages").Should().Be(11);
            var snapshot = await composed.Conversation.ReadPendingConversationAsync(target, () => true, f.Token);
            var exact = snapshot.Turns.Last();
            await composed.Conversation.CancelPendingConversationAsync(target, exact, snapshot.Revision, () => true, f.Token);
            var after = await composed.Conversation.ReadPendingConversationAsync(target, () => true, f.Token);
            after.Paused.Should().BeTrue();
            after.Turns.Where(item => item.Phase == ConversationPendingPhase.Pending).Select(item => item.Position)
                .Should().Equal(Enumerable.Range(1, 9));
            adapter.Calls.Should().Be(1);
            await composed.Conversation.Invoking(service => service.CancelPendingConversationAsync(target, exact,
                snapshot.Revision, () => true, f.Token)).Should().ThrowAsync<InvalidOperationException>();
        }
        finally { adapter.ReleaseAll(); await composed.Conversation.DisposeAsync(); }
    }

    [Fact]
    public async Task SwitchingConversationAfterAcceptanceKeepsOriginalResultAndIndependentUnsentInput()
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
            var adapter = new PendingTurnAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(item => item.Authority.SessionId == original));
                model.ComposerBody = "original accepted source";
                var submitted = model.SubmitConversationAsync();
                var active = await adapter.NextAsync();
                await WhenModelAsync(model, () => model.CanCompose && !model.ComposerDirty);
                await model.SelectAsync(model.Sessions.Single(item => item.Authority.SessionId != original));
                model.ComposerBody = "different session unsent input";
                active.Release();
                await submitted;
                var retained = await f.Store.ReadConversationMessageAsync(original, active.Provenance.Request.RequestId, f.Token);
                retained.AssistantResponse!.Body.Should().Be("answer: original accepted source");
                model.ConversationMessages.Should().BeEmpty();
                model.ComposerBody.Should().Be("different session unsent input");
                (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Should().BeEmpty();
                (await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token)).Draft.Body.Should().BeNull();
            }
            finally { adapter.ReleaseAll(); window.ClosePrivate(); }
        });
    }

    private static async Task WhenModelAsync(SessionsViewModel model, Func<bool> condition)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, PropertyChangedEventArgs args) { if (condition()) { signal.TrySetResult(); } }
        model.PropertyChanged += Changed;
        try { if (!condition()) { await signal.Task.WaitAsync(TestContext.Current.CancellationToken); } }
        finally { model.PropertyChanged -= Changed; }
    }

    private sealed class PendingTurnInvocation(ModelTurnProvenance provenance, ReadOnlyMemory<byte> bytes)
    {
        internal ModelTurnProvenance Provenance { get; } = provenance;
        internal JsonElement Context { get; } = JsonSerializer.Deserialize<JsonElement>(bytes.Span);
        internal string Body => Context.GetProperty("UserRequest").GetString()!;
        internal TaskCompletionSource<ModelProviderReply> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Release() => Completion.TrySetResult(new(ModelTurnOutcome.Succeeded, Provenance.Selection, new("answer: " + Body, null)));
    }

    private sealed class PendingTurnAdapter : IModelTurnAdapter
    {
        private readonly Channel<PendingTurnInvocation> started = Channel.CreateUnbounded<PendingTurnInvocation>();
        private readonly List<PendingTurnInvocation> invocations = [];
        internal int Calls { get { lock (invocations) { return invocations.Count; } } }
        internal async Task<PendingTurnInvocation> NextAsync() => await started.Reader.ReadAsync(TestContext.Current.CancellationToken);
        internal void ReleaseAll() { lock (invocations) { foreach (var item in invocations) { item.Release(); } } }
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool, CancellationToken token)
        {
            var invocation = new PendingTurnInvocation(provenance, context);
            lock (invocations) { invocations.Add(invocation); }
            started.Writer.TryWrite(invocation).Should().BeTrue();
            return invocation.Completion.Task;
        }
    }
}

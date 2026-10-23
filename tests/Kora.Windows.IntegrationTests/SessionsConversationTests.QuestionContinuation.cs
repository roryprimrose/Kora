using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Interaction;
using Kora.Core.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task ClosingQuestionDoesNotClaimQuiescenceBeforeItsInFlightAuditedReviewFinishes()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var spec = LocalModelQuestionPolicy.CreateSpec(new("Describe format", []));
        var record = (await f.RunAsync(() => f.Questions.CreateAsync(f.Request, spec, f.Time.Now.AddSeconds(15), f.Token))).Question!;
        var gated = new GatedQuestionStore(f.Store);
        var state = new NativeQuestionViewModel(record, new(gated, f.Time), new(gated, f.Time),
            new(gated, f.Time), static () => true, f.Time, NullLogger<NativeQuestionViewModel>.Instance);
        var review = state.ReviewAsync();
        try
        {
            await gated.Entered.Task.WaitAsync(f.Token);
            state.Close("Original question cancellation");
            state.Completion.IsCompleted.Should().BeTrue("logical question withdrawal is immediate");
            state.OperationQuiescence.IsCompleted.Should().BeFalse("the owned audit/store operation is still in flight");
            state.IsEditable.Should().BeFalse();
        }
        finally { gated.Release.TrySetResult(); }
        await review;
        await state.OperationQuiescence;
        state.HasPendingOperation.Should().BeFalse();
        state.HasPresentation.Should().BeFalse();
    }

    private sealed class GatedQuestionStore(IHostInteractionStore inner) : IHostInteractionStore
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<HostInteractionDecision> TransactAsync(HostRequest request,
            Func<HostInteractionSnapshot, HostInteractionCommit> transition, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return await inner.TransactAsync(request, transition, cancellationToken);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingExactRunningTaskControlsCancelPendingClarificationAndRejectLateNativeReply(bool compact)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var transport = new QuestionTransport(false);
            await using var adapter = new BoundedModelTurnAdapter(transport, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance);
            await using var composed = new Composition(f, adapter: adapter);
            using var host = new NativeQuestionHost(f.Store, f.Time, NullLogger<NativeQuestionViewModel>.Instance);
            host.BindGate(static () => true);
            host.BindAdmissionRevision(static () => 1);
            host.BindPrivateRevision(static () => 1);
            var model = composed.Model;
            var presentation = new SessionsPresentationOwner(model);
            var workspace = compact ? null : new SessionsWindow(model, presentation);
            var compactWindow = compact ? new CompactSessionWindow(model, presentation) : null;
            Window owner = workspace ?? (Window)compactWindow!;
            owner.Show();
            var displayed = new TaskCompletionSource<NativeQuestionViewModel>(TaskCreationOptions.RunContinuationsAsynchronously);
            QuestionWindow? questionWindow = null;
            model.QuestionPresenter = async (request, generation, spec, expiry, admitted, token) =>
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try
                    {
                        return await host.AskConversationAsync(request, generation, spec, expiry, admitted, state =>
                        {
                            questionWindow = new(state, new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance));
                            questionWindow.Show(owner);
                            displayed.TrySetResult(state);
                            return Task.CompletedTask;
                        }, token);
                    }
                    finally { questionWindow?.Close(); }
                });
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                model.ComposerBody = "cancel this exact clarification task";
                owner.FindControl<Button>("SubmitConversationWithClarification")!
                    .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                var operation = workspace?.ConversationOperation ?? compactWindow!.ConversationOperation;
                var state = await displayed.Task.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
                await model.RefreshRunningConversationAsync();
                model.CanCancelRunningConversation.Should().BeTrue();
                var cancel = owner.FindControl<Button>("CancelRunningConversation")!;
                cancel.IsEffectivelyEnabled.Should().BeTrue();
                cancel.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await (workspace?.ConversationCancellationOperation ?? compactWindow!.ConversationCancellationOperation);
                model.RunningConversationStatus.Should().NotContain("unconfirmed");
                model.RunningConversationStatus.Should().Match(text =>
                    text.Contains("Cancelling requested", StringComparison.Ordinal)
                    || text.Contains("Terminal Cancelled", StringComparison.Ordinal));
                await operation;
                state.IsEditable.Should().BeFalse();
                state.Edit(new(["option-0"]));
                await state.SubmitAsync();
                var retained = model.ConversationMessages.Single();
                retained.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
                retained.Reason.Should().Be(ModelTurnReason.CallerCancelled);
                retained.ExpectedTaskState.Should().Be(HostTaskState.Unknown);
                retained.AssistantResponse.Should().BeNull();
                transport.Requests.Should().ContainSingle();
                model.RunningConversationStatus.Should().Contain("Terminal Cancelled");
                model.CanCancelRunningConversation.Should().BeFalse();
                composed.Audit.Events.Where(item => string.Equals(item.ActionId, "conversation.cancel-request", StringComparison.Ordinal))
                    .Select(item => item.Outcome).Should().Equal(
                        Kora.Core.Auditing.SecurityAuditOutcome.Requested, Kora.Core.Auditing.SecurityAuditOutcome.Succeeded);
                (await f.Store.ReadConversationMessageAsync(retained.Request.SessionId, retained.Request.RequestId, f.Token))
                    .Should().Be(retained);
                (await f.Store.ReadQuestionsAsync(retained.Request.SessionId, f.Token)).Single().Status.Should().NotBe(QuestionStatus.Answered);
            }
            finally { questionWindow?.Close(); presentation.Revoke(); }
        });
    }

    [Fact]
    public async Task BoundQuestionPresenterDoesNotChangeOrdinarySubmissionToolAdmissionOrRequestShape()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var adapter = new AnswerAdapter();
        await using var composed = new Composition(f, adapter: adapter);
        var model = composed.Model;
        model.QuestionPresenter = (_, _, _, _, _, _) =>
            throw new InvalidOperationException("Ordinary Submit must never request clarification.");
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        model.ComposerBody = "Use the existing admitted read-only tool";
        await model.SubmitConversationAsync();
        adapter.ToolCalls.Should().Be(1);
        model.ConversationMessages.Single().AssistantResponse!.Body.Should().Be("Host version: 1.0.0");
        f.Count("host_questions").Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClarificationControlsFailClosedWithoutComposedPresenterInWorkspaceAndCompactView(bool compact)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            var model = composed.Model;
            var presentation = new SessionsPresentationOwner(model);
            Window window = compact ? new CompactSessionWindow(model, presentation) : new SessionsWindow(model, presentation);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                model.ComposerBody = "Original unsent text";
                var submit = window.FindControl<Button>("SubmitConversationWithClarification")!;
                submit.IsEffectivelyEnabled.Should().BeFalse();
                model.CanSubmitConversation.Should().BeTrue();
                await model.SubmitConversationWithClarificationAsync();
                f.Count("conversation_messages").Should().Be(0);
                model.ComposerBody.Should().Be("Original unsent text");
                model.ConversationStatus.Should().Contain("presenter is unavailable").And.Contain("Nothing was submitted");
            }
            finally { presentation.Revoke(); }
        });
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task ExactNativeClarificationUsesExistingQuestionWindowAndRetainsGenuineAnswerOrExplicitFailure(
        bool text, bool cancel, bool compact)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var transport = new QuestionTransport(text);
            await using var adapter = new BoundedModelTurnAdapter(transport, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance);
            await using var composed = new Composition(f, adapter: adapter);
            using var host = new NativeQuestionHost(f.Store, f.Time, NullLogger<NativeQuestionViewModel>.Instance);
            host.BindGate(static () => true);
            host.BindAdmissionRevision(static () => 1);
            host.BindPrivateRevision(static () => 1);
            var model = composed.Model;
            var presentation = new SessionsPresentationOwner(model);
            var workspace = compact ? null : new SessionsWindow(model, presentation);
            var compactWindow = compact ? new CompactSessionWindow(model, presentation) : null;
            Window owner = workspace ?? (Window)compactWindow!;
            owner.Show();
            var presentations = 0;
            var presentationState = string.Empty;
            model.QuestionPresenter = async (request, generation, spec, expires, admitted, token) =>
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                host.AskConversationAsync(request, generation, spec, expires, admitted, async state =>
                {
                    presentations++;
                    var window = new QuestionWindow(state, new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance));
                    var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    void OnReady(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
                    {
                        if (window.IsActive && state.IsEditable && !state.HasPendingOperation) { ready.TrySetResult(); }
                    }
                    state.PropertyChanged += OnReady;
                    try
                    {
                        window.Show(owner);
                        window.Activate();
                        if (window.IsActive && state.IsEditable && !state.HasPendingOperation) { ready.TrySetResult(); }
                        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                        state.Question.Proposal.Should().BeNull();
                        state.Question.Key.Request.Should().Be(request);
                        state.IsApproval.Should().BeFalse();
                        await state.ReviewAsync();
                        presentationState = state.Status + " editable=" + state.IsEditable + " busy=" + state.HasPendingOperation;
                        if (cancel)
                        {
                            await state.CancelAsync();
                            presentationState += " cancel=" + state.Status;
                            return;
                        }
                        state.Edit(text ? new([], "Fresh exact user answer") : new(["option-1"]));
                        await state.SaveDraftAsync();
                        presentationState += " draft=" + state.Status + " revision=" + state.Key.Revision.Value;
                        state.Completion.IsCompleted.Should().BeFalse("a draft is never a submitted clarification");
                        await state.SubmitAsync();
                        presentationState += " submit=" + state.Status;
                    }
                    finally { state.PropertyChanged -= OnReady; window.Close(); }
                }, token));
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                model.ComposerBody = "An original request that needs clarification";
                owner.FindControl<Avalonia.Controls.Button>("SubmitConversationWithClarification")!
                    .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await (workspace?.ConversationOperation ?? compactWindow!.ConversationOperation);
                presentations.Should().Be(1);
                var retained = model.ConversationMessages.Single();
                retained.Body.Should().Be("An original request that needs clarification");
                retained.Outcome.Should().Be(cancel ? ModelTurnOutcome.Denied : ModelTurnOutcome.Succeeded, presentationState);
                (retained.AssistantResponse?.Body).Should().Be(cancel ? null : "Genuine final response after exact clarification");
                transport.Requests.Should().HaveCount(cancel ? 1 : 2);
                if (!cancel)
                {
                    var answer = transport.Requests[1].Messages[1].Should().BeOfType<ModelQuestionAnswerMessage>().Subject;
                    answer.Answer.Text.Should().Be(text ? "Fresh exact user answer" : null);
                    answer.Channel.Should().Be(RequestOrigin.LocalUi);
                }
                var persisted = await f.Store.ReadConversationMessageAsync(retained.Request.SessionId, retained.Request.RequestId, f.Token);
                persisted.Should().Be(retained);
                var questions = await f.Store.ReadQuestionsAsync(retained.Request.SessionId, f.Token);
                questions.Should().ContainSingle();
                questions[0].Status.Should().Be(cancel ? QuestionStatus.Cancelled : QuestionStatus.Answered, presentationState);
                f.Count("scoped_grants").Should().Be(0);
                model.ComposerBody.Should().BeEmpty();
            }
            finally { presentation.Revoke(); }
        });
    }

    private sealed class QuestionTransport(bool text) : IModelToolTransport
    {
        internal List<ModelToolRequest> Requests { get; } = [];

        public Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.ClarificationAllowed.Should().BeTrue();
            request.Context.ToolMode.Should().Be(ModelToolMode.None);
            Requests.Add(request);
            ModelAssistantMessage message = Requests.Count == 1
                ? new ModelClarificationQuestion(new("Which format?", text ? [] : ["Compact", "Detailed"]))
                : ModelToolProtocol.ParseAssistant("""{"kind":"answer","answer":"Genuine final response after exact clarification"}"""u8);
            return Task.FromResult(new ModelToolTransportReply(ModelTurnOutcome.Succeeded, request.Provenance, message,
                new(ModelToolProtocol.SerializeRequest(request).Length, ModelToolProtocol.SerializeMessage(message).Length)));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

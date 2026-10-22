using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
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

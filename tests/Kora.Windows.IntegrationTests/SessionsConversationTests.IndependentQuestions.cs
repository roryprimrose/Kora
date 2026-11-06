using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

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
    [Theory]
    [InlineData(false, false, false, "none")]
    [InlineData(false, false, true, "none")]
    [InlineData(false, true, false, "none")]
    [InlineData(false, true, true, "none")]
    [InlineData(true, false, false, "none")]
    [InlineData(true, false, true, "none")]
    [InlineData(true, true, false, "none")]
    [InlineData(true, true, true, "none")]
    [InlineData(false, false, false, "control")]
    [InlineData(true, true, false, "privacy")]
    [InlineData(false, true, false, "close")]
    [InlineData(true, false, false, "expiry")]
    public async Task OriginalQuestionSurvivesSessionSwitchAndRetainsOnlyItsOwnGenuineOutcome(
        bool compact, bool readOnly, bool cancel, string retirement)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            f.Time.UseFrozenTimers = true;
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var originalSession = f.Request.SessionId;
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var transport = new QuestionTransport(false, readOnly);
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
            var displayed = new TaskCompletionSource<NativeQuestionViewModel>(TaskCreationOptions.RunContinuationsAsynchronously);
            QuestionWindow? questionWindow = null;
            model.QuestionPresenter = async (request, generation, spec, expiry, admitted, token) =>
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try
                    {
                        return await host.AskConversationAsync(request, generation, spec, expiry, admitted, async state =>
                        {
                            questionWindow = new(state, new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance));
                            questionWindow.Show(owner);
                            await state.OperationQuiescence;
                            displayed.TrySetResult(state);
                        }, token);
                    }
                    finally { questionWindow?.Close(); }
                });
            owner.Show();
            Task? operation = null;
            try
            {
                await model.RefreshAsync();
                var original = model.Sessions.Single(entry => entry.Authority.SessionId == originalSession);
                var other = model.Sessions.Single(entry => entry.Authority.SessionId != originalSession);
                await model.SelectAsync(original);
                await model.ReadConversationAsync();
                model.ComposerBody = "Original request requiring an exact answer";
                owner.FindControl<Button>(readOnly ? "SubmitReadOnlyWithClarification" : "SubmitConversationWithClarification")!
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                operation = workspace?.ConversationOperation ?? compactWindow!.ConversationOperation;
                var state = await displayed.Task.WaitAsync(f.Token);
                var question = state.Question;
                model.CanRead.Should().BeTrue("the dispatched question must not occupy the management UI");
                model.ComposerBody.Should().BeEmpty("the original request was already retained and dispatched");
                model.ComposerBody = "new unsent text in the original conversation";
                await model.SelectAsync(other);
                model.SelectedSessionRecord.Should().Be(other);
                await model.ReadConversationAsync();
                model.ComposerBody = "independent unsent text in the active conversation";
                await state.OperationQuiescence;
                state.Question.Key.Should().Be(question.Key);
                state.Question.Spec.Matches(question.Spec).Should().BeTrue();
                state.Question.ExpiresAt.Should().Be(question.ExpiresAt);
                state.Question.SessionGeneration.Should().Be(question.SessionGeneration);
                state.IsEditable.Should().BeTrue();
                model.CanSubmitConversationWithClarification.Should().BeFalse("the original singleton question is still owned");
                if (!string.Equals(retirement, "none", StringComparison.Ordinal))
                {
                    switch (retirement)
                    {
                        case "control": composed.Access.ControlRevision++; break;
                        case "privacy": composed.Access.CanInspect = false; break;
                        case "close": model.Close(); break;
                        case "expiry": f.Time.Now = question.ExpiresAt; break;
                        default: throw new InvalidOperationException("Unknown test retirement.");
                    }
                    state.RefreshEligibility();
                    state.IsEditable.Should().BeFalse();
                    state.Edit(new(["option-1"]));
                    await state.SubmitAsync();
                    await operation;
                    var originalRecord = (await f.Store.ReadConversationPageAsync(originalSession, null, 25, f.Token)).Messages.Single();
                    originalRecord.AssistantResponse.Should().BeNull();
                    (await f.Store.ReadQuestionsAsync(originalSession, f.Token)).Single().Status.Should().NotBe(QuestionStatus.Answered);
                    transport.Requests.Should().HaveCount(readOnly ? 2 : 1, "retired questions cannot resume the provider");
                    model.ComposerBody.Should().Be(string.Equals(retirement, "expiry", StringComparison.Ordinal)
                        ? "independent unsent text in the active conversation" : string.Empty);
                    f.Count("scoped_grants").Should().Be(0);
                    return;
                }
                await state.ReviewAsync();
                if (cancel) { await state.CancelAsync(); }
                else
                {
                    state.Edit(new(["option-1"]));
                    await state.SaveDraftAsync();
                    state.Completion.IsCompleted.Should().BeFalse();
                    await state.SubmitAsync();
                }
                await operation;
                model.SelectedSessionRecord.Should().Be(other);
                model.ComposerBody.Should().Be("independent unsent text in the active conversation");
                model.ConversationMessages.Should().BeEmpty();
                var retained = (await f.Store.ReadConversationPageAsync(originalSession, null, 25, f.Token)).Messages.Single();
                retained.Request.Should().Be(question.Key.Request);
                retained.Body.Should().Be("Original request requiring an exact answer");
                retained.Outcome.Should().Be(cancel ? ModelTurnOutcome.Denied : ModelTurnOutcome.Succeeded);
                (retained.AssistantResponse?.Body).Should().Be(cancel ? null : "Genuine final response after exact clarification");
                (await f.Store.ReadConversationPageAsync(other.Authority.SessionId, null, 25, f.Token)).Messages.Should().BeEmpty();
                var answered = (await f.Store.ReadQuestionsAsync(originalSession, f.Token)).Single();
                answered.Key.QuestionId.Should().Be(question.Key.QuestionId);
                answered.Status.Should().Be(cancel ? QuestionStatus.Cancelled : QuestionStatus.Answered);
                if (!cancel)
                {
                    var reply = transport.Requests[^1].Messages.OfType<ModelQuestionAnswerMessage>().Single();
                    reply.Answer.Choices.Should().Equal("option-1");
                    reply.Channel.Should().Be(RequestOrigin.LocalUi);
                }
                await model.SelectAsync(original);
                model.ComposerBody.Should().Be("new unsent text in the original conversation");
                await model.ReadConversationAsync();
                model.ConversationMessages.Should().ContainSingle().Which.Should().Be(retained);
                f.Count("conversation_messages").Should().Be(1);
                f.Count("host_questions").Should().Be(1);
                f.Count("scoped_grants").Should().Be(0);
            }
            finally
            {
                if (displayed.Task.IsCompletedSuccessfully) { displayed.Task.Result.Close("Test owner retired."); }
                if (operation is not null) { await operation; }
                presentation.Revoke();
                questionWindow?.Close();
                owner.Close();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClarificationSubmissionWithoutQuestionRetainsItsRealAnswerAndReleasesInput(bool readOnly)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var transport = new ImmediateClarificationAnswer();
            await using var adapter = new BoundedModelTurnAdapter(transport, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance);
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            model.QuestionPresenter = (_, _, _, _, _, _) => throw new InvalidOperationException("No question was requested.");
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            model.ComposerBody = "Original request already has sufficient detail";
            if (readOnly) { await model.SubmitReadOnlyWithClarificationAsync(); }
            else { await model.SubmitConversationWithClarificationAsync(); }
            model.ConversationMessages.Single().AssistantResponse!.Body.Should().Be("Genuine answer without a question");
            model.ComposerBody.Should().BeEmpty();
            model.CanRead.Should().BeTrue();
            model.ComposerBody = "New independent input";
            model.CanSubmitConversationWithClarification.Should().BeTrue();
            f.Count("host_questions").Should().Be(0);
            f.Count("conversation_messages").Should().Be(1);
        });
    }

    private sealed class ImmediateClarificationAnswer : IModelToolTransport
    {
        public Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.ClarificationAllowed.Should().BeTrue();
            var answer = ModelToolProtocol.ParseAssistant("""{"kind":"answer","answer":"Genuine answer without a question"}"""u8);
            return Task.FromResult(new ModelToolTransportReply(ModelTurnOutcome.Succeeded, request.Provenance, answer,
                new(ModelToolProtocol.SerializeRequest(request).Length, ModelToolProtocol.SerializeMessage(answer).Length)));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

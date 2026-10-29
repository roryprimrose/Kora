using System.Reflection;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application;
using Kora.Application.Dependencies;
using Kora.Application.Interaction;
using Kora.Application.Presentation;
using Kora.Application.Voice;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false, false, "valid")]
    [InlineData(true, false, "valid")]
    [InlineData(false, true, "valid")]
    [InlineData(false, false, "voice")]
    [InlineData(false, false, "ledger")]
    [InlineData(false, false, "question")]
    [InlineData(false, false, "expired")]
    [InlineData(false, false, "close")]
    [InlineData(false, false, "private")]
    [InlineData(false, false, "input")]
    [InlineData(false, false, "editor")]
    [InlineData(false, false, "source-close")]
    [InlineData(false, false, "capture")]
    public async Task BrokerNoticeGestureOpensSameCanonicalQuestionAndOnlyRealAnswerResumesOriginalTask(
        bool text, bool cancel, string fault)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            f.Time.UseFrozenTimers = true;
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            using var native = new NativeUxFixtureSession(Path.GetTempPath(), syntheticVoice: true);
            await native.InitializeAsync();
            await using var transport = new QuestionTransport(text);
            await using var adapter = new BoundedModelTurnAdapter(transport, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance);
            await using var composed = new Composition(f, adapter: adapter);
            await using var broker = new LocalEventBroker(
                new AuthorityLocalEventSource(f.Store, composed.Access, native.CreateMaintenance(), new(), f.Time),
                new LocalEventStateStore(f.Paths), composed.Access, f.Time, native.Audit, NullLogger<LocalEventBroker>.Instance);
            var model = new SessionsViewModel(composed.Workspace, native.Evidence, composed.Access,
                NullLogger<SessionsViewModel>.Instance, broker, conversation: composed.Conversation);
            HostQuestionVoiceReplyService? replies = null;
            using var host = new NativeQuestionHost(f.Store, f.Time, NullLogger<NativeQuestionViewModel>.Instance, questions =>
                replies = new(questions, native.Voice, f.Time, new AvaloniaUiDispatcher(),
                    () => new QuestionVoiceAdmission(new("fixture-synthetic-microphone", "Synthetic fixture microphone"), 1, 1, 1),
                    (_, _, _) => Task.CompletedTask, NullLogger<HostQuestionVoiceReplyService>.Instance));
            host.BindPrivateRevision(() => composed.Access.ControlRevision);
            using var controller = new QuestionWindowController(native.Main, host, native.VersionQuery,
                new AssemblyApplicationInfo(), new(NullLogger<NativeDetailRenderer>.Instance),
                () => composed.Access.CanControl, NullLogger<QuestionWindowController>.Instance,
                () => composed.Access.ControlRevision);
            host.BindWorkspace(composed.Workspace);
            var owner = new SessionsWindow(model);
            model.QuestionPresenter = (request, generation, spec, expiry, live, token) =>
                controller.AskConversationAsync(owner, request, generation, spec, expiry, live, token);
            model.ConversationNoticeQuestion = controller.CaptureConversationNoticeQuestion;
            var shown = new TaskCompletionSource<QuestionWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<QuestionWindow>((window, _) => shown.TrySetResult(window));
            owner.Show();
            Task operation = Task.CompletedTask;
            QuestionWindow? questionWindow = null;
            NativeQuestionViewModel? state = null;
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                model.ComposerBody = "Original task needing an exact clarification";
                owner.FindControl<Button>("SubmitConversationWithClarification")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                operation = owner.ConversationOperation;
                await (await Task.WhenAny(shown.Task, operation).WaitAsync(f.Token));
                shown.Task.IsCompletedSuccessfully.Should().BeTrue(model.ConversationStatus);
                questionWindow = await shown.Task;
                // Inspect the existing controller-owned state without adding a production test accessor.
                state = (NativeQuestionViewModel)typeof(QuestionWindowController)
                    .GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
                await NoticeQuestionReady(state, f.Token);
                var canonical = state.Question;
                var request = canonical.Key.Request;
                var selected = model.SelectedSessionRecord;
                owner.Activate();
                model.CanRefreshWork.Should().BeTrue("the original task's wait must not block notice observation");
                await model.RefreshWorkAsync();
                var notice = model.LocalEvents.Single();
                notice.Event.Source.Should().Be(LocalEventSource.ConversationQuestion);
                notice.Event.SubjectId.Should().Be(canonical.Key.QuestionId.Value);
                model.SelectLocalEvent(notice);
                model.CanAnswerConversationNotice.Should().BeFalse("the exact notice must be reviewed first");
                await model.ReviewLocalEventAsync();
                model.Detail.Should().Contain(request.SessionId.Value.ToString("D"))
                    .And.Contain(canonical.Key.QuestionId.Value.ToString("D")).And.NotContain("Which format?");
                model.CanAnswerConversationNotice.Should().BeTrue();
                var tasks = f.Count("host_tasks");
                var questions = f.Count("host_questions");
                var activations = 0;
                questionWindow.Activated += (_, _) => activations++;
                if (fault is not ("valid" or "voice"))
                {
                    model.ConversationNoticeQuestion = target =>
                    {
                        if (fault is "capture") { throw new InvalidOperationException("Synthetic unavailable native capture"); }
                        var captured = controller.CaptureConversationNoticeQuestion(target);
                        return captured is null ? null : new NoticeFocusRace(captured, async () =>
                        {
                            switch (fault)
                            {
                                case "ledger":
                                    await broker.ExecuteNativeAsync(new(LocalEventOperation.Defer, target.Id, target.Revision),
                                        () => true, f.Token);
                                    break;
                                case "question":
                                    state.Edit(new(["option-0"]));
                                    await state.SaveDraftAsync();
                                    break;
                                case "expired": f.Time.Now = canonical.ExpiresAt; break;
                                case "close": questionWindow.Close(); break;
                                case "private": composed.Access.ControlRevision++; composed.Access.CanControl = false; break;
                                case "input": native.Main.CommandText = "independent input"; break;
                                case "editor": native.Main.BindRetainedAnswerReadDecisionGate(() => false); break;
                                case "source-close": owner.ClosePrivate(); break;
                            }
                        });
                    };
                }
                owner.FindControl<Button>("AnswerConversationNotice")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.ConversationNoticeOperation;
                if (fault is not ("valid" or "voice"))
                {
                    model.Status.Should().Contain("Conversation notice unavailable");
                    activations.Should().Be(0, "a refused gesture must not activate any question presenter");
                    transport.Requests.Should().ContainSingle();
                    f.Count("host_tasks").Should().Be(tasks);
                    f.Count("host_questions").Should().Be(questions);
                    (await f.Store.ReadQuestionsAsync(request.SessionId, f.Token)).Should()
                        .NotContain(item => item.Status == QuestionStatus.Answered);
                    return;
                }
                model.Status.Should().Contain("Exact original conversation question opened");
                questionWindow.IsActive.Should().BeTrue();
                state.Question.Should().BeSameAs(canonical);
                model.SelectedSessionRecord.Should().Be(selected);
                state.Completion.IsCompleted.Should().BeFalse();
                operation.IsCompleted.Should().BeFalse();
                transport.Requests.Should().ContainSingle();
                f.Count("host_tasks").Should().Be(tasks);
                f.Count("host_questions").Should().Be(questions);
                f.Count("scoped_grants").Should().Be(0);
                native.Voice.StartCalls.Should().Be(0);
                model.CanAnswerConversationNotice.Should().BeFalse("the gesture is not a reusable answer capability");
                if (cancel) { await state.CancelAsync(); }
                else if (fault is "voice")
                {
                    state.CanAnswerByVoice.Should().BeTrue();
                    await state.AnswerByVoiceAsync();
                    await native.Voice.ReplyStarted.Task.WaitAsync(f.Token);
                    native.Voice.PublishTranscript("Detailed");
                    await replies!.Quiescence;
                }
                else
                {
                    if (text) { questionWindow.FindControl<TextBox>("AnswerInput")!.Text = "Fresh original user answer"; }
                    else { state.Edit(new(["option-1"])); }
                    await state.SubmitAsync();
                }
                await operation;
                var receipt = await state.Completion;
                receipt.Outcome.Should().Be(cancel ? HostInteractionOutcome.Cancelled : HostInteractionOutcome.Answered);
                var stored = (await f.Store.ReadQuestionsAsync(request.SessionId, f.Token)).Single();
                stored.Should().BeEquivalentTo(receipt.Question);
                var message = await f.Store.ReadConversationMessageAsync(request.SessionId, request.RequestId, f.Token);
                message!.Request.Should().Be(request);
                message.Outcome.Should().Be(cancel ? ModelTurnOutcome.Denied : ModelTurnOutcome.Succeeded, model.ConversationStatus);
                (message.AssistantResponse?.Body).Should().Be(cancel ? null : "Genuine final response after exact clarification");
                transport.Requests.Should().HaveCount(cancel ? 1 : 2);
                if (!cancel)
                {
                    var answer = transport.Requests[^1].Messages.OfType<ModelQuestionAnswerMessage>().Single();
                    answer.Question.Question.Prompt.Should().Be(canonical.Spec.Text);
                    answer.Answer.Should().BeEquivalentTo(stored.Draft);
                    answer.Channel.Should().Be(fault is "voice" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
                }
                state.Edit(new(["option-0"]));
                await state.SubmitAsync();
                transport.Requests.Should().HaveCount(cancel ? 1 : 2);
                await state.OperationQuiescence;
                await model.RefreshWorkAsync();
                model.LocalEvents.Should().NotContain(item => item.Event.Source == LocalEventSource.ConversationQuestion);
            }
            finally
            {
                controller.Dispose();
                questionWindow?.Close();
                await operation;
                if (state is not null) { await state.OperationQuiescence; }
                if (replies is not null) { await replies.DisposeAsync(); }
                owner.ClosePrivate();
            }
        });
    }

    private sealed class NoticeFocusRace(IPendingQuestionFocus inner, Func<Task> change) : IPendingQuestionFocus
    {
        public async Task<bool> FocusAsync(Func<bool> isAdmitted)
        {
            await change();
            return await inner.FocusAsync(isAdmitted);
        }
    }

    private static async Task NoticeQuestionReady(NativeQuestionViewModel state, CancellationToken token)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (state.IsEditable && !state.HasPendingOperation) { ready.TrySetResult(); }
        }
        state.PropertyChanged += Changed;
        try { Changed(null, new(null)); await ready.Task.WaitAsync(token); }
        finally { state.PropertyChanged -= Changed; }
    }
}

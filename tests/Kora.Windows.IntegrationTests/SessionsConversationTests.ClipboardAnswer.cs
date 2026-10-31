using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Application.Context;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Tools.Clipboard;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClipboardAnswerContinuesOnlyTheOriginalCanonicalTaskWithIndependentCurrentAdmission(bool revokeAfterDraft)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            f.Time.UseFrozenTimers = true;
            await using var transport = new QuestionTransport(text: true);
            await using var adapter = new BoundedModelTurnAdapter(transport, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance);
            await using var composed = new Composition(f, adapter: adapter);
            await using var admission = new ClipboardExplanationAdmission(f.Store, f.Store, new(f.Tasks));
            using var native = new NativeQuestionHost(f.Store, f.Time, NullLogger<NativeQuestionViewModel>.Instance);
            var eligible = true;
            native.BindGate(() => eligible);
            native.BindAdmissionRevision(static () => 1);
            native.BindPrivateRevision(static () => 1);
            var reader = new QuestionClipboardAnswerTests.Reader();
            var exact = reader.Text;
            var renderer = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance);
            native.BindClipboardAnswers(state => new(state, reader, admission, f.Store, () => () => eligible,
                () => new(1, Kora.Core.Communication.CallState.Clear, false, Kora.Core.Communication.CallAwareSettings.Default),
                static () => 1, static () => 1,
                f.Time, NullLogger<ClipboardSnapshotBroker>.Instance, NullLogger<NativeQuestionViewModel>.Instance, renderer));
            var model = composed.Model;
            var owner = new Window();
            owner.Show();
            Exception? presentationFailure = null;
            model.QuestionPresenter = async (request, generation, spec, expires, admitted, token) =>
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => native.AskConversationAsync(
                    request, generation, spec, expires, admitted, async state =>
                    {
                        var window = new QuestionWindow(state, renderer);
                        window.Show(owner);
                        await QuestionClipboardAnswerTests.Activate(window);
                        try
                        {
                            await QuestionClipboardAnswerTests.WhenEditable(state);
                            var permission = new TaskCompletionSource<QuestionWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
                            using var opened = Window.WindowOpenedEvent.AddClassHandler<QuestionWindow>((next, _) => permission.TrySetResult(next));
                            state.ClipboardAnswer!.CanCapture.Should().BeTrue("the original ordinary question must remain active and current: {0}", state.Status);
                            var capture = state.ClipboardAnswer!.CaptureAsync();
                            await Task.WhenAny(capture, permission.Task);
                            permission.Task.IsCompleted.Should().BeTrue("capture ended before permission review: {0}", state.Status);
                            await QuestionClipboardAnswerTests.DecidePermission(await permission.Task.WaitAsync(token), grant: true);
                            await capture;
                            await QuestionClipboardAnswerTests.Activate(window);
                            await QuestionClipboardAnswerTests.WhenEditable(state);
                            state.Completion.IsCompleted.Should().BeFalse();
                            transport.Requests.Should().ContainSingle();
                            reader.Text = "Replacement clipboard text must not be reread or substituted";
                            await state.ClipboardAnswer.AcceptAsync();
                            state.Answer.Text.Should().Be(exact);
                            state.Completion.IsCompleted.Should().BeFalse();
                            if (revokeAfterDraft) { eligible = false; state.ClipboardAnswer.Refresh(); }
                            await state.SubmitAsync();
                            state.Completion.IsCompleted.Should().BeTrue("Submit must answer or explicitly retire this exact question: {0}", state.Status);
                            await state.OperationQuiescence;
                        }
                        catch (Exception exception) { presentationFailure = exception; throw; }
                        finally { window.Close(); }
                    }, token));
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                model.ComposerBody = "Use one ordinary clarification";
                await model.SubmitConversationWithClarificationAsync();
                presentationFailure.Should().BeNull("native interaction must complete: {0}", presentationFailure);
                model.ConversationMessages.Should().ContainSingle(model.ConversationStatus);
                var retained = model.ConversationMessages.Single();
                var questions = await f.Store.ReadQuestionsAsync(retained.Request.SessionId, f.Token);
                questions.Should().ContainSingle();
                reader.Calls.Should().Be(1);
                if (revokeAfterDraft)
                {
                    retained.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
                    transport.Requests.Should().ContainSingle();
                    questions[0].Status.Should().Be(QuestionStatus.Pending);
                    questions[0].Draft.Should().BeNull();
                }
                else
                {
                    retained.Outcome.Should().Be(ModelTurnOutcome.Succeeded, model.ConversationStatus);
                    retained.AssistantResponse!.Body.Should().Be("Genuine final response after exact clarification");
                    questions[0].Status.Should().Be(QuestionStatus.Answered);
                    questions[0].Key.Revision.Value.Should().Be(2);
                    questions[0].Draft!.Text.Should().Be(exact);
                    questions[0].Key.Request.Should().Be(retained.Request);
                    var reply = transport.Requests[^1].Messages.OfType<ModelQuestionAnswerMessage>().Single();
                    reply.Answer.Text.Should().Be(exact);
                    reply.Channel.Should().Be(RequestOrigin.LocalUi);
                    transport.Requests.Should().HaveCount(2);
                }
            }
            finally { owner.Close(); model.Close(); }
        });
    }
}

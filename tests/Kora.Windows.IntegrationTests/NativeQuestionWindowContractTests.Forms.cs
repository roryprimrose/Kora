using Avalonia.Controls;
using Avalonia.Headless;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.Interaction;
using Kora.Application.Voice;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Windows.Audio;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class NativeQuestionWindowContractTests
{
    private static LocalModelQuestion NativeForm() => ModelToolProtocol.ParseAskUser(
        """{"prompt":"Describe the answer","fields":[{"label":"Audience","type":"text","maximumLength":40,"maximumUtf8Bytes":100},{"label":"Style","type":"singleChoice","options":["Compact","Detailed"]}]}"""u8,
        "ask_1", formsAllowed: true).Question;

    [Fact]
    public async Task NativeMixedFieldEditsReviewExplicitSubmitCanonicalStoreAndExactFakeTaskContinuation()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
            var continued = 0;
            var broker = new ConversationQuestionContinuation(f.Store, async (request, _, spec, expiry, _, token) =>
            {
                var initial = (await f.Questions.CreateAsync(request, spec, expiry, token)).Question!;
                var model = new NativeQuestionViewModel(initial, f.Questions, f.Authorization, new(f.Store, f.Time),
                    static () => true, f.Time, NullLogger<NativeQuestionViewModel>.Instance);
                using var privacy = new ActivatedVoiceRecognitionTests.FakePrivacy();
                var factory = new ActivatedVoiceRecognitionTests.FakeFactory();
                var native = new ActivatedVoiceRecognitionTests.FakeCapture();
                factory.OpenResult.SetResult(native);
                await using var capture = new WindowsVoiceRecognitionService(NullLogger<WindowsVoiceRecognitionService>.Instance,
                    privacy, factory, VoiceCaptureLimits.Default);
                await using var replies = new HostQuestionVoiceReplyService(f.Questions, capture, new ReplyUiTime { Now = f.Time.Now },
                    new ReplyUiDispatcher(), () => new QuestionVoiceAdmission(new("mic", "Synthetic"), 1, 1, 1),
                    (_, _, _) => Task.CompletedTask, NullLogger<HostQuestionVoiceReplyService>.Instance);
                model.BindVoiceReplies(replies, static () => true);
                var window = CreateWindow(model);
                try
                {
                    await ShowAsync(window, model);
                    var panel = window.FindControl<StackPanel>("ChoicePanel")!;
                    var input = panel.Children.OfType<TextBox>().Single();
                    var radios = panel.Children.OfType<RadioButton>().ToArray();
                    radios.Should().OnlyContain(radio => radio.IsChecked != true, "there is no affirmative preselection");
                    model.CanSubmit.Should().BeFalse();
                    input.Text = "Engineers";
                    var retired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    void Changed(object? sender, EventArgs args)
                    {
                        if (!replies.IsActive) { retired.TrySetResult(); }
                    }
                    await model.AnswerFieldByVoiceAsync("field-1");
                    replies.Changed += Changed;
                    try
                    {
                        native.Transcript("option two");
                        await retired.Task.WaitAsync(token);
                        await replies.Quiescence;
                    }
                    finally { replies.Changed -= Changed; }
                    radios[1].IsChecked.Should().BeTrue();
                    input.Text.Should().Be("Engineers", "publishing one field must not clobber another field's edit");
                    input.Text = "Reviewers";
                    model.Completion.IsCompleted.Should().BeFalse();
                    continued.Should().Be(0);
                    var pending = (await f.Store.ReadQuestionsAsync(request.SessionId, token)).Single();
                    pending.Key.Should().Be(initial.Key);
                    pending.Draft.Should().BeNull("neither native nor voice edits autosave to disk");
                    model.CanSaveDraft.Should().BeFalse();
                    await model.ReviewAsync();
                    model.ReviewText.Should().Contain("Reviewers").And.Contain("option-1");
                    input.Text = "Readers";
                    model.ReviewText.Should().BeEmpty("a new draft invalidates its old review");
                    await model.ReviewAsync();
                    await model.SubmitAsync();
                    await model.SubmitAsync();
                    var result = await model.Completion;
                    result.Question!.Draft!.Fields.Should().Equal(new QuestionFieldAnswer("field-0", "Readers"), new("field-1", Choice: "option-1"));
                    result.Question.Key.Should().Be(initial.Key.Next());
                    return new(initial, result);
                }
                finally { window.Close(); }
            }, f.Request, new(1), f.Time.Now.AddSeconds(15), static () => true, f.Time);
            var reply = await broker.AskAsync(NativeForm(), f.Token);
            continued++;
            continued.Should().Be(1);
            reply.Question.Draft!.Fields[0].Text.Should().Be("Readers");
            f.Reopen();
            await f.Store.InitializeAsync(f.Token);
            var stored = (await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token)).Single();
            stored.Spec.Matches(reply.Question.Spec).Should().BeTrue();
            stored.Draft!.Fields.Should().Equal(reply.Question.Draft.Fields);
            stored.Status.Should().Be(QuestionStatus.Answered);
            f.Count("scoped_grants").Should().Be(0);
        });
    }

    [Theory]
    [InlineData("dismiss")]
    [InlineData("cancel")]
    [InlineData("privacy")]
    [InlineData("expiry")]
    public async Task NativeFormClosureNeverStoresPartialDraftOrAnswers(string closure)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            var eligible = true;
            var spec = LocalModelQuestionPolicy.CreateSpec(NativeForm());
            var question = (await f.RunAsync(() => f.Questions.CreateAsync(f.Request, spec, f.Time.Now.AddSeconds(15), f.Token))).Question!;
            var model = new NativeQuestionViewModel(question, f.Questions, f.Authorization, new(f.Store, f.Time),
                () => eligible, f.Time, NullLogger<NativeQuestionViewModel>.Instance);
            var window = CreateWindow(model);
            try
            {
                await ShowAsync(window, model);
                var panel = window.FindControl<StackPanel>("ChoicePanel")!;
                panel.Children.OfType<TextBox>().Single().Text = "Unsubmitted";
                if (closure is "cancel") { await model.CancelAsync(); }
                if (closure is "dismiss") { window.Close(); }
                if (closure is "privacy") { eligible = false; model.RefreshEligibility(); }
                if (closure is "expiry") { f.Time.Now = question.ExpiresAt; model.RefreshEligibility(); }
                await model.SubmitAsync();
                var stored = (await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token)).Single();
                stored.Status.Should().NotBe(QuestionStatus.Answered);
                stored.Draft.Should().BeNull();
                model.Answer.Fields.Should().BeEmpty();
            }
            finally { window.Close(); }
        });
    }
}

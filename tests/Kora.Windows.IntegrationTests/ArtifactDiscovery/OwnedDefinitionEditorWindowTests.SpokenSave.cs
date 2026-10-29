using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application;
using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Hosting;
using Kora.Application.Interaction;
using Kora.Application.Voice;
using Kora.Core.Artifacts;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Definitions.Artifacts;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class OwnedDefinitionEditorWindowTests
{
    [Theory]
    [InlineData("save")]
    [InlineData("cancel")]
    [InlineData("ambiguous")]
    [InlineData("draft")]
    [InlineData("caret")]
    [InlineData("input")]
    [InlineData("owner")]
    [InlineData("privacy")]
    [InlineData("native")]
    [InlineData("late")]
    [InlineData("capture-owner")]
    [InlineData("call-active")]
    [InlineData("call-suspected")]
    [InlineData("call-unknown")]
    [InlineData("microphone")]
    [InlineData("control")]
    [InlineData("consent")]
    [InlineData("disk")]
    [InlineData("two-hosts")]
    [InlineData("late-question")]
    [InlineData("expiry")]
    [InlineData("risk")]
    [InlineData("ambiguous-target")]
    [InlineData("dictated-save")]
    [InlineData("dictation-review-save")]
    [InlineData("question-excludes-dictation")]
    public async Task CompletePreparedReviewUsesCanonicalQuestionAndSameInstructionWriter(string scenario)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent, syntheticVoice: true);
            var call = new SaveCallState();
            await f.InitializeAsync(null, callState: call);
            f.Main.BindVoiceOwnershipGate(static () => true);
            await f.Main.SetVoiceConsentAsync(true);
            var dictation = f.Main.BindComposerDictation(NullLogger<ComposerDictationService>.Instance);
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var store = new WindowsOwnedInstructionStore(f);
            var revisions = new InstructionRevisionCatalogue(f);
            using var catalogue = EmbeddedArtifactCatalogue.Load();
            var actions = new OwnedInstructionEditorActions(store, admission, revisions, catalogue, f.Audit, new(new()));
            var directories = new LocalArtifactDirectoryPreferences(f);
            using var reload = new ArtifactCatalogueReloadService(catalogue, f, directories,
                new ArtifactDirectoryConfigurationService(directories, f.Audit),
                new McpServerConfigurationService(f, directories, Path.Combine(f.LocalRoot, "no-profile")),
                NullLogger<ArtifactCatalogueReloadService>.Instance, revisions, store, Path.Combine(f.LocalRoot, "no-profile"));
            const string initial = "---\r\nkora-format: 1\r\nname: example\r\ndescription: Exact reviewed metadata\r\n---\r\nOriginal body.\r\n";
            var original = (await store.Write("example", null, ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(initial, true),
                ArtifactKind.Instruction, "kora-profile", "example"), () => true, f.Token)).Current!;
            reload.ReconcileSavedCopy(original).Should().BeTrue();
            await actions.Activate(original, _ => Task.FromResult(true), () => true, f.Token);
            var ownership = 1L;
            var time = new SaveQuestionTime();
            HostQuestionVoiceReplyService? replies = null;
            using var host = new NativeQuestionHost(f.Interactions, time, NullLogger<NativeQuestionViewModel>.Instance,
                questions => replies = f.Main.BindQuestionVoiceReplies(questions, time,
                    NullLogger<HostQuestionVoiceReplyService>.Instance));
            host.BindPrivateRevision(() => f.Access.ControlRevision);
            using var questions = new QuestionWindowController(f.Main, host, f.VersionQuery, new AssemblyApplicationInfo(),
                new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance), () => true,
                NullLogger<QuestionWindowController>.Instance, () => ownership);
            using var controller = new OwnedInstructionEditorController(f.Main, actions, new(actions), reload, f,
                () => true, () => ownership, NullLogger<OwnedInstructionEditorController>.Instance,
                saveQuestions: questions, dictation: dictation);
            OwnedInstructionEditorWindow? editor = null;
            var shown = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var windows = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window is OwnedInstructionEditorWindow owned) { editor = owned; }
                else { shown.TrySetResult(window); }
            });
            controller.Bind();
            async Task Run(string command) { f.Main.CommandText = command; await f.Main.RunTypedCommand.ExecuteAsync(); }
            async Task<Window> Next(Task operation)
            {
                (await Task.WhenAny(operation, shown.Task)).Should().BeSameAs(shown.Task,
                    "the operation must reach exact review: {0}", string.Join("\n",
                        editor!.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text)));
                var result = await shown.Task;
                shown = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return result;
            }
            static void Click(Window window, string caption) => window.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, caption)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Run("instructions list");
            await Run("instructions inspect example");
            var owned = editor!;
            var draft = initial.Replace("Original body.", "Complete replacement body.\r\nSecond exact line.", StringComparison.Ordinal);
            if (scenario is "risk") { draft += "password=not-for-speech"; }
            if (scenario is "ambiguous-target") { draft = draft.Replace("name: example", "name: Example", StringComparison.Ordinal); }
            owned.Source.Text = draft;
            Action? lateLiteral = null;
            if (scenario is "dictated-save" or "dictation-review-save")
            {
                owned.Activate();
                Dispatcher.UIThread.RunJobs();
                owned.Source.CaretIndex = draft.Length;
                owned.Source.SelectionStart = owned.Source.SelectionEnd = draft.Length;
                var hold = owned.GetVisualDescendants().OfType<Button>().Single(button => button.Name is "DictateInstructionBody");
                hold.Focus().Should().BeTrue();
                owned.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                await owned.BodyDictation!.Operation;
                dictation.IsActive.Should().BeTrue();
                lateLiteral = f.Voice.QueueLiteralTranscript("Save instruction example");
                lateLiteral();
                owned.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                await owned.BodyDictation.Operation;
                dictation.HasReview.Should().BeTrue();
                owned.Source.Text.Should().Be(draft);
                (await store.Read("example", f.Token))!.Document.Text.Should().Be(initial);
                f.Voice.ReplyCalls.Should().Be(0, "literal approval words are only body data");
                if (scenario is "dictated-save")
                {
                    owned.GetVisualDescendants().OfType<Button>().Single(button => button.Name is "AcceptInstructionDictation")
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await owned.BodyDictation.Operation;
                    draft += "Save instruction example";
                    owned.Source.Text.Should().Be(draft);
                }
            }
            var saving = Run("instructions save");
            var completeReview = await Next(saving);
            dictation.IsActive.Should().BeFalse();
            f.Voice.IsCaptureQuiescent.Should().BeTrue();
            lateLiteral?.Invoke();
            owned.Source.Text.Should().Be(draft, "a retired literal capture cannot answer a Save review or change its draft");
            completeReview.GetVisualDescendants().OfType<TextBox>().Select(box => box.Text).Should().Contain(draft);
            f.Voice.ReplyCalls.Should().Be(0);
            Click(completeReview, "Reviewed complete source: exact save question");
            if (scenario is "risk" or "ambiguous-target")
            {
                await saving.WaitAsync(f.Token);
                (await store.Read("example", f.Token))!.Document.Text.Should().Be(initial);
                f.Voice.ReplyCalls.Should().Be(0);
                owned.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text)
                    .Should().Contain(text => text != null && text.Contains("unambiguous canonical native owned instruction", StringComparison.Ordinal));
                f.Main.CloseForObservedPrivacyEvent("Synthetic refused save cleanup", hidePresentation: true);
                await f.Main.PrivacyClosureTask;
                return;
            }
            var question = (QuestionWindow)await Next(saving);
            var answerButton = question.FindControl<Button>("AnswerByVoice")!;
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Ready(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
            { if (answerButton.IsEnabled) { ready.TrySetResult(); } }
            answerButton.PropertyChanged += Ready;
            try
            {
                if (answerButton.IsEnabled) { ready.TrySetResult(); }
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
            }
            finally { answerButton.PropertyChanged -= Ready; }
            question.FindControl<TextBlock>("PromptLabel")!.Text.Should().Contain("ALL displayed metadata, body and attribution")
                .And.Contain("Kora roaming Instructions").And.Contain("Save instruction example");
            f.Voice.ReplyCalls.Should().Be(0, "showing approval must not start capture");
            var records = new List<HostQuestionRecord>();
            foreach (var session in (await f.Tasks.ReadIncompleteAsync(100, f.Token)).Select(task => task.Request.SessionId).Distinct())
            { records.AddRange(await f.Interactions.ReadQuestionsAsync(session, f.Token)); }
            var record = records.Single(item => string.Equals(item.Spec.Purpose, "owned-instruction-save", StringComparison.Ordinal));
            record.Key.Request.Origin.Should().Be(RequestOrigin.LocalUi);
            record.Proposal.Should().BeNull("saving is not a reusable grant");
            var expectedText = initial;
            if (scenario is "two-hosts")
            {
                var otherTasks = new WindowsSqliteHostTaskStore(f);
                await otherTasks.InitializeAsync(f.Token);
                var other = new WindowsSqliteHostInteractionStore(f, otherTasks, time);
                await other.InitializeAsync(f.Token);
                using var foreign = HostActivity.BeginRoot(record.Key.Request, HostActivityLayer.Desktop, HostOperation.Request);
                var denied = await new HostQuestionService(other, time).SubmitReplyAsync(record.Key, record.SessionGeneration,
                    new(["save"]), () => true, f.Token);
                denied.Outcome.Should().NotBe(HostInteractionOutcome.Answered);
                question.Close();
            }
            else if (scenario is "native")
            {
                question.GetVisualDescendants().OfType<RadioButton>().Single(button => Equals(button.Content, "Save instruction example")).IsChecked = true;
                question.FindControl<Button>("SubmitAnswer")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            else if (scenario is "late")
            {
                question.Close();
                f.Voice.PublishTranscript("Save instruction example");
            }
            else if (scenario is "capture-owner")
            {
                await f.Voice.BeginPushToTalkAsync(FixtureBoundaries.FixtureVoice.SyntheticMicrophone, [], cancellationToken: f.Token);
                question.FindControl<Button>("AnswerByVoice")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                f.Voice.ReplyCalls.Should().Be(0);
                await f.Voice.StopAsync(f.Token);
                question.Close();
            }
            else
            {
                question.FindControl<Button>("AnswerByVoice")!.IsEnabled.Should().BeTrue();
                question.FindControl<Button>("AnswerByVoice")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await f.Voice.ReplyStarted.Task.WaitAsync(f.Token);
                if (scenario is "question-excludes-dictation")
                {
                    var captureStarts = f.Voice.StartCalls;
                    var hold = owned.GetVisualDescendants().OfType<Button>().Single(button => button.Name is "DictateInstructionBody");
                    hold.RaiseEvent(new KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Key.Space });
                    var attempt = () => owned.BodyDictation!.Operation;
                    await attempt.Should().ThrowAsync<InvalidOperationException>()
                        .WithMessage("Select one supported canonical Kora-owned Instruction draft*");
                    dictation.IsActive.Should().BeFalse();
                    f.Voice.StartCalls.Should().Be(captureStarts, "the dedicated question cannot fall back to body capture");
                    f.Voice.ReplyCalls.Should().Be(1);
                }
                switch (scenario)
                {
                    case "draft": owned.Source.Text += "Changed draft."; break;
                    case "caret": owned.Source.CaretIndex = owned.Source.CaretIndex == 0 ? 1 : 0; break;
                    case "input": f.Main.CommandText = "different command"; break;
                    case "owner": ownership++; break;
                    case "privacy": f.Main.CloseForObservedPrivacyEvent("Synthetic save privacy closure", hidePresentation: true); break;
                    case "call-active": call.Set(CallState.Active); break;
                    case "call-suspected": call.Set(CallState.Suspected); break;
                    case "call-unknown": call.Set(CallState.Unknown); break;
                    case "microphone": f.Main.SelectedMicrophone = new("other", "Unavailable synthetic endpoint"); break;
                    case "control": f.Access.SetOpen(false); break;
                    case "consent": await f.Main.SetVoiceConsentAsync(false); break;
                    case "expiry": time.Now = time.Now.AddMinutes(3); break;
                    case "disk":
                        expectedText = initial.Replace("Original body.", "External version.", StringComparison.Ordinal);
                        await store.Write("example", original, ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(expectedText, true),
                            ArtifactKind.Instruction, "kora-profile", "example"), () => true, f.Token);
                        break;
                    case "late-question":
                        using (var revision = HostActivity.BeginRoot(record.Key.Request, HostActivityLayer.Desktop, HostOperation.Request))
                        {
                            await new HostQuestionService(f.Interactions, time).ReviseAsync(record.Key, record.Spec,
                                record.ExpiresAt, f.Token);
                        }
                        break;
                }
                f.Voice.PublishTranscript(scenario is "cancel" ? "Cancel saving instruction example"
                    : scenario is "ambiguous" ? "yes" : "Save instruction example");
                await replies!.Quiescence;
                if (scenario is "disk")
                {
                    var conflict = await Next(saving);
                    conflict.Title.Should().Be("Instruction conflict");
                    Click(conflict, "Cancel (keep draft)");
                }
                else if (scenario is not ("save" or "cancel" or "dictated-save" or "dictation-review-save" or "question-excludes-dictation")) { question.Close(); }
            }
            await saving.WaitAsync(f.Token);
            var current = await store.Read("example", f.Token);
            var saved = scenario is "save" or "native" or "dictated-save" or "dictation-review-save" or "question-excludes-dictation";
            current!.Document.Text.Should().Be(saved ? draft : expectedText);
            if (saved)
            {
                actions.IsReviewedUseEnabled(current).Should().BeFalse();
                owned.Source.Text.Should().Be(draft);
                var answer = (await f.Interactions.ReadQuestionsAsync(record.Key.Request.SessionId, f.Token)).Single();
                answer.Status.Should().Be(QuestionStatus.Answered);
                answer.AnswerChannel.Should().Be(scenario is "native" ? RequestOrigin.LocalUi : RequestOrigin.ActivatedVoice);
                answer.Key.Request.Should().Be(record.Key.Request);
                f.Voice.PublishTranscript("Save instruction example");
                (await store.Read("example", f.Token))!.Revision.Should().Be(current.Revision);
            }
            if (replies is not null) { await replies.Quiescence; }
            f.Voice.IsCaptureQuiescent.Should().BeTrue();
            f.Main.CloseForObservedPrivacyEvent("Synthetic test cleanup", hidePresentation: true);
            await f.Main.PrivacyClosureTask;
        });
    }

    private sealed class SaveCallState : ICallStateService
    {
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
        public CallState CurrentState { get; private set; } = CallState.Clear;
        internal void Set(CallState value) { CurrentState = value; StateChanged?.Invoke(this, new(value)); }
    }

    private sealed class SaveQuestionTime : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}

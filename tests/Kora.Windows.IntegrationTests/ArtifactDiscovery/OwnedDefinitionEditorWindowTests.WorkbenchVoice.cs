using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application;
using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Interaction;
using Kora.Application.Voice;
using Kora.Core.Artifacts;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Definitions.Artifacts;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class OwnedDefinitionEditorWindowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostedCompleteInstructionReviewUsesInlineQuestionAndOriginalAnswerChannel(bool voice)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent, syntheticVoice: true);
            await f.InitializeAsync();
            f.Main.BindVoiceOwnershipGate(static () => true);
            await f.Main.SetVoiceConsentAsync(true);
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
                ArtifactKind.Instruction, "kora-profile", "example"), static () => true, f.Token)).Current!;
            reload.ReconcileSavedCopy(original).Should().BeTrue();
            var time = new SaveQuestionTime();
            HostQuestionVoiceReplyService? replies = null;
            using var host = new NativeQuestionHost(f.Interactions, time, NullLogger<NativeQuestionViewModel>.Instance,
                questions => replies = f.Main.BindQuestionVoiceReplies(questions, time, NullLogger<HostQuestionVoiceReplyService>.Instance));
            host.BindPrivateRevision(() => f.Access.ControlRevision);
            using var questions = new QuestionWindowController(f.Main, host, f.VersionQuery, new AssemblyApplicationInfo(),
                new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance), static () => true,
                NullLogger<QuestionWindowController>.Instance, static () => 1);
            using var controller = new OwnedInstructionEditorController(f.Main, actions, new(actions), reload, f,
                static () => true, static () => 1, NullLogger<OwnedInstructionEditorController>.Instance, saveQuestions: questions);
            using var responses = new ResponseWindowController(f.Main, NullLogger<ResponseWindowController>.Instance);
            controller.BindWorkbench(responses.PrepareWorkbenchOwner,
                view => responses.ShowHostedView(WorkbenchActivity.InstructionEditor, view));
            controller.Bind();
            var windows = new List<Window>();
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => windows.Add(window));
            async Task Run(string command)
            {
                f.Main.CommandText = command;
                await f.Main.RunTypedCommand.ExecuteAsync();
            }
            await Run("instructions list");
            await Run("instructions inspect example");
            var view = controller.View!;
            var shell = responses.PrepareWorkbenchOwner();
            var draft = initial.Replace("Original body.", "Complete replacement body.\r\nSecond exact line.", StringComparison.Ordinal);
            view.Source.Text = draft;
            var saving = Run("instructions save");
            var review = await FindHostedInstructionDecision(shell, "Reviewed complete source: exact save question", saving);
            shell.FindControl<ContentControl>("NativeReviewContent")!.GetVisualDescendants().OfType<TextBox>()
                .Select(source => source.Text).Should().Equal(initial, draft);
            review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var question = await FindHostedSaveQuestion(shell, saving, f.Token);
            question.FindControl<TextBlock>("PromptLabel")!.Text.Should().Contain("ALL displayed metadata, body and attribution");
            (await store.Read("example", f.Token))!.Document.Text.Should().Be(initial);
            f.Voice.ReplyCalls.Should().Be(0);
            var records = new List<HostQuestionRecord>();
            foreach (var session in (await f.Tasks.ReadIncompleteAsync(100, f.Token)).Select(task => task.Request.SessionId).Distinct())
            { records.AddRange(await f.Interactions.ReadQuestionsAsync(session, f.Token)); }
            var record = records.Single(item => item.Spec.Purpose is "owned-instruction-save");
            record.Key.Request.Origin.Should().Be(RequestOrigin.LocalUi);
            if (voice)
            {
                var button = question.FindControl<Button>("AnswerByVoice")!;
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Ready(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
                { if (button.IsEnabled) { ready.TrySetResult(); } }
                button.PropertyChanged += Ready;
                try
                {
                    if (button.IsEnabled) { ready.TrySetResult(); }
                    await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
                }
                finally { button.PropertyChanged -= Ready; }
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await f.Voice.ReplyStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
                f.Voice.PublishTranscript("Save instruction example");
                await replies!.Quiescence;
            }
            else
            {
                question.GetVisualDescendants().OfType<RadioButton>().Single(button => Equals(button.Content, "Save instruction example")).IsChecked = true;
                question.FindControl<Button>("SubmitAnswer")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            await saving.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
            var current = (await store.Read("example", f.Token))!;
            current.Document.Text.Should().Be(draft);
            actions.IsReviewedUseEnabled(current).Should().BeFalse();
            var answer = (await f.Interactions.ReadQuestionsAsync(record.Key.Request.SessionId, f.Token)).Single();
            answer.Status.Should().Be(QuestionStatus.Answered);
            answer.AnswerChannel.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
            answer.Key.Request.Should().Be(record.Key.Request);
            windows.Distinct().Should().Equal(shell);
            f.Voice.IsCaptureQuiescent.Should().BeTrue();
            f.Main.CloseForObservedPrivacyEvent("Synthetic hosted instruction cleanup", hidePresentation: true);
            await f.Main.PrivacyClosureTask;
        });
    }

    private static async Task<QuestionView> FindHostedSaveQuestion(ResponseWindow shell, Task operation, CancellationToken token)
    {
        var ready = new TaskCompletionSource<QuestionView>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check(object? sender, EventArgs args)
        {
            var question = shell.GetVisualDescendants().OfType<QuestionView>().SingleOrDefault();
            if (question is not null) { ready.TrySetResult(question); }
        }
        shell.LayoutUpdated += Check;
        try
        {
            shell.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Check(null, EventArgs.Empty);
            var next = await Task.WhenAny(ready.Task, operation).WaitAsync(TimeSpan.FromSeconds(10), token);
            if (ReferenceEquals(next, operation)) { throw new InvalidOperationException("The exact save ended before its inline question."); }
            return await ready.Task;
        }
        finally { shell.LayoutUpdated -= Check; }
    }
}

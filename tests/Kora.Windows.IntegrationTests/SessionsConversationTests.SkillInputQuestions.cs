using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AwesomeAssertions;
using Kora.Application.Skills;
using Kora.Core.Skills;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Skills;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("complete")]
    [InlineData("cancel")]
    [InlineData("rewrite")]
    [InlineData("request")]
    [InlineData("target")]
    [InlineData("close")]
    [InlineData("source")]
    [InlineData("duplicate")]
    public async Task OwnedSkillMissingInputsUseExactNativeQuestionsBeforeSeparateCanonicalUse(string scenario)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            f.Time.UseFrozenTimers = true;
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new HistoryAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent);
            await native.InitializeAsync();
            var store = new WindowsOwnedDeclarativeSkillStore(native);
            var document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description",
                "Compare ${input:topic} and ${input:other}; ${input:topic}.",
                guidanceFormat: OwnedSkillGuidanceFormat.RequiredLiteralInputs);
            await store.Publish(document, static () => true, commit => Task.FromResult(commit()), native.Token);
            await using var admission = new OwnedSkillControlAdmission(native.Interactions, native.Interactions, new(native.Tasks));
            var workflow = new OwnedSkillInvocationWorkflow(store, admission, composed.Access, f.Store, composed.Workspace,
                composed.Conversation, native.Audit, f.Time);
            using var host = new NativeQuestionHost(f.Store, f.Time, NullLogger<NativeQuestionViewModel>.Instance);
            using var questions = new QuestionWindowController(native.Main, host, native.VersionQuery, new HostBoundary(),
                new(NullLogger<NativeDetailRenderer>.Instance), static () => true,
                NullLogger<QuestionWindowController>.Instance, static () => 1);
            await using var use = new OwnedSkillInvocationController(native.Main, workflow, static () => true, static () => 1,
                NullLogger<OwnedSkillInvocationController>.Instance, questions: questions);
            use.Bind();
            await using var editor = new OwnedSkillEditorController(native.Main, new(store, admission, native.Audit), admission,
                static () => true, static () => 1, NullLogger<OwnedSkillEditorController>.Instance, use);
            editor.Bind();
            OwnedSkillInvocationWindow? invoking = null;
            using var useOpened = Window.WindowOpenedEvent.AddClassHandler<OwnedSkillInvocationWindow>((window, _) => invoking = window);
            var first = new TaskCompletionSource<QuestionWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            var second = new TaskCompletionSource<QuestionWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            var count = 0;
            using var questionOpened = Window.WindowOpenedEvent.AddClassHandler<QuestionWindow>((window, _) =>
            {
                if (++count == 1) { first.SetResult(window); }
                else { second.SetResult(window); }
            });
            async Task Command(string command)
            { native.Main.CommandText = command; await native.Main.RunTypedCommand.ExecuteAsync(); }
            await Command("owned skill use example 1.0.0");
            var opened = invoking!;
            await Command("owned skill enable");
            await Command("owned skill targets");
            opened.Targets.SelectedIndex = 0;
            await Command("owned skill request: Original unchanged");
            var preparing = Command("owned skill review-use");
            var question = await first.Task.WaitAsync(TimeSpan.FromSeconds(20), native.Token);
            question.FindControl<TextBlock>("PromptLabel")!.Text.Should().Contain("topic");
            opened.Confirm.IsEnabled.Should().BeFalse();
            adapter.Calls.Should().Be(0);
            f.Count("conversation_messages").Should().Be(0);
            if (scenario is "complete")
            {
                await AnswerSkillQuestion(question, "  literal ${input:other} e\u0301  ", native.Token);
                var next = await second.Task.WaitAsync(TimeSpan.FromSeconds(20), native.Token);
                next.FindControl<TextBlock>("PromptLabel")!.Text.Should().Contain("other");
                await AnswerSkillQuestion(next, "second literal", native.Token);
            }
            else if (scenario is "cancel")
            { question.FindControl<Button>("CancelQuestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            else if (scenario is "rewrite")
            { opened.LiteralInputs.Inputs["other"].Text = "unreviewed rewrite"; }
            else if (scenario is "request")
            { opened.Request.Text = "Different unreviewed task"; }
            else if (scenario is "target")
            { opened.Targets.SelectedIndex = -1; }
            else if (scenario is "close")
            { opened.Close(); }
            else if (scenario is "source")
            {
                await File.WriteAllTextAsync(Path.Combine(store.Destination(document), "instructions.md"),
                    "changed source", native.Token);
                await AnswerSkillQuestion(question, "late answer", native.Token);
            }
            else
            {
                await questions.Invoking(controller => controller.AskSkillInputAsync(opened, null!, null!,
                    static () => true, native.Token)).Should().ThrowAsync<InvalidOperationException>();
                question.Close();
            }
            await preparing.WaitAsync(TimeSpan.FromSeconds(20), native.Token);
            if (scenario is "complete")
            {
                opened.Confirm.IsEnabled.Should().BeTrue();
                opened.LiteralInputs.Inputs["topic"].Text.Should().Be("  literal ${input:other} e\u0301  ");
                using var preview = JsonDocument.Parse(opened.Confirmation.Text![opened.Confirmation.Text!.IndexOf('{')..]);
                preview.RootElement.GetProperty("UserRequest").GetString().Should().Be("Original unchanged");
                var binding = preview.RootElement.GetProperty("Evidence")[2].GetProperty("Content").GetString()!;
                binding.Should().Contain("${input:other}").And.Contain("second literal");
                adapter.Calls.Should().Be(0);
                foreach (var session in (await native.Interactions.ReadSessionsAsync(null, 50, native.Token)).Records)
                {
                    (await native.Interactions.ReadQuestionsAsync(session.SessionId, native.Token))
                        .Should().NotContain(record => string.Equals(record.Spec.Purpose, OwnedSkillInputQuestions.Purpose, StringComparison.Ordinal));
                }
                opened.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await use.ActiveOperation;
                adapter.Calls.Should().Be(1);
                var retained = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
                retained.Body.Should().Be("Original unchanged");
                retained.AssistantResponse!.Body.Should().Be("earlier ASSISTANT");
            }
            else
            {
                opened.Confirm.IsEnabled.Should().BeFalse();
                adapter.Calls.Should().Be(0);
                f.Count("conversation_messages").Should().Be(0);
            }
            f.Count("scoped_grants").Should().Be(0);
            await Command("owned skill close-use");
            (await editor.PrepareClose()).Should().BeTrue();
        });
    }

    private static async Task AnswerSkillQuestion(QuestionWindow window, string value, CancellationToken token)
    {
        var button = window.FindControl<Button>("SubmitAnswer")!;
        window.FindControl<TextBox>("AnswerInput")!.Text = value;
        await WaitForSkillButton(button, token);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static async Task WaitForSkillButton(Button button, CancellationToken token)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
        { if (button.IsEnabled) { ready.TrySetResult(); } }
        button.PropertyChanged += Changed;
        try
        {
            if (button.IsEnabled) { ready.TrySetResult(); }
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), token);
        }
        finally { button.PropertyChanged -= Changed; }
    }
}

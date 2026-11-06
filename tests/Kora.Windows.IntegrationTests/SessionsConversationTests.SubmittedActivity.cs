using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeSubmittedActivityObservesTwoConversationsAndCompletesOrCancelsReviewedStopWithoutRetargeting(bool confirm)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            TestContext.Current.TestOutputHelper!.WriteLine("Initial durable source ready.");
            var originalSession = f.Request.SessionId;
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new PendingTurnAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            try
            {
                var firstTarget = await f.Store.ReadMetadataAsync(originalSession, f.Token);
                var secondTarget = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
                TestContext.Current.TestOutputHelper!.WriteLine("Second conversation created.");
                var seed = await composed.Conversation.SubmitQueuedAsync(secondTarget, "establish independent failure hold", () => true, f.Token);
                var seedInvocation = await adapter.NextAsync().WaitAsync(TimeSpan.FromSeconds(30), f.Token);
                seedInvocation.Completion.SetResult(new(ModelTurnOutcome.Denied, seedInvocation.Provenance.Selection, null));
                await seed.Completion;
                TestContext.Current.TestOutputHelper!.WriteLine("Independent failure hold established.");
                var first = await composed.Conversation.SubmitQueuedAsync(firstTarget, "private running original", () => true, f.Token);
                var invocation = await adapter.NextAsync().WaitAsync(TimeSpan.FromSeconds(30), f.Token);
                var pending = await composed.Conversation.SubmitQueuedAsync(firstTarget, "private pending original", () => true, f.Token);
                var other = await composed.Conversation.SubmitQueuedAsync(secondTarget, "private independently held original", () => true, f.Token);
                TestContext.Current.TestOutputHelper!.WriteLine("Running and pending submitted work ready.");
                var nativeParent = Path.GetFullPath(".net-test-artifacts");
                Directory.CreateDirectory(nativeParent);
                using var native = new NativeUxFixtureSession(nativeParent);
                await native.InitializeAsync();
                TestContext.Current.TestOutputHelper!.WriteLine("Native fixture initialized.");
                native.Main.BindRemainingWork(new(composed.Workspace, composed.Conversation, composed.Access, f.Time), composed.Conversation);
                native.Main.CommandText = "private unsent command";
                var window = new ResponseWindow(native.Main);
                window.Show();
                using var questions = new NativeQuestionHost(f.Store, f.Time, NullLogger<NativeQuestionViewModel>.Instance);
                questions.BindGate(() => native.Main.CanRevealPrivatePresentation);
                questions.BindAdmissionRevision(() => 1);
                questions.BindPrivateRevision(() => composed.Access.ControlRevision);
                using var lifecycle = new ApplicationLifecycleController(native.Main, composed.Conversation, native.Queue,
                    questions, new(f.Tasks), new(NullLogger<NativeDetailRenderer>.Instance), () => window);
                var opened = new TaskCompletionSource<QuestionWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var observe = Window.WindowOpenedEvent.AddClassHandler<QuestionWindow>((question, _) => opened.TrySetResult(question));
                var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
                {
                    if (string.Equals(args.PropertyName, nameof(native.Main.SubmittedWorkActivityStatus), StringComparison.Ordinal)
                        && native.Main.LastAllWorkStop is not null)
                    { completed.TrySetResult(); }
                }
                native.Main.PropertyChanged += Changed;
                var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                composed.Conversation.RunningConversationChanged += status =>
                {
                    if (status.Phase == ConversationExecutionPhase.Cancelling) { stopping.TrySetResult(); }
                };
                var closed = 0;
                native.Main.WindowActionRequested += (_, action) => { if (action == WindowAction.Close) { closed++; } };
                try
                {
                    var taskEvents = f.Count("host_task_events");
                    var messages = f.Count("conversation_messages");
                    var audit = WindowsSqliteConversationTests.AuditText(f);
                    window.FindControl<Button>("RefreshSubmittedActivity")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    window.FindControl<TextBlock>("SubmittedActivityStatus")!.Text.Should()
                        .Contain("busy 1").And.Contain("queued 2").And.Contain("held conversations 1").And.NotContain("private");
                    f.Count("host_task_events").Should().Be(taskEvents);
                    f.Count("conversation_messages").Should().Be(messages);
                    WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
                    adapter.Calls.Should().Be(2);
                    first.Completion.IsCompleted.Should().BeFalse();
                    native.Main.CommandText.Should().Be("private unsent command");

                    window.FindControl<Button>("StopSubmittedActivity")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var question = await opened.Task.WaitAsync(TimeSpan.FromSeconds(30), f.Token);
                    TestContext.Current.TestOutputHelper!.WriteLine("Separate Stop All confirmation opened.");
                    question.FindControl<TextBlock>("TargetLabel")!.Text.Should().Contain("stop-all-work/host-stop-all-work");
                    first.Completion.IsCompleted.Should().BeFalse("opening review is not cancellation");
                    if (confirm)
                    {
                        var choice = question.GetLogicalDescendants().OfType<RadioButton>().Single(item =>
                            item.Content is string text && text.StartsWith("Confirm:", StringComparison.Ordinal));
                        await AwaitSubmittedActivityChoiceAsync(question, choice, f.Token);
                        choice.IsChecked = true;
                        var submit = question.FindControl<Button>("SubmitAnswer")!;
                        submit.IsEnabled.Should().BeTrue(question.FindControl<TextBlock>("QuestionStatus")!.Text);
                        submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await Task.WhenAny(stopping.Task, completed.Task).WaitAsync(TimeSpan.FromSeconds(30), f.Token);
                        stopping.Task.IsCompleted.Should().BeTrue("the reviewed stop must be accepted; outcome is {0}",
                            native.Main.LastAllWorkStop?.Outcome);
                        window.FindControl<Button>("RefreshSubmittedActivity")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        window.FindControl<TextBlock>("SubmittedActivityStatus")!.Text.Should().Contain("cancelling 1");
                        first.Completion.IsCompleted.Should().BeFalse("actual noncooperative provider release has not occurred");
                        completed.Task.IsCompleted.Should().BeFalse();
                        invocation.Release();
                    }
                    else { question.FindControl<Button>("CancelQuestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                    await completed.Task.WaitAsync(TimeSpan.FromSeconds(30), f.Token);
                    TestContext.Current.TestOutputHelper!.WriteLine("Native Stop All result observed.");
                    native.Main.LastAllWorkStop!.Outcome.Should().Be(confirm
                        ? AllWorkStopResult.OutcomeKind.Completed : AllWorkStopResult.OutcomeKind.Cancelled);
                    native.Main.CommandText.Should().Be("private unsent command");
                    window.IsVisible.Should().BeTrue();
                    closed.Should().Be(0);
                    if (confirm)
                    {
                        (await first.Completion).RequireReceipt().Message.AssistantResponse.Should().BeNull();
                        (await pending.Completion).RequireReceipt().Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
                        (await other.Completion).RequireReceipt().Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
                        native.Main.SubmittedWorkActivityStatus.Should().Contain("cancelling 0");
                    }
                    else
                    {
                        first.Completion.IsCompleted.Should().BeFalse();
                        pending.Completion.IsCompleted.Should().BeFalse();
                        other.Completion.IsCompleted.Should().BeFalse();
                        adapter.Calls.Should().Be(2);
                    }
                }
                finally
                {
                    native.Main.PropertyChanged -= Changed;
                    var disposal = composed.Conversation.DisposeAsync();
                    adapter.ReleaseAll();
                    await disposal;
                    window.Close();
                }
            }
            finally
            {
                var disposal = composed.Conversation.DisposeAsync();
                adapter.ReleaseAll();
                await disposal;
            }
        });
    }

    private static async Task AwaitSubmittedActivityChoiceAsync(QuestionWindow question, RadioButton choice, CancellationToken token)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
        {
            if (question.IsActive && choice.IsEffectivelyEnabled) { ready.TrySetResult(); }
        }
        question.PropertyChanged += Changed;
        choice.PropertyChanged += Changed;
        try
        {
            if (question.IsActive && choice.IsEffectivelyEnabled) { ready.TrySetResult(); }
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
        }
        finally
        {
            question.PropertyChanged -= Changed;
            choice.PropertyChanged -= Changed;
        }
    }
}
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.ViewModels;
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
    public async Task NativeApplicationExitReviewsEveryAcceptedOriginalAcrossSessionsAndJoinsBeforeClose(bool confirm)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var original = f.Request.SessionId;
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new PendingTurnAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            var sessions = new SessionsWindow(model, owner);
            sessions.Show();
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent);
            await native.InitializeAsync();
            using var questions = new NativeQuestionHost(f.Store, f.Time, NullLogger<NativeQuestionViewModel>.Instance);
            questions.BindGate(() => native.Main.CanRevealPrivatePresentation);
            questions.BindAdmissionRevision(() => 1);
            questions.BindPrivateRevision(() => composed.Access.ControlRevision);
            using var lifecycle = new ApplicationLifecycleController(native.Main, composed.Conversation, native.Queue,
                questions, new(f.Tasks), new(NullLogger<NativeDetailRenderer>.Instance), () => sessions);
            Func<bool>? unsentCurrent = null;
            native.Main.BindConversationShutdown(async () =>
            {
                if (!await owner.PrepareShutdown()) { return false; }
                unsentCurrent = model.CaptureUnsentShutdownEligibility();
                return true;
            }, () => unsentCurrent?.Invoke() == true);
            var opened = new TaskCompletionSource<QuestionWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var observe = Window.WindowOpenedEvent.AddClassHandler<QuestionWindow>((window, _) => opened.TrySetResult(window));
            var closed = 0;
            native.Main.WindowActionRequested += (_, action) => { if (action == WindowAction.Close) { closed++; } };
            PendingTurnInvocation? running = null;
            Task? first = null;
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(item => item.Authority.SessionId == original));
                model.ComposerBody = "running original in another session";
                sessions.FindControl<Button>("SubmitConversation")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                first = sessions.ConversationOperation;
                running = await adapter.NextAsync();
                await WhenModelAsync(model, () => model.CanCompose && !model.ComposerDirty);
                model.ComposerBody = "pending original in another session";
                sessions.FindControl<Button>("SubmitConversation")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await sessions.ConversationOperation;
                await model.SelectAsync(model.Sessions.Single(item => item.Authority.SessionId != original));
                model.ComposerBody.Should().BeEmpty();
                var exiting = native.Main.ExitAsync();
                var question = await opened.Task.WaitAsync(f.Token);
                question.Owner!.GetLogicalDescendants().OfType<TextBlock>().Select(item => item.Text)
                    .Should().Contain(item => item != null && item.Contains("running original in another session", StringComparison.Ordinal))
                    .And.Contain(item => item != null && item.Contains("pending original in another session", StringComparison.Ordinal));
                first.IsCompleted.Should().BeFalse();
                adapter.Calls.Should().Be(1);
                closed.Should().Be(0);
                if (!confirm)
                {
                    question.FindControl<Button>("CancelQuestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await exiting;
                    first.IsCompleted.Should().BeFalse();
                    var target = await f.Store.ReadMetadataAsync(original, f.Token);
                    (await composed.Conversation.ReadPendingConversationAsync(target, () => true, f.Token))
                        .Turns.Should().Contain(item => string.Equals(item.Submission.Message.Body, "pending original in another session", StringComparison.Ordinal));
                    closed.Should().Be(0);
                }
                else
                {
                    question.GetLogicalDescendants().OfType<RadioButton>()
                        .Single(item => item.Content is string text && text.StartsWith("Confirm:", StringComparison.Ordinal)).IsChecked = true;
                    var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    void Changed(Kora.Core.Conversation.ConversationExecutionStatus status)
                    {
                        if (status.Phase == Kora.Core.Conversation.ConversationExecutionPhase.Cancelling) { stopped.TrySetResult(); }
                    }
                    composed.Conversation.RunningConversationChanged += Changed;
                    try
                    {
                        question.FindControl<Button>("SubmitAnswer")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await stopped.Task.WaitAsync(f.Token);
                    }
                    finally { composed.Conversation.RunningConversationChanged -= Changed; }
                    exiting.IsCompleted.Should().BeFalse();
                    first.IsCompleted.Should().BeFalse();
                    closed.Should().Be(0, "the native effect cannot precede the noncooperative provider's physical join");
                    running.Release();
                    await exiting;
                    closed.Should().Be(1);
                    adapter.Calls.Should().Be(1);
                    var page = await f.Store.ReadConversationPageAsync(original, null, 25, f.Token);
                    page.Messages.Should().HaveCount(2).And.OnlyContain(item =>
                        item.Outcome == Kora.Core.Dependencies.ModelTurnOutcome.Cancelled && item.AssistantResponse == null);
                }
            }
            finally
            {
                adapter.ReleaseAll();
                await composed.Conversation.DisposeAsync();
                if (first is not null) { await first; }
                sessions.ClosePrivate();
                owner.Revoke();
            }
        });
    }
}

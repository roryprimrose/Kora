using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Presentation;
using Kora.Core.Voice;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeExactDetailsThenDeliberateReviewAndUndoPreserveIndependentConversationsAndBackgroundResults(bool compact)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var first = await WindowsSqliteAnswerReviewTests.Answer(f, f.Request.SessionId, "first");
            await WindowsSqliteAnswerReviewTests.Answer(f, f.Request.SessionId, "unseen");
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            var other = await WindowsSqliteAnswerReviewTests.Answer(f, f.Request.SessionId, "other topic");
            var opened = new List<AdmittedDetailContent>();
            await using var composition = new Composition(f, async (content, admitted, resolve, token) =>
            {
                admitted().Should().BeTrue();
                content.Origin.Should().Be(DetailContentOrigin.RetainedAssistant);
                (await resolve(token)).IsSameSnapshot(content).Should().BeTrue();
                opened.Add(content);
                return "Exact canonical Details opened; not marked.";
            });
            var model = composition.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single(entry => entry.Authority.SessionId == first.Request.SessionId));
            model.ComposerBody = "independent unsent input";
            var owner = new SessionsPresentationOwner(model);
            Window window = compact ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
            window.Show();
            AnswerReviewPanel? panel = null;
            try
            {
                var currentPanel = window.GetVisualDescendants().OfType<AnswerReviewPanel>().Single();
                panel = currentPanel;
                var expander = currentPanel.GetVisualDescendants().OfType<Expander>().Single();
                expander.IsExpanded = true;
                await Click("ReadNotMarked");
                model.AnswerReviewStatus.Should().Contain("2 of 2").And.Contain("not marked reviewed");
                f.Count("conversation_answer_reviews").Should().Be(0);
                var row = model.AnswerReviewRows[0];
                model.SelectedAnswerReview = row with { Sequence = row.Sequence };
                model.SelectedAnswerReview.Should().BeNull("equal/copied rows are not native selection");
                model.SelectedAnswerReview = row;
                model.CanMarkAnswerReviewed.Should().BeFalse("opening is separate from selecting");
                await Click("OpenAnswer");
                model.AnswerReviewStatus.Should().NotStartWith("Unavailable:", model.ConversationStatus);
                opened.Single().ConversationMessage.Should().Be(first);
                f.Count("conversation_answer_reviews").Should().Be(0, "Details is not a read receipt");
                model.CanMarkAnswerReviewed.Should().BeTrue();
                await Click("Mark");
                model.AnswerReviewStatus.Should().Contain("Only this exact answer marked reviewed").And.Contain("1 of 2");
                model.AnswerReviewRows.Should().ContainSingle().Which.RequestId.Should().NotBe(first.Request.RequestId);
                model.ComposerBody.Should().Be("independent unsent input");
                await WindowsSqliteAnswerReviewTests.Answer(f, first.Request.SessionId, "background");
                await Click("ReadNotMarked");
                model.AnswerReviewStatus.Should().Contain("2 of 3");
                await Click("ReadAll");
                model.SelectedAnswerReview = model.AnswerReviewRows.Single(value => value.RequestId == first.Request.RequestId);
                await Click("OpenAnswer");
                model.CanUnmarkAnswerReviewed.Should().BeTrue();
                await Click("Unmark");
                model.AnswerReviewStatus.Should().Contain("Only this exact answer unmarked").And.Contain("3 of 3");
                var otherPage = await f.Store.ReadAnswerReviewsAsync(await f.Store.ReadMetadataAsync(other.Request.SessionId, f.Token),
                    ConversationAnswerReviewFilter.NotMarkedReviewed, null, 25, f.Token);
                otherPage.NotMarkedReviewed.Should().Be(1);
                f.Count("scoped_grants").Should().Be(0);
                f.Count("host_questions").Should().Be(0);

                async Task Click(string name)
                {
                    currentPanel.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await currentPanel.Operation;
                }
            }
            finally
            {
                try { owner.Revoke(); }
                finally
                {
                    if (panel is not null)
                    {
#pragma warning disable VSTHRD003 // Join the exact native operation before the fixture deletes its private store.
                        await panel.Operation;
#pragma warning restore VSTHRD003
                    }
                }
            }
        });
    }

    [Theory]
    [InlineData("pointer", false, "row")]
    [InlineData("pointer", true, "row")]
    [InlineData("space", false, "row")]
    [InlineData("space", true, "row")]
    [InlineData("enter", false, "row")]
    [InlineData("enter", true, "row")]
    [InlineData("pointer", false, "session")]
    [InlineData("pointer", true, "session")]
    [InlineData("space", false, "session")]
    [InlineData("space", true, "session")]
    [InlineData("enter", false, "session")]
    [InlineData("enter", true, "session")]
    [InlineData("pointer", false, "page")]
    [InlineData("pointer", true, "page")]
    [InlineData("space", false, "page")]
    [InlineData("space", true, "page")]
    [InlineData("enter", false, "page")]
    [InlineData("enter", true, "page")]
    [InlineData("pointer", false, "presentation")]
    [InlineData("pointer", true, "presentation")]
    [InlineData("space", false, "presentation")]
    [InlineData("space", true, "presentation")]
    [InlineData("enter", false, "presentation")]
    [InlineData("enter", true, "presentation")]
    [InlineData("pointer", false, "disposal")]
    [InlineData("pointer", true, "disposal")]
    [InlineData("space", false, "disposal")]
    [InlineData("space", true, "disposal")]
    [InlineData("enter", false, "disposal")]
    [InlineData("enter", true, "disposal")]
    public async Task NativeStaleAnswerReviewActivationCannotRetargetReplacementAndFreshChoiceWorks(
        string gesture, bool reviewed, string replacement)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var first = await WindowsSqliteAnswerReviewTests.Answer(f, f.Request.SessionId, "first");
            var second = await WindowsSqliteAnswerReviewTests.Answer(f, f.Request.SessionId, "second");
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            var other = await WindowsSqliteAnswerReviewTests.Answer(f, f.Request.SessionId, "other topic");
            if (reviewed)
            {
                foreach (var answer in new[] { first, second, other })
                {
                    var target = await f.Store.ReadMetadataAsync(answer.Request.SessionId, f.Token);
                    var page = await f.Store.ReadAnswerReviewsAsync(target, ConversationAnswerReviewFilter.All,
                        null, 25, f.Token);
                    await WindowsSqliteAnswerReviewTests.Mark(f, page,
                        page.Rows.Single(row => row.RequestId == answer.Request.RequestId), true);
                }
            }
            var originalTarget = await f.Store.ReadMetadataAsync(first.Request.SessionId, f.Token);
            var otherTarget = await f.Store.ReadMetadataAsync(other.Request.SessionId, f.Token);
            var originalPage = await f.Store.ReadAnswerReviewsAsync(originalTarget, ConversationAnswerReviewFilter.All,
                null, 25, f.Token);
            var otherPage = await f.Store.ReadAnswerReviewsAsync(otherTarget, ConversationAnswerReviewFilter.All,
                null, 25, f.Token);
            var originalRetention = await f.Store.ReadRetentionAsync(first.Request.SessionId, f.Token);
            var otherRetention = await f.Store.ReadRetentionAsync(other.Request.SessionId, f.Token);
            f.Time.Now = f.Time.Now.AddHours(1);
            var opened = new List<ConversationMessage>();
            await using var composition = new Composition(f, async (content, admitted, resolve, token) =>
            {
                admitted().Should().BeTrue();
                content.Origin.Should().Be(DetailContentOrigin.RetainedAssistant);
                (await resolve(token)).IsSameSnapshot(content).Should().BeTrue();
                opened.Add(content.ConversationMessage!);
                return "Exact canonical Details opened; not a review receipt.";
            });
            var model = composition.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single(entry => entry.Authority.SessionId == first.Request.SessionId));
            model.ComposerBody = "original independent unsent input";
            var panel = new AnswerReviewPanel();
            var window = new Window { Content = panel, DataContext = model, Width = 720, Height = 800 };
            window.Show();
            try
            {
                panel.GetVisualDescendants().OfType<Expander>().Single().IsExpanded = true;
                await Click("ReadAll");
                model.SelectedAnswerReview = model.AnswerReviewRows.Single(row => row.RequestId == first.Request.RequestId);
                await Click("OpenAnswer");
                var originalRow = model.SelectedAnswerReview;
                var button = panel.FindControl<Button>(reviewed ? "Unmark" : "Mark")!;
                using var activation = new NativeAnswerReviewActivationBarrier(button);
                var audits = WindowsSqliteConversationTests.AuditText(f);
                var taskCount = f.Count("host_tasks");
                var beforeGesture = panel.Operation;
                Activate();
                activation.Gesture.IsCompletedSuccessfully.Should().BeTrue("native input must finish tunnelling through production capture");
                panel.Operation.Should().BeSameAs(beforeGesture, "the held gesture must not start a native mutation");
                model.CanMarkAnswerReviewed.Should().Be(!reviewed, "holding the native activation must defer mutation");
                model.CanUnmarkAnswerReviewed.Should().Be(reviewed);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audits);
                var replacementAnswer = replacement is "presentation" ? first : replacement is "session" ? other : second;
                if (replacement is "session")
                {
                    await model.SelectAsync(model.Sessions.Single(entry => entry.Authority.SessionId == other.Request.SessionId));
                    model.ComposerBody = "replacement independent unsent input";
                    await Click("ReadAll");
                }
                if (replacement is "page") { await Click("ReadAll"); }
                if (replacement is "disposal")
                {
                    window.Content = null;
                    window.Content = panel;
                }
                model.SelectedAnswerReview = model.AnswerReviewRows.Single(row => row.RequestId == replacementAnswer.Request.RequestId);
                if (replacement is "presentation") { model.SelectedAnswerReview.Should().BeSameAs(originalRow); }
                await Click("OpenAnswer");
                opened.Should().Equal(first, replacementAnswer);
                model.CanMarkAnswerReviewed.Should().Be(!reviewed);
                model.CanUnmarkAnswerReviewed.Should().Be(reviewed);
                activation.Release();
                var beforeStaleClick = panel.Operation;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                panel.Operation.Should().NotBeSameAs(beforeStaleClick, "the delayed Click must deliver the captured decision");
                await panel.Operation;
                model.ConversationStatus.Should().Contain("old control cannot change a replacement answer");
                model.SelectedAnswerReview!.RequestId.Should().Be(replacementAnswer.Request.RequestId);
                model.CanMarkAnswerReviewed.Should().Be(!reviewed, "refusing an old activation must not retire the current replacement");
                model.CanUnmarkAnswerReviewed.Should().Be(reviewed);
                var originalUnchanged = await f.Store.ReadAnswerReviewsAsync(originalTarget, ConversationAnswerReviewFilter.All,
                    null, 25, f.Token);
                var otherUnchanged = await f.Store.ReadAnswerReviewsAsync(otherTarget, ConversationAnswerReviewFilter.All,
                    null, 25, f.Token);
                originalUnchanged.Should().BeEquivalentTo(originalPage);
                otherUnchanged.Should().BeEquivalentTo(otherPage);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audits,
                    "an old native activation must produce neither a requested mutation nor a durable Succeeded audit");
                composition.Audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
                f.Count("conversation_answer_reviews").Should().Be(reviewed ? 3 : 0);
                f.Count("host_tasks").Should().Be(taskCount);
                f.Count("conversation_messages").Should().Be(3);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                f.Count("perpetual_grants").Should().Be(0);
                (await f.Store.ReadRetentionAsync(first.Request.SessionId, f.Token)).Should().Be(originalRetention);
                (await f.Store.ReadRetentionAsync(other.Request.SessionId, f.Token)).Should().Be(otherRetention);
                var replacementRow = model.SelectedAnswerReview!;
                var beforeFreshGesture = panel.Operation;
                Activate();
                activation.Gesture.IsCompletedSuccessfully.Should().BeTrue("a fresh deliberate activation must use the current native choice");
                panel.Operation.Should().NotBeSameAs(beforeFreshGesture, "fresh native input must actually start its own operation");
                await panel.Operation;
                model.AnswerReviewStatus.Should().Contain(reviewed ? "Only this exact answer unmarked" : "Only this exact answer marked reviewed");
                var originalAfter = await f.Store.ReadAnswerReviewsAsync(originalTarget, ConversationAnswerReviewFilter.All,
                    null, 25, f.Token);
                var otherAfter = await f.Store.ReadAnswerReviewsAsync(otherTarget, ConversationAnswerReviewFilter.All,
                    null, 25, f.Token);
                var expectedRows = originalPage.Rows.Concat(otherPage.Rows).Select(row =>
                    row.RequestId == replacementRow.RequestId ? row with
                    { MarkedReviewed = !reviewed, ReviewRevision = row.ReviewRevision + 1 } : row);
                originalAfter.Rows.Concat(otherAfter.Rows).Should().Equal(expectedRows);
                WindowsSqliteConversationTests.AuditText(f).Should().NotBe(audits);
                f.Count("conversation_answer_reviews").Should().Be(reviewed ? 3 : 1);
                f.Count("host_tasks").Should().Be(taskCount + 1, "only the fresh exact acknowledgement creates a control task");
                foreach (var answer in new[] { first, second, other })
                {
                    (await f.Store.ReadConversationMessageAsync(answer.Request.SessionId, answer.Request.RequestId, f.Token))
                        .Should().Be(answer);
                }
                f.Count("conversation_messages").Should().Be(3);
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                f.Count("perpetual_grants").Should().Be(0);
                (await f.Store.ReadRetentionAsync(first.Request.SessionId, f.Token)).Should().Be(originalRetention);
                (await f.Store.ReadRetentionAsync(other.Request.SessionId, f.Token)).Should().Be(otherRetention);
                model.ComposerBody.Should().Be(replacement is "session"
                    ? "replacement independent unsent input" : "original independent unsent input");
                if (replacement is "session")
                {
                    await model.SelectAsync(model.Sessions.Single(entry => entry.Authority.SessionId == first.Request.SessionId));
                    model.ComposerBody.Should().Be("original independent unsent input");
                }

                async Task Click(string name)
                {
                    panel.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await panel.Operation;
                }

                void Activate()
                {
                    ActivateAnswerReview(window, button, gesture);
                }
            }
            finally
            {
                try { window.Close(); model.Close(); }
                finally
                {
#pragma warning disable VSTHRD003 // Cancellation is not quiescence; join even when a native assertion fails.
                    await panel.Operation;
#pragma warning restore VSTHRD003
                }
            }
        });
    }

    [Theory]
    [InlineData("pointer", false, "privacy")]
    [InlineData("pointer", true, "privacy")]
    [InlineData("space", false, "privacy")]
    [InlineData("space", true, "privacy")]
    [InlineData("enter", false, "privacy")]
    [InlineData("enter", true, "privacy")]
    [InlineData("pointer", false, "close")]
    [InlineData("pointer", true, "close")]
    [InlineData("space", false, "close")]
    [InlineData("space", true, "close")]
    [InlineData("enter", false, "close")]
    [InlineData("enter", true, "close")]
    public async Task NativeCapturedAnswerReviewAfterPrivateRetirementCannotMutateOrPublishSourceFeedback(
        string gesture, bool reviewed, string retirement)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var answer = await WindowsSqliteAnswerReviewTests.Answer(f, f.Request.SessionId, "private original");
            var target = await f.Store.ReadMetadataAsync(answer.Request.SessionId, f.Token);
            var page = await f.Store.ReadAnswerReviewsAsync(target, ConversationAnswerReviewFilter.All, null, 25, f.Token);
            if (reviewed) { await WindowsSqliteAnswerReviewTests.Mark(f, page, page.Rows.Single(), true); }
            var original = await f.Store.ReadAnswerReviewsAsync(target, ConversationAnswerReviewFilter.All, null, 25, f.Token);
            var retention = await f.Store.ReadRetentionAsync(answer.Request.SessionId, f.Token);
            await using var composition = new Composition(f, async (content, admitted, resolve, token) =>
            {
                admitted().Should().BeTrue();
                (await resolve(token)).IsSameSnapshot(content).Should().BeTrue();
                return "Exact canonical Details opened.";
            });
            var model = composition.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            var panel = new AnswerReviewPanel();
            var window = new Window { Content = panel, DataContext = model, Width = 720, Height = 800 };
            window.Show();
            try
            {
                panel.GetVisualDescendants().OfType<Expander>().Single().IsExpanded = true;
                panel.FindControl<Button>("ReadAll")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await panel.Operation;
                model.SelectedAnswerReview = model.AnswerReviewRows.Single();
                panel.FindControl<Button>("OpenAnswer")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await panel.Operation;
                var button = panel.FindControl<Button>(reviewed ? "Unmark" : "Mark")!;
                using var activation = new NativeAnswerReviewActivationBarrier(button);
                var audits = WindowsSqliteConversationTests.AuditText(f);
                var tasks = f.Count("host_tasks");
                var beforeGesture = panel.Operation;
                ActivateAnswerReview(window, button, gesture);
                activation.Gesture.IsCompletedSuccessfully.Should().BeTrue();
                panel.Operation.Should().BeSameAs(beforeGesture);
                if (retirement is "privacy")
                {
                    composition.Access.CanInspect = false;
                    composition.Access.ControlRevision++;
                    await model.RefreshAsync();
                }
                else { model.Close(); }
                var status = model.ConversationStatus;
                activation.Release();
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await panel.Operation;
                model.ConversationStatus.Should().Be(status, "closed private admission must not publish stale source feedback");
                model.ConversationStatus.Should().NotContain("old control cannot change a replacement answer")
                    .And.NotContain(answer.Body).And.NotContain(answer.AssistantResponse!.Body);
                model.AnswerReviewStatus.Should().NotContain(answer.Body).And.NotContain(answer.AssistantResponse!.Body);
                model.ComposerBody.Should().BeEmpty();
                model.AnswerReviewRows.Should().BeEmpty();
                model.SelectedAnswerReview.Should().BeNull();
                model.CanMarkAnswerReviewed.Should().BeFalse();
                model.CanUnmarkAnswerReviewed.Should().BeFalse();
                (await f.Store.ReadAnswerReviewsAsync(target, ConversationAnswerReviewFilter.All, null, 25, f.Token))
                    .Should().BeEquivalentTo(original);
                (await f.Store.ReadConversationMessageAsync(answer.Request.SessionId, answer.Request.RequestId, f.Token))
                    .Should().Be(answer);
                (await f.Store.ReadRetentionAsync(answer.Request.SessionId, f.Token)).Should().Be(retention);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audits);
                composition.Audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
                f.Count("host_tasks").Should().Be(tasks);
                f.Count("conversation_messages").Should().Be(1);
                f.Count("conversation_answer_reviews").Should().Be(reviewed ? 1 : 0);
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                f.Count("perpetual_grants").Should().Be(0);
            }
            finally
            {
                try { window.Close(); model.Close(); }
                finally
                {
#pragma warning disable VSTHRD003 // Retire and join the native owner before private database deletion on every exit.
                    await panel.Operation;
#pragma warning restore VSTHRD003
                }
            }
        });
    }

    private static void ActivateAnswerReview(Window window, Button button, string gesture)
    {
        window.Activate();
        window.UpdateLayout();
        button.IsEnabled.Should().BeTrue();
        button.Focus().Should().BeTrue();
        button.IsFocused.Should().BeTrue();
        if (gesture is "pointer")
        {
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point);
            button.IsPointerOver.Should().BeTrue();
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
        }
        else
        {
            var key = gesture is "enter" ? Key.Enter : Key.Space;
            var physical = gesture is "enter" ? PhysicalKey.Enter : PhysicalKey.Space;
            var symbol = gesture is "enter" ? "\r" : " ";
            window.KeyPress(key, RawInputModifiers.None, physical, symbol);
            window.KeyRelease(key, RawInputModifiers.None, physical, symbol);
        }
    }

    private sealed class NativeAnswerReviewActivationBarrier : IObserver<RoutedEventArgs>, IDisposable
    {
        private readonly Button button;
        private readonly IDisposable pointerPressed;
        private readonly IDisposable pointerReleased;
        private readonly IDisposable keyDown;
        private readonly IDisposable keyUp;
        private TaskCompletionSource gesture = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool hold = true;

        internal NativeAnswerReviewActivationBarrier(Button button)
        {
            this.button = button;
            // Tunnel handlers run in reverse registration order. Hold only after production capture.
            pointerPressed = InputElement.PointerPressedEvent.RouteFinished.Subscribe(this);
            pointerReleased = InputElement.PointerReleasedEvent.RouteFinished.Subscribe(this);
            keyDown = InputElement.KeyDownEvent.RouteFinished.Subscribe(this);
            keyUp = InputElement.KeyUpEvent.RouteFinished.Subscribe(this);
        }

        internal Task Gesture => gesture.Task;

        internal void Release()
        {
            hold = false;
            gesture = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void OnNext(RoutedEventArgs args)
        {
            if (args.Route != RoutingStrategies.Tunnel || args.Source is not Visual source
                || !ReferenceEquals(source, button) && !button.IsVisualAncestorOf(source)
                || args is KeyEventArgs { Key: not (Key.Space or Key.Enter) }) { return; }
            if (args.RoutedEvent == InputElement.PointerPressedEvent || args.RoutedEvent == InputElement.KeyDownEvent)
            { gesture.TrySetResult(); }
            if (hold) { args.Handled = true; }
        }

        public void OnError(Exception error) => throw new InvalidOperationException("The native answer-review input barrier failed.", error);
        public void OnCompleted() { }

        public void Dispose()
        {
            pointerPressed.Dispose();
            pointerReleased.Dispose();
            keyDown.Dispose();
            keyUp.Dispose();
        }
    }

    [Theory]
    [InlineData("selection", false)]
    [InlineData("selection", true)]
    [InlineData("privacy", false)]
    [InlineData("privacy", true)]
    [InlineData("close", false)]
    [InlineData("close", true)]
    public async Task AnswerReviewAndNewTaskOutputRetireTogetherWithoutChangingAcknowledgementOrSubmitting(
        string change, bool reviewed)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var answer = await WindowsSqliteAnswerReviewTests.Answer(f, f.Request.SessionId, "first");
            var target = await f.Store.ReadMetadataAsync(answer.Request.SessionId, f.Token);
            var originalPage = await f.Store.ReadAnswerReviewsAsync(target, ConversationAnswerReviewFilter.All,
                null, 25, f.Token);
            if (reviewed) { await WindowsSqliteAnswerReviewTests.Mark(f, originalPage, originalPage.Rows.Single(), true); }
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            await WindowsSqliteAnswerReviewTests.Answer(f, f.Request.SessionId, "other topic");
            var other = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
            var originalRetention = await f.Store.ReadRetentionAsync(target.Authority.SessionId, f.Token);
            var otherRetention = await f.Store.ReadRetentionAsync(other.Authority.SessionId, f.Token);
            var audits = WindowsSqliteConversationTests.AuditText(f);
            var taskCount = f.Count("host_tasks");
            await using var composition = new Composition(f, async (content, admitted, resolve, token) =>
            {
                admitted().Should().BeTrue();
                content.ConversationMessage.Should().Be(answer);
                (await resolve(token)).IsSameSnapshot(content).Should().BeTrue();
                return "Exact canonical Details opened; flag unchanged.";
            });
            var presented = 0;
            composition.Conversation.BindTaskResponseOutput((mode, _) => new(mode, ResponseOutputMode.Hybrid,
                () => composition.Access.CanInspect && composition.Access.CanControl, (_, _, _, _) =>
                {
                    presented++;
                    return Task.CompletedTask;
                }));
            var model = composition.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single(entry => entry.Authority.SessionId == answer.Request.SessionId));
            model.ComposerBody = "Unconfirmed original task input";
            await model.ReadAnswerReviewsAsync(ConversationAnswerReviewFilter.All);
            model.SelectedAnswerReview = model.AnswerReviewRows.Single();
            await model.OpenAnswerReviewAsync(static () => true);
            model.CanMarkAnswerReviewed.Should().Be(!reviewed);
            model.CanUnmarkAnswerReviewed.Should().Be(reviewed);
            model.TaskOutputMode = TaskResponseMode.VisualOnly;
            await model.ReviewTaskOutputAsync();
            model.CanConfirmTaskOutput.Should().BeTrue();
            model.TaskOutputReviewText.Should().Contain("Unconfirmed original task input");
            var staleConfirmation = model.CaptureTaskOutputDecision(true);
            var notifications = new HashSet<string>(StringComparer.Ordinal);
            void Observe(object? sender, PropertyChangedEventArgs args)
            {
                if (args.PropertyName is { } name) { notifications.Add(name); }
            }
            model.PropertyChanged += Observe;
            try
            {
                await model.ReadConversationAsync();
                notifications.Should().Contain(nameof(SessionsViewModel.AnswerReviewRows))
                    .And.Contain(nameof(SessionsViewModel.CanConfirmTaskOutput));
                model.CanMarkAnswerReviewed.Should().Be(!reviewed);
                model.CanUnmarkAnswerReviewed.Should().Be(reviewed);
                model.CanConfirmTaskOutput.Should().BeTrue();

                if (change is "selection")
                { await model.SelectAsync(model.Sessions.Single(entry => entry.Authority.SessionId == other.Authority.SessionId)); }
                if (change is "privacy")
                {
                    composition.Access.CanInspect = false;
                    composition.Access.ControlRevision++;
                    await model.RefreshAsync();
                }
                if (change is "close") { model.Close(); }

                model.AnswerReviewRows.Should().BeEmpty();
                model.SelectedAnswerReview.Should().BeNull();
                model.CanMarkAnswerReviewed.Should().BeFalse();
                model.CanUnmarkAnswerReviewed.Should().BeFalse();
                model.CanConfirmTaskOutput.Should().BeFalse();
                model.TaskOutputReviewText.Should().NotContain("Unconfirmed original task input");
                await model.SetAnswerReviewedAsync(!reviewed);
                await staleConfirmation();
                var refreshed = await f.Store.ReadAnswerReviewsAsync(target, ConversationAnswerReviewFilter.All,
                    null, 25, f.Token);
                refreshed.Rows.Single().Should().Be(originalPage.Rows.Single() with
                { ReviewRevision = reviewed ? 1 : 0, MarkedReviewed = reviewed });
                var otherPage = await f.Store.ReadAnswerReviewsAsync(other, ConversationAnswerReviewFilter.All,
                    null, 25, f.Token);
                otherPage.NotMarkedReviewed.Should().Be(1);
                presented.Should().Be(0);
                f.Count("conversation_messages").Should().Be(2);
                f.Count("conversation_answer_reviews").Should().Be(reviewed ? 1 : 0);
                f.Count("host_tasks").Should().Be(taskCount);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audits);
                (await f.Store.ReadRetentionAsync(target.Authority.SessionId, f.Token)).Should().Be(originalRetention);
                (await f.Store.ReadRetentionAsync(other.Authority.SessionId, f.Token)).Should().Be(otherRetention);
                (await f.Store.ReadConversationMessageAsync(answer.Request.SessionId, answer.Request.RequestId,
                    f.Token)).Should().Be(answer);
            }
            finally { model.PropertyChanged -= Observe; }
        });
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("presentation")]
    [InlineData("head")]
    public async Task LateAnswerDetailsOrChangedSourceNeverAcquiresReviewChoice(string revoke)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await WindowsSqliteAnswerReviewTests.Answer(f, f.Request.SessionId, "first");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var composition = new Composition(f, (_, _, _, _) =>
        {
            entered.SetResult();
            return release.Task;
        });
        var model = composition.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadAnswerReviewsAsync(ConversationAnswerReviewFilter.NotMarkedReviewed);
        model.SelectedAnswerReview = model.AnswerReviewRows.Single();
        var presentation = true;
        var opening = model.OpenAnswerReviewAsync(() => presentation);
        await entered.Task;
        if (revoke is "privacy") { composition.Access.CanInspect = false; composition.Access.ControlRevision++; }
        if (revoke is "presentation") { presentation = false; }
        release.SetResult("late opening");
        await opening;
        if (revoke is "head")
        {
            await WindowsSqliteAnswerReviewTests.Answer(f, f.Request.SessionId, "background");
            await model.SetAnswerReviewedAsync(true);
            model.AnswerReviewStatus.Should().StartWith("Unavailable:");
        }
        model.CanMarkAnswerReviewed.Should().BeFalse();
        f.Count("conversation_answer_reviews").Should().Be(0);
    }
}

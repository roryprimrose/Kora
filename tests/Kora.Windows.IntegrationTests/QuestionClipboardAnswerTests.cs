using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Context;
using Kora.Application.Interaction;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Tools.Clipboard;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class QuestionClipboardAnswerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidNativeEditRetiresClipboardReviewButPreservesCompleteTextAndValidationFailure(bool accept)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await Fixture.Create();
            var original = f.Model.Key;
            await f.Capture(grant: true);
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
            await WhenEditable(f.Model);
            if (accept)
            {
                await f.Answer.AcceptAsync();
                await WhenEditable(f.Model);
            }
            var input = f.Window.FindControl<TextBox>("AnswerInput")!;
            var invalid = new string('x', f.Model.Question.Spec.MaximumTextLength + 1);
            input.Focus().Should().BeTrue();
            input.SelectAll();
            f.Window.KeyTextInput(invalid);
            await f.Answer.Quiescence;
            input.Text.Should().Be(invalid);
            f.Model.Answer.Text.Should().Be(invalid);
            f.Model.Status.Should().StartWith("Invalid answer:");
            f.Window.FindControl<TextBlock>("QuestionStatus")!.Text.Should().Contain("Invalid answer:");
            f.Model.CanSubmit.Should().BeFalse();
            f.Model.CanSaveDraft.Should().BeFalse();
            f.Answer.ReviewText.Should().BeEmpty();
            f.Answer.HasAccepted.Should().BeFalse();
            f.Model.Key.Should().Be(original);
            f.Model.Completion.IsCompleted.Should().BeFalse();
            var canonical = (await f.Data.Store.ReadQuestionsAsync(original.Request.SessionId, f.Data.Token)).Single();
            canonical.Status.Should().Be(QuestionStatus.Pending);
            canonical.Draft.Should().BeNull();
            f.Reader.Calls.Should().Be(1);
            f.Reader.Released.Should().BeTrue();
            f.Data.Count("conversation_messages").Should().Be(0);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeEditingRetiresClipboardReviewWithoutRewritingTextOrLosingUndoBeforeExplicitSubmit(bool accept)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await Fixture.Create();
            const string manual = "previous manual answer";
            f.Model.Edit(new([], manual));
            var original = f.Model.Key;
            await f.Capture(grant: true);
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
            await WhenEditable(f.Model);
            if (accept)
            {
                f.Window.FindControl<CheckBox>("ReplaceManualDraft")!.IsChecked = true;
                await f.Answer.AcceptAsync();
                await WhenEditable(f.Model);
            }
            var input = f.Window.FindControl<TextBox>("AnswerInput")!;
            var before = accept ? f.Reader.Text : manual;
            const int start = 3;
            const int end = 8;
            const string replacement = "edited e\u0301\r\n";
            var edited = before[..start] + replacement + before[end..];
            var publications = new List<string?>();
            void Observe(object? sender, AvaloniaPropertyChangedEventArgs args)
            {
                if (args.Property == TextBox.TextProperty) { publications.Add(input.Text); }
            }
            input.IsEffectivelyEnabled.Should().BeTrue();
            input.Focus().Should().BeTrue();
            f.Window.FocusManager!.GetFocusedElement().Should().BeSameAs(input);
            input.CaretIndex = end;
            input.SelectionStart = start;
            input.SelectionEnd = end;
            input.PropertyChanged += Observe;
            try { f.Window.KeyTextInput(replacement); }
            finally { input.PropertyChanged -= Observe; }
            input.Text.Should().Be(edited);
            f.Model.Answer.Text.Should().Be(edited);
            publications.Should().ContainSingle("candidate retirement must not write an older answer into the native edit")
                .Which.Should().Be(edited);
            input.CaretIndex.Should().Be(start + replacement.Length);
            input.SelectionStart.Should().Be(input.CaretIndex);
            input.SelectionEnd.Should().Be(input.CaretIndex);
            input.CanUndo.Should().BeTrue();
            input.Undo();
            input.Text.Should().Be(before);
            f.Model.Answer.Text.Should().Be(before);
            input.CanRedo.Should().BeTrue();
            input.Redo();
            input.Text.Should().Be(edited);
            f.Model.Answer.Text.Should().Be(edited);
            await f.Answer.Quiescence;
            f.Answer.ReviewText.Should().BeEmpty();
            f.Answer.HasAccepted.Should().BeFalse();
            f.Window.FindControl<TextBox>("ClipboardAnswerReview")!.Text.Should().BeNullOrEmpty();
            f.Model.Key.Should().Be(original);
            f.Model.Completion.IsCompleted.Should().BeFalse();
            var pending = (await f.Data.Store.ReadQuestionsAsync(original.Request.SessionId, f.Data.Token)).Single();
            pending.Status.Should().Be(QuestionStatus.Pending);
            pending.Draft.Should().BeNull();
            f.Reader.Calls.Should().Be(1);
            f.Reader.Released.Should().BeTrue();
            f.Data.Count("conversation_messages").Should().Be(0);
            await Task.WhenAll(f.Model.SubmitAsync(), f.Model.SubmitAsync());
            var canonical = (await f.Data.Store.ReadQuestionsAsync(original.Request.SessionId, f.Data.Token)).Single();
            canonical.Key.Should().Be(original.Next());
            canonical.Status.Should().Be(QuestionStatus.Answered);
            canonical.AnswerChannel.Should().Be(RequestOrigin.LocalUi);
            canonical.Draft!.Text.Should().Be(edited);
            f.Reader.Calls.Should().Be(1);
            f.Data.Count("conversation_messages").Should().Be(0);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("previous manual draft")]
    public async Task GrantedSnapshotRequiresCompleteReviewDraftAcceptanceAndIndependentCanonicalSubmit(string manual)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await Fixture.Create();
            f.Model.Edit(new([], manual));
            var original = f.Model.Key;
            f.Reader.Calls.Should().Be(0);
            await f.Capture(grant: true);
            f.Reader.Calls.Should().Be(1);
            f.Model.Answer.Text.Should().Be(manual);
            f.Model.Completion.IsCompleted.Should().BeFalse();
            f.Model.CanSubmit.Should().BeFalse();
            var review = f.Window.FindControl<TextBox>("ClipboardAnswerReview")!;
            review.Text.Should().Contain(f.Reader.Text).And.Contain("CF_UNICODETEXT").And.Contain("native version: 17");
            f.Answer.CanAccept.Should().Be(manual.Length == 0);
            if (manual.Length != 0)
            {
                review.Text.Should().Contain(manual);
                f.Window.FindControl<CheckBox>("ReplaceManualDraft")!.IsChecked = true;
            }
            await f.Answer.AcceptAsync();
            await WhenEditable(f.Model);
            f.Model.Key.Should().Be(original);
            f.Model.Answer.Text.Should().Be(f.Reader.Text);
            f.Window.FindControl<TextBox>("AnswerInput")!.Text.Should().Be(f.Reader.Text);
            f.Model.CanSubmit.Should().BeTrue();
            f.Model.CanSaveDraft.Should().BeFalse();
            await f.Model.SaveDraftAsync();
            (await f.Data.Store.ReadQuestionsAsync(original.Request.SessionId, f.Data.Token)).Single().Draft.Should().BeNull();
            await Task.WhenAll(f.Model.SubmitAsync(), f.Model.SubmitAsync());
            await f.Answer.Quiescence;
            var canonical = (await f.Data.Store.ReadQuestionsAsync(original.Request.SessionId, f.Data.Token)).Single();
            canonical.Key.Should().Be(original.Next());
            canonical.Status.Should().Be(QuestionStatus.Answered);
            canonical.AnswerChannel.Should().Be(RequestOrigin.LocalUi);
            canonical.Draft!.Text.Should().Be(f.Reader.Text);
            f.Reader.Calls.Should().Be(1);
            f.Data.Count("conversation_messages").Should().Be(0, "capture and acceptance never create a new USER task");
        });
    }

    [Fact]
    public async Task ClipboardReviewRemainsSeparateFromPermissionPolicyAndCloseClearsBothWithoutSubmitting()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await Fixture.Create();
            var original = f.Model.Key;
            await f.Capture(grant: true);
            var review = f.Window.FindControl<TextBox>("ClipboardAnswerReview")!;
            var policy = f.Window.FindControl<TextBox>("PolicyExplanation")!;
            review.IsVisible.Should().BeTrue();
            review.Text.Should().Contain(f.Reader.Text);
            policy.IsVisible.Should().BeFalse();
            policy.Text.Should().BeNullOrEmpty();
            f.Window.FindControl<Button>("ExplainPolicy")!.IsVisible.Should().BeFalse();
            f.Window.FindControl<Button>("ReturnToApproval")!.IsVisible.Should().BeFalse();
            var copying = new RoutedEventArgs(TextBox.CopyingToClipboardEvent);
            review.RaiseEvent(copying);
            copying.Handled.Should().BeTrue();
            f.Model.Key.Should().Be(original);
            f.Model.Completion.IsCompleted.Should().BeFalse();
            f.Model.CanSubmit.Should().BeFalse();

            f.Window.Close();
            await f.Answer.Quiescence;
            review.Text.Should().BeNullOrEmpty();
            policy.Text.Should().BeNullOrEmpty();
            f.Window.FindControl<TextBox>("AnswerInput")!.Text.Should().BeNullOrEmpty();
            f.Answer.ReviewText.Should().BeEmpty();
            var canonical = (await f.Data.Store.ReadQuestionsAsync(original.Request.SessionId, f.Data.Token)).Single();
            canonical.Key.Should().Be(original);
            canonical.Status.Should().Be(QuestionStatus.Pending);
            canonical.Draft.Should().BeNull();
            f.Reader.Calls.Should().Be(1);
            f.Reader.Released.Should().BeTrue();
            f.Data.Count("conversation_messages").Should().Be(0);
        });
    }

    [Theory]
    [InlineData("ownership")]
    [InlineData("private")]
    public async Task ChangedNativeRevisionBeforeReadApprovalCannotReadOrReplaceManualDraft(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await Fixture.Create();
            f.Model.Edit(new([], "keep this manual draft"));
            var original = f.Model.Key;
            await f.Capture(grant: true, beforePermissionSubmit: () =>
            {
                if (change is "ownership") { f.AdmissionRevision++; }
                else { f.PrivateRevision++; }
            });
            await f.Answer.Quiescence;
            f.Eligible.Should().BeTrue("a reopened boolean gate cannot replace the original native revision");
            f.Reader.Calls.Should().Be(0);
            f.Model.Answer.Text.Should().Be("keep this manual draft");
            f.Model.Key.Should().Be(original);
            f.Model.Completion.IsCompleted.Should().BeFalse();
            f.Answer.ReviewText.Should().BeEmpty();
            f.Answer.HasAccepted.Should().BeFalse();
            (await f.Data.Store.ReadGrantsAsync(f.Data.Token)).Should().BeEmpty();
            var canonical = (await f.Data.Store.ReadQuestionsAsync(original.Request.SessionId, f.Data.Token))
                .Single(record => record.Key.QuestionId == original.QuestionId);
            canonical.Status.Should().Be(QuestionStatus.Pending);
            canonical.Draft.Should().BeNull();
            f.Data.Count("conversation_messages").Should().Be(0);
        });
    }

    [Fact]
    public async Task DeniedPermissionDoesNotReadOrChangeExistingDraft()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await Fixture.Create();
            f.Model.Edit(new([], "keep this manual draft"));
            await f.Capture(grant: false);
            await f.Answer.Quiescence;
            f.Reader.Calls.Should().Be(0);
            f.Model.Answer.Text.Should().Be("keep this manual draft");
            f.Model.Completion.IsCompleted.Should().BeFalse();
            f.Answer.ReviewText.Should().BeEmpty();
            f.Model.Status.Should().Contain("Denied");
        });
    }

    [Fact]
    public async Task CandidateCancellationAtSubmitBoundaryCannotFallBackToUnreviewedNativeSubmit()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await Fixture.Create();
            await f.Capture(grant: true);
            await f.Answer.AcceptAsync();
            var cancelled = false;
            void CancelAtCommit(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
            {
                if (!cancelled && f.Model.HasPendingOperation)
                {
                    cancelled = true;
                    f.Answer.Clear();
                }
            }
            f.Model.PropertyChanged += CancelAtCommit;
            try { await f.Model.SubmitAsync(); }
            finally { f.Model.PropertyChanged -= CancelAtCommit; }
            cancelled.Should().BeTrue();
            await f.Answer.Quiescence;
            var canonical = (await f.Data.Store.ReadQuestionsAsync(f.Model.Key.Request.SessionId, f.Data.Token)).Single();
            canonical.Status.Should().Be(QuestionStatus.Pending);
            canonical.Draft.Should().BeNull();
            f.Reader.Calls.Should().Be(1);
        });
    }

    [Theory]
    [InlineData("authorization")]
    [InlineData("authentication")]
    [InlineData("consent")]
    [InlineData("security")]
    [InlineData("clarification")]
    public async Task UnknownOrSecurityPurposesCannotUseClipboardShortcut(string purpose)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await Fixture.Create(purpose);
            f.Answer.CanCapture.Should().BeFalse();
            await f.Answer.CaptureAsync();
            f.Reader.Calls.Should().Be(0);
            f.Model.Answer.Text.Should().BeEmpty();
            f.Data.Count("scoped_grants").Should().Be(0);
        });
    }

    [Theory]
    [InlineData("oversize")]
    [InlineData("malformed")]
    [InlineData("credential")]
    [InlineData("empty")]
    [InlineData("unsupported")]
    [InlineData("native-failure")]
    public async Task EntireInvalidSourceIsRejectedWithoutTruncationOrAnswerStorage(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await Fixture.Create();
            f.Model.Edit(new([], "keep"));
            f.Reader.Text = change switch
            {
                "oversize" => new string('x', LocalModelQuestion.MaximumFreeTextLength + 1),
                "malformed" => "\ud800",
                "credential" => "Authorization: Bearer " + new string('b', 32),
                "empty" => " \r\n ",
                _ => "valid text",
            };
            if (change is "unsupported") { f.Reader.Outcome = ClipboardOutcome.UnsupportedFormat; }
            if (change is "native-failure") { f.Reader.Outcome = ClipboardOutcome.Unavailable; }
            await f.Capture(grant: true);
            await f.Answer.Quiescence;
            f.Answer.ReviewText.Should().BeEmpty();
            f.Answer.CanAccept.Should().BeFalse();
            f.Model.Answer.Text.Should().Be("keep");
            f.Model.Completion.IsCompleted.Should().BeFalse();
            (await f.Data.Store.ReadQuestionsAsync(f.Model.Key.Request.SessionId, f.Data.Token)).Single().Draft.Should().BeNull();
            f.Reader.Calls.Should().Be(1);
        });
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("typing")]
    [InlineData("owner")]
    [InlineData("expiry")]
    [InlineData("revision")]
    [InlineData("grant")]
    public async Task ChangesAfterCaptureNeverSubmitOrRetargetTheReviewedSnapshot(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await Fixture.Create();
            f.Model.Edit(new([], "previous"));
            await f.Capture(grant: true);
            f.Window.FindControl<CheckBox>("ReplaceManualDraft")!.IsChecked = true;
            await f.Answer.AcceptAsync();
            await WhenEditable(f.Model);
            f.Model.Answer.Text.Should().Be(f.Reader.Text);
            switch (change)
            {
                case "cancel": f.Answer.Clear(); break;
                case "typing": f.Model.Edit(new([], "new typing")); break;
                case "owner": f.Eligible = false; f.Answer.Refresh(); break;
                case "expiry": f.Data.Time.Now = f.Model.Question.ExpiresAt; f.Answer.Refresh(); break;
                case "revision":
                    await f.Data.RunAsync(() => f.Data.Questions.ReviseAsync(f.Model.Key, f.Model.Question.Spec,
                        f.Model.Question.ExpiresAt, f.Data.Token));
                    await f.Model.SubmitAsync();
                    break;
                case "grant":
                    var grant = (await f.Data.Store.ReadGrantsAsync(f.Data.Token)).Single();
                    using (var activity = HostActivity.BeginRoot(grant.ApprovedProposal.Request,
                        HostActivityLayer.Application, HostOperation.Policy))
                    {
                        (await f.Data.Authorization.RevokeAsync(grant.ApprovedProposal.Request, grant.Id,
                            grant.Revision, f.Data.Token)).Outcome.Should().Be(HostInteractionOutcome.Revoked);
                    }
                    await f.Model.SubmitAsync();
                    break;
            }
            await f.Answer.Quiescence;
            f.Answer.ReviewText.Should().BeEmpty();
            f.Reader.Calls.Should().Be(1);
            var canonical = (await f.Data.Store.ReadQuestionsAsync(f.Model.Key.Request.SessionId, f.Data.Token)).Single();
            canonical.Status.Should().Be(QuestionStatus.Pending);
            canonical.Draft.Should().BeNull();
            if (change is "cancel" or "grant") { f.Model.Answer.Text.Should().Be("previous"); }
            if (change is "typing") { f.Model.Answer.Text.Should().Be("new typing"); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateNativeReadMustPhysicallyJoinAndCannotReplaceNewTyping(bool close)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await Fixture.Create();
            f.Reader.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var capture = f.Capture(grant: true, waitForCapture: false);
            await f.Reader.Started.Task.WaitAsync(f.Data.Token);
            f.Model.Edit(new([], "new manual typing"));
            if (close) { f.Window.Close(); }
            f.Answer.Quiescence.IsCompleted.Should().BeFalse();
            f.Reader.Pending.SetResult();
            await capture;
            await f.Answer.Quiescence;
            f.Reader.Released.Should().BeTrue();
            f.Answer.ReviewText.Should().BeEmpty();
            if (!close) { f.Model.Answer.Text.Should().Be("new manual typing"); }
            (await f.Data.Store.ReadQuestionsAsync(f.Model.Key.Request.SessionId, f.Data.Token)).Single().Draft.Should().BeNull();
        });
    }

    internal static async Task DecidePermission(QuestionWindow permission, bool grant, Action? beforeSubmit = null)
    {
        var review = permission.FindControl<Button>("ReviewQuestion")!;
        await WhenEnabled(review);
        if (!grant) { permission.FindControl<Button>("CancelQuestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); return; }
        ((RadioButton)permission.FindControl<StackPanel>("ChoicePanel")!.Children[0]).IsChecked = true;
        review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var submit = permission.FindControl<Button>("SubmitAnswer")!;
        await WhenEnabled(submit);
        beforeSubmit?.Invoke();
        submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    internal static async Task WhenEnabled(Control control)
    {
        if (control.IsEnabled) { return; }
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (control.IsEnabled) { ready.TrySetResult(); }
        }

        control.PropertyChanged += Changed;
        try { if (control.IsEnabled) { ready.TrySetResult(); } await ready.Task.WaitAsync(TestContext.Current.CancellationToken); }
        finally { control.PropertyChanged -= Changed; }
    }

    internal static async Task Activate(Window window)
    {
        var active = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (window.IsActive) { active.TrySetResult(); }
        }
        window.PropertyChanged += Changed;
        try
        {
            window.Activate();
            if (window.IsActive) { active.TrySetResult(); }
            await active.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally { window.PropertyChanged -= Changed; }
    }

    internal static async Task WhenEditable(NativeQuestionViewModel model)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (model.IsEditable && !model.HasPendingOperation) { ready.TrySetResult(); }
        }
        model.PropertyChanged += Changed;
        try
        {
            Changed(null, new(null));
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally { model.PropertyChanged -= Changed; }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal InteractionStorageFixture Data { get; } = new();
        internal Reader Reader { get; } = new();
        internal NativeQuestionViewModel Model { get; private set; } = null!;
        internal QuestionClipboardAnswer Answer { get; private set; } = null!;
        internal QuestionWindow Window { get; private set; } = null!;
        internal bool Eligible { get; set; } = true;
        internal long AdmissionRevision { get; set; } = 1;
        internal long PrivateRevision { get; set; } = 1;
        private ClipboardExplanationAdmission admission = null!;

        internal static async Task<Fixture> Create(string purpose = LocalModelQuestionPolicy.Purpose)
        {
            var f = new Fixture();
            await f.Data.InitializeAsync();
            f.Data.Time.UseFrozenTimers = true;
            var spec = new QuestionSpec("Describe the required format", QuestionKind.Text, [],
                maximumTextLength: LocalModelQuestion.MaximumFreeTextLength, purpose: purpose, sourceId: LocalModelQuestionPolicy.SourceId);
            var record = (await f.Data.RunAsync(() => f.Data.Questions.CreateAsync(f.Data.Request, spec,
                f.Data.Time.Now.AddMinutes(2), f.Data.Token))).Question!;
            f.Model = new(record, f.Data.Questions, f.Data.Authorization, new(f.Data.Store, f.Data.Time),
                () => f.Eligible, f.Data.Time, NullLogger<NativeQuestionViewModel>.Instance);
            f.admission = new(f.Data.Store, f.Data.Store, new(f.Data.Tasks));
            var renderer = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance);
            f.Answer = new(f.Model, f.Reader, f.admission, f.Data.Store, () => () => f.Eligible,
                () => new(1, Kora.Core.Communication.CallState.Clear, false, Kora.Core.Communication.CallAwareSettings.Default),
                () => f.AdmissionRevision, () => f.PrivateRevision,
                f.Data.Time, NullLogger<ClipboardSnapshotBroker>.Instance, NullLogger<NativeQuestionViewModel>.Instance, renderer);
            f.Model.BindClipboardAnswer(f.Answer);
            f.Window = new(f.Model, renderer);
            f.Window.Show();
            await Activate(f.Window);
            await WhenEnabled(f.Window.FindControl<Button>("ReviewQuestion")!);
            return f;
        }

        internal async Task Capture(bool grant, bool waitForCapture = true, Action? beforePermissionSubmit = null)
        {
            Answer.CanCapture.Should().BeTrue("active={0}, editable={1}, status={2}", Window.IsActive, Model.IsEditable, Model.Status);
            var opened = new TaskCompletionSource<QuestionWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var observer = Avalonia.Controls.Window.WindowOpenedEvent.AddClassHandler<QuestionWindow>((window, _) => opened.TrySetResult(window));
            var capture = Answer.CaptureAsync();
            await Task.WhenAny(opened.Task, capture);
            opened.Task.IsCompleted.Should().BeTrue("capture ended before any permission presentation: {0}", Model.Status);
            var permission = await opened.Task.WaitAsync(Data.Token);
            Reader.Calls.Should().Be(0);
            await DecidePermission(permission, grant, beforePermissionSubmit);
            if (waitForCapture)
            {
                await capture;
                await Activate(Window);
                await WhenEditable(Model);
            }
        }

        public async ValueTask DisposeAsync()
        {
            Answer.Dispose();
            Reader.Pending?.TrySetResult();
            await Answer.Quiescence;
            Window.Close();
            await Model.OperationQuiescence;
            await admission.DisposeAsync();
            Data.Dispose();
        }
    }

    internal sealed class Reader : IPlainTextClipboardReader
    {
        internal string Text { get; set; } = "  complete exact clipboard text e\u0301\r\nending  ";
        internal ClipboardOutcome Outcome { get; set; } = ClipboardOutcome.Captured;
        internal int Calls { get; private set; }
        internal bool Released { get; private set; }
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource? Pending { get; set; }
        public async Task<ClipboardReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            Calls++;
            Started.TrySetResult();
            if (Pending is { } pending) { await pending.Task; }
            Released = true;
            return new(Outcome, Text, 17);
        }
    }
}

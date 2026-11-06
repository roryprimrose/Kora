using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComposerKeyboardSubmitUsesCurrentExactSessionAndExistingButtonOnce(bool compact)
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
            var adapter = new OriginalRequestAdapter();
            await using var composition = new Composition(f, adapter: adapter);
            var model = composition.Model;
            var owner = new SessionsPresentationOwner(model);
            Window window = compact ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
                model.ComposerBody = "other topic remains unsent";
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != original));
                const string body = "  exact keyboard message e\u0301\U0001f642\r\nend  ";
                var editor = window.FindControl<TextBox>("ConversationComposer")!;
                editor.Text = body;
                var clicks = 0;
                window.FindControl<Button>("SubmitConversation")!.Click += (_, _) => clicks++;
                editor.Focus().Should().BeTrue();
                window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, "\r");
                window.KeyRelease(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, "\r");
                await KeyboardSubmitOperation(window);
                clicks.Should().Be(1);
                adapter.Calls.Should().Be(1);
                var message = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
                message.Body.Should().Be(body);
                message.Request.SessionId.Should().Be(f.Request.SessionId);
                message.Request.Origin.Should().Be(Kora.Core.Hosting.RequestOrigin.LocalUi);
                message.AssistantResponse!.Body.Should().Be("Genuine local original-request answer");
                model.ComposerBody.Should().BeEmpty();
                window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, "\r");
                clicks.Should().Be(1, "an empty composer cannot dispatch a repeated key");
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
                model.ComposerBody.Should().Be("other topic remains unsent");
                (await f.Store.ReadConversationPageAsync(original, null, 25, f.Token)).Messages.Should().BeEmpty();
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                WindowsSqliteConversationTests.AuditText(f).Should().NotContain(body);
            }
            finally { owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComposerKeyboardSubmitLeavesNewlinesOtherModifiersAndOtherFieldsUnchanged(bool compact)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composition = new Composition(f);
            var model = composition.Model;
            var owner = new SessionsPresentationOwner(model);
            Window window = compact ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                var editor = window.FindControl<TextBox>("ConversationComposer")!;
                var clicks = 0;
                window.FindControl<Button>("SubmitConversation")!.Click += (_, _) => clicks++;
                foreach (var modifiers in new[] { RawInputModifiers.None, RawInputModifiers.Shift,
                    RawInputModifiers.Alt, RawInputModifiers.Meta, RawInputModifiers.Control | RawInputModifiers.Shift,
                    RawInputModifiers.Control | RawInputModifiers.Alt, RawInputModifiers.Control | RawInputModifiers.Meta })
                {
                    editor.Text = "multiline";
                    editor.CaretIndex = editor.Text.Length;
                    editor.Focus().Should().BeTrue();
                    window.KeyPress(Key.Enter, modifiers, PhysicalKey.Enter, "\r");
                    window.KeyRelease(Key.Enter, modifiers, PhysicalKey.Enter, "\r");
                    if (modifiers == RawInputModifiers.None)
                    { editor.Text.Should().Be("multiline" + Environment.NewLine); }
                    clicks.Should().Be(0);
                }
                editor.Text = "still unsent";
                window.FindControl<TextBox>("ConversationQuery")!.Focus().Should().BeTrue();
                window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, "\r");
                window.KeyRelease(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, "\r");
                await KeyboardSubmitOperation(window);
                clicks.Should().Be(0);
                model.ComposerBody.Should().Be("still unsent");
                f.Count("conversation_messages").Should().Be(0);
                f.Count("conversation_drafts").Should().Be(0);
            }
            finally { owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData(false, "denied")]
    [InlineData(true, "denied")]
    [InlineData(false, "private")]
    [InlineData(true, "private")]
    [InlineData(false, "failure")]
    [InlineData(true, "failure")]
    [InlineData(false, "cancel")]
    [InlineData(true, "cancel")]
    public async Task ComposerKeyboardSubmitCannotBypassPendingOrChangedAdmissionAndPreservesInput(bool compact, string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var checkpoint = new NewRequestCheckpoint { Action = "conversation.submit" };
            f.Reopen(checkpoint);
            await f.Store.InitializeAsync(f.Token);
            var adapter = new OriginalRequestAdapter();
            await using var composition = new Composition(f, adapter: adapter);
            var model = composition.Model;
            var owner = new SessionsPresentationOwner(model);
            Window window = compact ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                const string body = "unsent through refused admission";
                var editor = window.FindControl<TextBox>("ConversationComposer")!;
                editor.Text = body;
                var clicks = 0;
                window.FindControl<Button>("SubmitConversation")!.Click += (_, _) => clicks++;
                checkpoint.Commit = () => Dispatcher.UIThread.Invoke(() =>
                {
                    model.CanSubmitConversation.Should().BeFalse();
                    editor.RaiseEvent(new KeyEventArgs
                    {
                        RoutedEvent = InputElement.KeyDownEvent,
                        Key = Key.Enter,
                        KeyModifiers = KeyModifiers.Control,
                    });
                    if (change is "denied") { composition.Access.CanControl = false; }
                    if (change is "private") { composition.Access.ControlRevision++; }
                    if (change is "failure") { throw new IOException("Synthetic keyboard admission failure."); }
                    if (change is "cancel") { throw new OperationCanceledException("Synthetic keyboard admission cancellation."); }
                });
                editor.Focus().Should().BeTrue();
                window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, "\r");
                await KeyboardSubmitOperation(window);
                checkpoint.Calls.Should().Be(1);
                clicks.Should().Be(1, "pending admission uses the existing in-progress fence");
                adapter.Calls.Should().Be(0);
                f.Count("conversation_messages").Should().Be(0);
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                model.ComposerBody.Should().Be(change is "private" ? string.Empty : body,
                    "ordinary refusal preserves input, but private-control retirement must clear it");
                WindowsSqliteConversationTests.AuditText(f).Should().NotContain(body);
                if (change is "failure" or "cancel")
                { model.ConversationStatus.Should().Contain("unconfirmed"); }
            }
            finally { checkpoint.Commit = null; owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComposerKeyboardSubmitRefusesStaleCurrentSessionWithoutResumingOrLosingInput(bool compact)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new OriginalRequestAdapter();
            await using var composition = new Composition(f, adapter: adapter);
            var model = composition.Model;
            var owner = new SessionsPresentationOwner(model);
            Window window = compact ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                var target = model.SelectedSessionRecord!;
                var editor = window.FindControl<TextBox>("ConversationComposer")!;
                editor.Text = "preserved stale-target input";
                var retired = await composition.Workspace.ChangeLifecycleAsync(target.Authority.SessionId,
                    target.Authority.Generation, false, Kora.Core.Hosting.RequestOrigin.LocalUi, f.Token);
                retired.IsActive.Should().BeFalse();
                model.CanSubmitConversation.Should().BeTrue("the UI has not refreshed its stale metadata");
                editor.Focus().Should().BeTrue();
                window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, "\r");
                await KeyboardSubmitOperation(window);
                adapter.Calls.Should().Be(0);
                model.ComposerBody.Should().Be("preserved stale-target input");
                model.ConversationStatus.Should().Contain("unconfirmed");
                f.Count("conversation_messages").Should().Be(0);
                f.Count("conversation_drafts").Should().Be(0);
                (await f.Store.ReadSessionAsync(target.Authority.SessionId, f.Token))!.Should().Be(retired);
            }
            finally { owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComposerKeyboardSubmitPreservesSeparateReplyReviewAndCancel(bool compact)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new OriginalRequestAdapter();
            await using var composition = new Composition(f, adapter: adapter);
            var model = composition.Model;
            var owner = new SessionsPresentationOwner(model);
            Window window = compact ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.ComposerBody = "first question";
                window.FindControl<Button>("SubmitConversation")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await KeyboardSubmitOperation(window);
                await model.ReadConversationAsync();
                await model.ReplyToAnswerAsync(model.ConversationMessages.Single());
                var editor = window.FindControl<TextBox>("ConversationComposer")!;
                editor.Text = "follow-up remains unsent";
                editor.Focus().Should().BeTrue();
                window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, "\r");
                await KeyboardSubmitOperation(window);
                model.CanConfirmAssistantReply.Should().BeTrue(model.ConversationStatus);
                model.ReplyReviewText.Should().Contain("follow-up remains unsent");
                adapter.Calls.Should().Be(1, "the shortcut must not bypass the separate reply confirmation");
                f.Count("conversation_messages").Should().Be(1);
                model.CancelAssistantReply();
                model.ComposerBody.Should().Be("follow-up remains unsent");
                model.CanConfirmAssistantReply.Should().BeFalse();
            }
            finally { owner.Revoke(); }
        });
    }

    private static Task KeyboardSubmitOperation(Window window) => window switch
    {
        SessionsWindow sessions => sessions.ConversationOperation,
        CompactSessionWindow compact => compact.ConversationOperation,
        _ => throw new ArgumentException("Expected a session composer window.", nameof(window)),
    };
}

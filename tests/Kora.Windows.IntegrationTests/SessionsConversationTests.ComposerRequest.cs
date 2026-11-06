using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using AwesomeAssertions;

using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("sessions", "answer")]
    [InlineData("compact", "answer")]
    [InlineData("interaction", "answer")]
    [InlineData("sessions", "unavailable")]
    [InlineData("compact", "cancel")]
    [InlineData("interaction", "cancel")]
    public async Task NativeReviewedComposerCompletesOriginalSubmitOrCancelWithoutSavingInput(string surface, string decision)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new OriginalRequestAdapter();
            await using var composed = new Composition(f, adapter: decision is "unavailable" ? null : adapter);
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            var window = surface switch
            {
                "sessions" => new SessionsWindow(model, owner),
                "compact" => (Window)new CompactSessionWindow(model, owner),
                _ => new Window { Content = new SessionInteractionView { DataContext = model } },
            };
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                const string body = "  reviewed original e\u0301🙂\r\nend  ";
                model.ComposerBody = body;
                var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                var audit = WindowsSqliteConversationTests.AuditText(f);
                var panel = window.GetLogicalDescendants().OfType<ComposerRequestReviewPanel>().Single();
                Button FindButton(string name) => panel.GetLogicalDescendants().OfType<Button>()
                    .Single(item => string.Equals(item.Name, name, StringComparison.Ordinal));
                FindButton("ReviewComposerRequest").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await panel.Operation;
                model.ComposerRequestReviewText.Should().Contain(body).And.Contain("LocalUi").And.Contain("nothing submitted");
                model.CanConfirmComposerRequest.Should().BeTrue(model.ConversationStatus);
                model.ComposerBody.Should().Be(body);
                f.Count("conversation_messages").Should().Be(0);
                adapter.Calls.Should().Be(0);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);

                FindButton(decision is "cancel" ? "CancelComposerRequest" : "ConfirmComposerRequest")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await panel.Operation;
                model.CanConfirmComposerRequest.Should().BeFalse();
                model.ComposerRequestReviewText.Should().BeEmpty();
                if (decision is "cancel")
                {
                    model.ComposerBody.Should().Be(body);
                    model.ConversationStatus.Should().Contain("cancelled");
                    adapter.Calls.Should().Be(0);
                    f.Count("conversation_messages").Should().Be(0);
                    WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
                }
                else
                {
                    model.ComposerBody.Should().BeEmpty();
                    var message = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
                    message.Body.Should().Be(body);
                    message.Request.Origin.Should().Be(Kora.Core.Hosting.RequestOrigin.LocalUi);
                    if (decision is "answer")
                    {
                        message.AssistantResponse!.Body.Should().Be("Genuine local original-request answer");
                        adapter.Calls.Should().Be(1);
                        model.ConversationStatus.Should().Contain("Succeeded");
                    }
                    else
                    {
                        message.AssistantResponse.Should().BeNull();
                        model.ConversationStatus.Should().Contain("Unavailable").And.Contain("UnregisteredModel");
                    }
                    await model.CaptureComposerRequestDecision(true)();
                    f.Count("conversation_messages").Should().Be(1, "a consumed review cannot be replayed");
                }
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                WindowsSqliteConversationTests.AuditText(f).Should().NotContain(body);
                composed.Turns.IsQuiescent.Should().BeTrue();
            }
            finally { window.Close(); owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData("switch")]
    [InlineData("input")]
    [InlineData("replacement")]
    [InlineData("expiry")]
    [InlineData("private")]
    [InlineData("direct")]
    public async Task ReviewedComposerCannotConfirmAnOldBufferOrRetargetAnotherConversation(string change)
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
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
            const string body = "original unsent reviewed input";
            model.ComposerBody = body;
            await model.ReviewComposerRequestAsync();
            model.CanConfirmComposerRequest.Should().BeTrue(model.ConversationStatus);
            var confirm = model.CaptureComposerRequestDecision(true);
            var cancel = model.CaptureComposerRequestDecision(false);
            if (change is "switch")
            {
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != original));
                model.ComposerBody = "other independent unsent input";
            }
            if (change is "input") { model.ComposerBody = body; }
            if (change is "replacement") { await model.ReviewComposerRequestAsync(); await cancel(); }
            if (change is "expiry") { f.Time.Now = f.Time.Now.AddSeconds(60); }
            if (change is "private") { model.Close(); }
            if (change is "direct") { await model.SubmitConversationAsync(); }
            await confirm();
            adapter.Calls.Should().Be(change is "direct" ? 1 : 0);
            f.Count("conversation_messages").Should().Be(change is "direct" ? 1 : 0);
            if (change is "replacement") { model.CanConfirmComposerRequest.Should().BeTrue("the old decision cannot consume its replacement"); }
            if (change is "switch")
            {
                model.ComposerBody.Should().Be("other independent unsent input");
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
                model.ComposerBody.Should().Be(body);
                model.CanConfirmComposerRequest.Should().BeFalse("returning does not renew the retired review");
            }
            else { model.ComposerBody.Should().Be(change is "private" or "direct" ? string.Empty : body); }
            f.Count("conversation_drafts").Should().Be(0);
            f.Count("host_questions").Should().Be(0);
            f.Count("scoped_grants").Should().Be(0);
            composed.Turns.IsQuiescent.Should().BeTrue();
            model.Close();
        });
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("selection")]
    [InlineData("failure")]
    public async Task AwaitedComposerReviewRetiresLateResultsAndKeepsOriginalUnsentInput(string decision)
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
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reader = new SearchReader(f.Store.ReadConversationPageAsync, f.Store.ReadConversationDraftAsync,
                async (session, token) =>
                {
                    var value = await f.Store.ReadMetadataAsync(session, token);
                    entered.TrySetResult();
                    await release.Task;
                    if (decision is "failure") { throw new IOException("Synthetic composer source refusal."); }
                    return value;
                });
            await using var composed = new Composition(f, conversationReader: reader, adapter: new OriginalRequestAdapter());
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
                model.ComposerBody = "input retained through awaited review";
                var operation = model.ReviewComposerRequestAsync();
                await entered.Task.WaitAsync(f.Token);
                model.CanCancelComposerRequest.Should().BeTrue("review preparation has an explicit cancellation route");
                if (decision is "cancel") { await model.CaptureComposerRequestDecision(false)(); }
                if (decision is "selection")
                { await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != original)); }
                release.TrySetResult();
                await operation;
                model.CanConfirmComposerRequest.Should().BeFalse();
                model.ComposerRequestReviewText.Should().BeEmpty();
                model.ComposerBody.Should().Be("input retained through awaited review");
                model.CanCompose.Should().BeTrue("no request was accepted and no uncertain submission was manufactured");
                f.Count("conversation_messages").Should().Be(0);
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                composed.Turns.IsQuiescent.Should().BeTrue();
            }
            finally { release.TrySetResult(); window.ClosePrivate(); }
        });
    }

    [Fact]
    public async Task NativeReviewedComposerCapturesKeyboardInputBeforeTheQueuedReviewAction()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.ComposerBody = "first input";
                var panel = window.GetLogicalDescendants().OfType<ComposerRequestReviewPanel>().Single();
                var review = panel.GetLogicalDescendants().OfType<Button>().Single(item => item.Name is "ReviewComposerRequest");
                review.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
                model.ComposerBody = "new input";
                review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await panel.Operation;
                model.CanConfirmComposerRequest.Should().BeFalse();
                model.ComposerRequestReviewText.Should().BeEmpty();
                model.ComposerBody.Should().Be("new input");
                f.Count("conversation_messages").Should().Be(0);
            }
            finally { window.ClosePrivate(); }
        });
    }
}

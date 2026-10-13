using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task NativeSelectedConversationSearchKeepsDirtyComposerSavedRevisionAndHostAuthorityIndependent()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var original = await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "actual retained café\r\n e\u0301🙂");
            await using var composed = new Composition(f);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                model.ComposerBody = "memory-only dirty buffer";
                var audit = WindowsSqliteConversationTests.AuditText(f);
                window.FindControl<TextBox>("ConversationQuery")!.Text = "CAFE\u0301 café";
                window.FindControl<Button>("SearchConversation")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.ConversationOperation;
                model.ConversationSearchMessages.Single().Should().Be(original.Message);
                model.ComposerBody.Should().Be("memory-only dirty buffer");
                model.ComposerDirty.Should().BeTrue();
                model.ConversationMessages.Single().Should().Be(original.Message);
                model.CanNextConversationSearch.Should().BeFalse();
                model.ConversationSearchStatus.Should().Contain("scanned 1").And.Contain("omitted 0").And.Contain("unavailable 0");
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
                (await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token)).Draft.Revision.Should().Be(0);
                window.FindControl<TextBox>("ConversationQuery")!.Text = "%%%";
                model.ConversationSearchMessages.Should().BeEmpty();
                window.FindControl<Button>("SearchConversation")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.ConversationOperation;
                model.ConversationSearchStatus.Should().Contain("InvalidQuery");
                model.ConversationSearchMessages.Should().BeEmpty();
                model.ComposerBody.Should().Be("memory-only dirty buffer");
                window.FindControl<Button>("ClearConversationSearch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.ConversationQuery.Should().BeEmpty();
                model.ComposerDirty.Should().BeTrue();
            }
            finally { window.ClosePrivate(); }
            model.ConversationQuery.Should().BeEmpty();
            model.ConversationSearchMessages.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task SearchClearPrivacyAndLateCompletionRevokeOnlySearchStateUntilPrivateClose()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "retained marker");
        await using var composed = new Composition(f);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.ComposerBody = "dirty independent";
        model.ConversationQuery = "marker";
        var pending = model.SearchConversationAsync();
        model.ClearConversationSearch();
        await pending;
        model.ConversationSearchMessages.Should().BeEmpty();
        model.ConversationQuery.Should().BeEmpty();
        model.ComposerBody.Should().Be("dirty independent");
        model.ConversationQuery = "marker";
        pending = model.SearchConversationAsync();
        model.Close();
        await pending;
        model.ConversationSearchMessages.Should().BeEmpty();
        model.ConversationQuery.Should().BeEmpty();
        model.CanSearchConversation.Should().BeFalse();
        f.Count("conversation_messages").Should().Be(1);
    }
}

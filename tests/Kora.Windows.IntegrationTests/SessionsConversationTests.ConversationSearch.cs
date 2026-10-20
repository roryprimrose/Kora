using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactCommandSearchUsesPrivateSqliteNativeActionWithoutDirtyNavigationOrActivityRenewal(bool done)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var firstSession = f.Request.SessionId;
            await WindowsSqliteConversationTests.Submit(f, firstSession, "other session not searched");
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            var exact = await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "original marker café\r\n e\u0301");
            await WindowsSqliteConversationTests.Save(f, await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "draft excluded");
            await using var composed = new Composition(f);
            if (done)
            {
                await composed.Workspace.ChangeLifecycleAsync(f.Request.SessionId, new(1), false, RequestOrigin.LocalUi, f.Token);
            }
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == firstSession));
                await model.ReadConversationAsync();
                model.ComposerBody = "dirty exact original session";
                var decisions = 0;
                model.DecideComposerDraft = () =>
                {
                    decisions++;
                    return Task.FromResult(ComposerDraftDecision.Discard);
                };
                var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                var audit = WindowsSqliteConversationTests.AuditText(f);
                var service = new ConversationSearchCommandService(composed.Workspace, composed.Conversation, composed.Access, f.Time);
                var command = SessionCommand.Parse("conversation search " + f.Request.SessionId.Value.ToString("D") + " --   CAFE\u0301  ", "Kora")!;
                var page = (await service.ExecuteAsync(command, RequestOrigin.LocalUi, static () => true, f.Token))!;
                page.Messages.Should().Equal(exact.Message);
                page.Session.IsActive.Should().Be(!done);
                ConversationSearchPage.Serialize(page).Length.Should().BeLessThanOrEqualTo(ConversationText.MaximumPageUtf8Bytes);
                model.SelectedSessionRecord!.Authority.SessionId.Should().Be(firstSession);
                model.ComposerBody.Should().Be("dirty exact original session");
                model.ComposerDirty.Should().BeTrue();
                decisions.Should().Be(0);
                (await f.Store.ReadConversationDraftAsync(firstSession, f.Token)).Draft.Revision.Should().Be(0);
                (await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token)).Draft.Body.Should().Be("draft excluded");
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
            }
            finally { window.ClosePrivate(); }
        });
    }

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

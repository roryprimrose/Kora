using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    private static async Task<ConversationMessage> RetainSearchAnswer(InteractionStorageFixture f, string answer)
    {
        var request = WindowsSqliteConversationTests.Fresh(f.Request.SessionId);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RetainConversationMessageAsync(request, new(1), 0, "Original USER only", static () => true, f.Token);
        await f.Store.BeginConversationExecutionAsync(request, provenance, 0, static () => true, f.Token);
        var receipt = await f.Store.CompleteConversationExecutionAsync(request,
            new(ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome, provenance, new(answer, null)),
            static () => true, f.Token);
        return receipt.Message;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeAssistantSearchFindsAndInspectsCompleteInertRetainedAnswersAfterRestartWithoutRenewal(bool done)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            const string answer = "  exact saved café e\u0301\r\n<script>inert</script>  ";
            var messages = new List<ConversationMessage>();
            for (var index = 0; index < 26; index++) { messages.Add(await RetainSearchAnswer(f, answer)); }
            f.Reopen();
            await f.Store.InitializeAsync(f.Token);
            var opened = new List<AdmittedDetailContent>();
            Func<bool>? detailLive = null;
            var revoked = new List<HostId<SessionIdentity>>();
            await using var composed = new Composition(f, async (content, live, resolve, token) =>
            {
                live().Should().BeTrue();
                (await resolve(token)).IsSameSnapshot(content).Should().BeTrue();
                opened.Add(content);
                detailLive = live;
                return "Complete retained assistant detail opened";
            }, revokeDetails: revoked.Add);
            if (done)
            {
                await composed.Workspace.ChangeLifecycleAsync(f.Request.SessionId, new(1), false, RequestOrigin.LocalUi, f.Token);
            }
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            var window = new SessionsWindow(model, owner);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.ComposerBody = "independent unsent input";
                var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                var audit = WindowsSqliteConversationTests.AuditText(f);
                var query = window.FindControl<TextBox>("ConversationQuery")!;
                var roles = window.FindControl<ComboBox>("ConversationRole")!;
                roles.Items.Cast<ConversationSearchRole>().Should().Contain(ConversationSearchRole.ASSISTANT);
                query.Text = "CAFE\u0301";
                window.FindControl<Button>("SearchConversation")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.ConversationOperation;
                model.ConversationSearchMessages.Should().BeEmpty("All remains USER/HOST only");
                roles.SelectedItem = ConversationSearchRole.ASSISTANT;
                model.ConversationSearchMessages.Should().BeEmpty("role edits do not read");
                window.FindControl<Button>("SearchConversation")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.ConversationOperation;
                model.ConversationSearchMessages.Should().HaveCount(25);
                model.ConversationSearchMessages.Select(message => message.AssistantResponse!.Body).Should().OnlyContain(body => body == answer);
                model.CanNextConversationSearch.Should().BeTrue();
                var resultList = window.FindControl<ListBox>("ConversationSearchResults")!;
                resultList.Items.Cast<ConversationMessage>().Should().Equal(model.ConversationSearchMessages);
                await model.OpenConversationDetailAsync(resultList.Items.Cast<ConversationMessage>().First());
                opened.Should().ContainSingle();
                opened[0].ConversationMessage!.AssistantResponse!.Body.Should().Be(answer);
                detailLive!().Should().BeTrue();
                window.FindControl<Button>("NextConversationSearch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.ConversationOperation;
                model.ConversationSearchMessages.Single().Request.Should().Be(messages[25].Request);
                model.CanNextConversationSearch.Should().BeFalse();
                detailLive().Should().BeFalse();
                revoked.Should().Contain(f.Request.SessionId);
                window.FindControl<Button>("ClearConversationSearch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.ConversationSearchMessages.Should().BeEmpty();
                model.ConversationQuery.Should().BeEmpty();
                model.ConversationRole.Should().Be(ConversationSearchRole.All);
                model.ComposerBody.Should().Be("independent unsent input");
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
                f.Count("conversation_messages").Should().Be(26);
                f.Count("host_questions").Should().Be(0);
                model.SelectedSessionRecord!.Authority.IsActive.Should().Be(!done);
            }
            finally { window.ClosePrivate(); }
            model.ConversationSearchMessages.Should().BeEmpty();
        });
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task ExactAssistantCommandUsesPrivateSqliteAndCompleteAnswerWithoutSelectingOrSubmitting(RequestOrigin origin)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var expected = await RetainSearchAnswer(f, "complete retained answer café");
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        await using var composed = new Composition(f);
        var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
        var audit = WindowsSqliteConversationTests.AuditText(f);
        var service = new ConversationSearchCommandService(composed.Workspace, composed.Conversation, composed.Access, f.Time);
        var command = SessionCommand.Parse((origin == RequestOrigin.ActivatedVoice ? "Nova, " : "")
            + "conversation search " + f.Request.SessionId.Value.ToString("D") + " --role ASSISTANT --   CAFE\u0301  ", "Nova")!;
        var page = (await service.ExecuteAsync(command, origin, static () => true, f.Token))!;
        page.Messages.Should().Equal(expected);
        page.Role.Should().Be(ConversationSearchRole.ASSISTANT);
        page.HeadRevision.Should().Be(3);
        ConversationSearchPage.Serialize(page).Length.Should().BeLessThanOrEqualTo(ConversationText.MaximumPageUtf8Bytes);
        composed.Model.SelectedSessionRecord.Should().BeNull();
        (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
        WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
        f.Count("conversation_messages").Should().Be(1);
    }

    [Theory]
    [InlineData("assistant")]
    [InlineData("user")]
    public async Task PrivateAssistantSearchRefusesTamperedNonmatchingCompleteRecord(string field)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await RetainSearchAnswer(f, "saved complete answer");
        f.Mutate(field is "assistant"
            ? "UPDATE conversation_messages SET payload=json_set(payload,'$.AssistantResponse.Body','substituted');"
            : "UPDATE conversation_messages SET payload=json_set(payload,'$.Body','substituted');");
        await using var composed = new Composition(f);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        await composed.Workspace.Invoking(value => value.SearchConversationAsync(f.Request.SessionId, "absent", null, 25,
            f.Token, ConversationSearchRole.ASSISTANT)).Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("role")]
    [InlineData("private")]
    public async Task NativeAssistantSearchCancelsOrRevokesLateAnswerWithoutChangingUnsentInput(string retirement)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await RetainSearchAnswer(f, "saved answer marker");
            var snapshot = await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 50, f.Token);
            var release = new TaskCompletionSource<ConversationPage>();
            CancellationToken observed = default;
            var reader = new SearchReader((_, _, _, token) =>
            {
                observed = token;
                return new(release.Task);
            });
            await using var composed = new Composition(f, conversationReader: reader);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.ComposerBody = "independent input";
                window.FindControl<ComboBox>("ConversationRole")!.SelectedItem = ConversationSearchRole.ASSISTANT;
                window.FindControl<TextBox>("ConversationQuery")!.Text = "marker";
                window.FindControl<Button>("SearchConversation")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var pending = window.ConversationOperation;
                pending.IsCompleted.Should().BeFalse();
                if (retirement is "cancel")
                { window.FindControl<Button>("CancelConversationSearch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                if (retirement is "role")
                { window.FindControl<ComboBox>("ConversationRole")!.SelectedItem = ConversationSearchRole.USER; }
                if (retirement is "private") { composed.Access.CanInspect = false; }
                if (retirement is not "private") { observed.IsCancellationRequested.Should().BeTrue(); }
                release.SetResult(snapshot);
                await pending;
                model.ConversationSearchMessages.Should().BeEmpty();
                model.CanNextConversationSearch.Should().BeFalse();
                model.CanCancelConversationSearch.Should().BeFalse();
                if (retirement is not "private") { model.ComposerBody.Should().Be("independent input"); }
            }
            finally { window.ClosePrivate(); }
        });
    }
}

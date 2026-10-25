using System.Collections.Immutable;

using Avalonia.Automation;
using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task NativeRoleControlsShareExactFieldsAndRevokeSelectionsContinuationWithoutReading()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "marker Denied");
            var snapshot = await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 50, f.Token);
            var messages = Enumerable.Range(1, 26).Select(index => snapshot.Messages[0] with { Sequence = index }).ToImmutableArray();
            var reader = new SearchReader((_, cursor, limit, _) =>
            {
                var after = cursor?.After ?? 0;
                var rows = messages.Where(message => message.Sequence > after).Take(limit).ToImmutableArray();
                return ValueTask.FromResult(snapshot with { Messages = rows, Ceiling = 26, HeadRevision = 52 });
            });
            await using var composed = new Composition(f, conversationReader: reader);
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            var workspace = new SessionsWindow(model, owner);
            var compact = new CompactSessionWindow(model, owner);
            workspace.Show();
            compact.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.ComposerBody = "independent dirty buffer";
                var roles = workspace.FindControl<ComboBox>("ConversationRole")!;
                var sharedRoles = compact.FindControl<ComboBox>("ConversationRole")!;
                roles.ItemsSource!.Cast<ConversationSearchRole>().Should().Equal(
                    ConversationSearchRole.All, ConversationSearchRole.USER, ConversationSearchRole.HOST,
                    ConversationSearchRole.ASSISTANT);
                AutomationProperties.GetName(roles).Should().Contain("USER original body only").And.Contain("HOST outcome")
                    .And.Contain("ASSISTANT retained answer body only");
                AutomationProperties.GetName(sharedRoles).Should().Contain("Shared").And.Contain("editing does not read");
                roles.Focusable.Should().BeTrue();
                sharedRoles.Focusable.Should().BeTrue();
                model.ConversationQuery = "Denied";
                roles.SelectedItem = ConversationSearchRole.USER;
                reader.Reads.Should().Be(0);
                sharedRoles.SelectedItem.Should().Be(ConversationSearchRole.USER);
                Click(compact, "SearchConversation");
                await compact.ConversationOperation;
                model.ConversationSearchMessages.Should().HaveCount(25);
                model.CanNextConversationSearch.Should().BeTrue();
                var results = workspace.FindControl<ListBox>("ConversationSearchResults")!;
                var sharedResults = compact.FindControl<ListBox>("ConversationSearchResults")!;
                results.SelectedIndex = 0;
                sharedResults.SelectedIndex = 0;
                var reads = reader.Reads;
                sharedRoles.SelectedItem = ConversationSearchRole.HOST;
                roles.SelectedItem.Should().Be(ConversationSearchRole.HOST);
                model.ConversationSearchMessages.Should().BeEmpty();
                results.SelectedItem.Should().BeNull();
                sharedResults.SelectedItem.Should().BeNull();
                model.CanNextConversationSearch.Should().BeFalse();
                reader.Reads.Should().Be(reads);
                Click(workspace, "SearchConversation");
                await workspace.ConversationOperation;
                model.ConversationSearchMessages.Should().BeEmpty("HOST must not interpret a USER's Denied word as status");
                model.ConversationSearchStatus.Should().Contain("Observed 0 exact HOST").And.Contain("scanned 26");
                model.ConversationQuery = "Unavailable";
                Click(compact, "SearchConversation");
                await compact.ConversationOperation;
                model.ConversationSearchMessages.Should().HaveCount(25);
                results.SelectedIndex = 0;
                sharedResults.SelectedIndex = 0;
                reads = reader.Reads;
                model.ConversationQuery = "marker";
                reader.Reads.Should().Be(reads);
                results.SelectedItem.Should().BeNull();
                sharedResults.SelectedItem.Should().BeNull();
                model.CanNextConversationSearch.Should().BeFalse();
                sharedRoles.SelectedItem = ConversationSearchRole.All;
                Click(workspace, "SearchConversation");
                await workspace.ConversationOperation;
                model.ConversationSearchMessages.Should().HaveCount(25);
                reads = reader.Reads;
                model.ConversationRole = (ConversationSearchRole)99;
                await model.SearchConversationAsync();
                reader.Reads.Should().Be(reads);
                model.ConversationSearchMessages.Should().BeEmpty();
                model.ConversationSearchStatus.Should().Contain("InvalidRole");
                model.ComposerBody.Should().Be("independent dirty buffer");
                Click(compact, "ClearConversationSearch");
                model.ConversationRole.Should().Be(ConversationSearchRole.All);
                model.ConversationQuery.Should().BeEmpty();
            }
            finally { owner.Revoke(); }
        });
    }
}

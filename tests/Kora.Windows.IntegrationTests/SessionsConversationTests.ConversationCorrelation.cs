using Avalonia.Automation;
using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeCorrelationIsSharedAccessiblePassiveAndNeverRetargetsTheSelectedSession(bool done)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var original = await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "marker Denied\r\n e\u0301");
            await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "marker other");
            await using var composed = new Composition(f);
            if (done)
            {
                await composed.Workspace.ChangeLifecycleAsync(f.Request.SessionId, new(1), false,
                    Kora.Core.Hosting.RequestOrigin.LocalUi, f.Token);
            }
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
                var selected = model.SelectedSessionRecord;
                model.ComposerBody = "independent dirty";
                model.ConversationQuery = "marker";
                var audit = WindowsSqliteConversationTests.AuditText(f);
                var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                foreach (var (name, value) in new[]
                {
                    ("ConversationRequestId", original.Message.Request.RequestId.Value.ToString("D")),
                    ("ConversationTaskId", original.Message.Request.TaskId.Value.ToString("D")),
                })
                {
                    await model.SearchConversationAsync();
                    var results = workspace.FindControl<ListBox>("ConversationSearchResults")!;
                    var sharedResults = compact.FindControl<ListBox>("ConversationSearchResults")!;
                    results.SelectedIndex = 0;
                    sharedResults.SelectedIndex = 0;
                    var control = workspace.FindControl<TextBox>(name)!;
                    var shared = compact.FindControl<TextBox>(name)!;
                    AutomationProperties.GetName(control).Should().Contain("canonical lowercase nonempty D GUID")
                        .And.Contain("empty means Any").And.Contain("editing does not read");
                    AutomationProperties.GetName(shared).Should().Contain("Shared exact");
                    control.Focusable.Should().BeTrue();
                    shared.Focusable.Should().BeTrue();
                    shared.Text = value;
                    control.Text.Should().Be(value);
                    model.ConversationSearchMessages.Should().BeEmpty();
                    results.SelectedItem.Should().BeNull();
                    sharedResults.SelectedItem.Should().BeNull();
                    model.CanNextConversationSearch.Should().BeFalse();
                }
                model.ConversationMinimumSequence = "1";
                model.ConversationMaximumSequence = "1";
                model.ConversationSubmittedFrom = original.Message.SubmittedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
                model.ConversationSubmittedThrough = model.ConversationSubmittedFrom;
                model.ConversationOutcome = ModelTurnOutcome.Unavailable;
                model.ConversationReason = ModelTurnReason.UnregisteredModel;
                Click(compact, "SearchConversation");
                await compact.ConversationOperation;
                model.ConversationSearchMessages.Should().Equal(original.Message);
                model.ConversationRole = ConversationSearchRole.HOST;
                model.ConversationQuery = "Denied";
                await model.SearchConversationAsync();
                model.ConversationSearchMessages.Should().BeEmpty("USER text cannot supply HOST semantics");
                model.ConversationQuery = "Unavailable";
                await model.SearchConversationAsync();
                model.ConversationSearchMessages.Should().Equal(original.Message);
                model.ConversationRole = ConversationSearchRole.USER;
                await model.SearchConversationAsync();
                model.ConversationSearchMessages.Should().BeEmpty("terminal predicates do not rewrite USER role");
                model.ConversationRole = ConversationSearchRole.All;
                model.ConversationTaskId = Guid.NewGuid().ToString("D");
                await model.SearchConversationAsync();
                model.ConversationSearchMessages.Should().BeEmpty("a foreign task ID is only a nonmatching predicate");
                model.SelectedSessionRecord.Should().Be(selected);
                Click(workspace, "ClearConversationSearch");
                model.ConversationRequestId.Should().BeEmpty();
                model.ConversationTaskId.Should().BeEmpty();
                model.ComposerBody.Should().Be("independent dirty");
                (await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token)).Draft.Revision.Should().Be(0);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
                f.Count("scoped_grants").Should().Be(0);
            }
            finally { owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData("bad")]
    [InlineData("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}")]
    [InlineData(" aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    [InlineData("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa ")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task InvalidNativeCorrelationFailsExplicitlyBeforeBodyReadAndPreservesInput(string invalid)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var reader = new SearchReader((_, _, _, _) => throw new InvalidOperationException("must not read"));
        await using var composed = new Composition(f, conversationReader: reader);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.ConversationQuery = "marker";
        foreach (var request in new[] { true, false })
        {
            model.ClearConversationSearch();
            model.ConversationQuery = "marker";
            model.ConversationRequestId = request ? invalid : "";
            model.ConversationTaskId = request ? "" : invalid;
            await model.SearchConversationAsync();
            reader.Reads.Should().Be(0);
            model.ConversationSearchMessages.Should().BeEmpty();
            model.ConversationSearchStatus.Should().Contain("InvalidCorrelation").And.Contain("empty means Any");
            model.ConversationRequestId.Should().Be(request ? invalid : "");
            model.ConversationTaskId.Should().Be(request ? "" : invalid);
        }
    }
}

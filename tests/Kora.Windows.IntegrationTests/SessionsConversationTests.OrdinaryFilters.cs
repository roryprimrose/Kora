using System.Globalization;

using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    private static readonly string[] OrdinaryConversationFilterNames =
        ["ConversationSubmittedFrom", "ConversationSubmittedThrough", "ConversationTaskState"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinaryConversationDatesAndTaskStateShareNativeViewsAndPreserveChronologicalSnapshot(bool done)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "marker before");
            f.Time.Now = f.Time.Now.AddHours(1);
            var submitted = f.Time.Now.ToString("O", CultureInfo.InvariantCulture);
            for (var index = 0; index < 26; index++)
            { await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "marker bounded " + index); }
            f.Time.Now = f.Time.Now.AddHours(1);
            await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "marker after");
            await using var composed = new Composition(f);
            if (done)
            { await composed.Workspace.ChangeLifecycleAsync(f.Request.SessionId, new(1), false, RequestOrigin.LocalUi, f.Token); }
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
                model.ComposerBody = "independent unsent input";
                var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                var audit = WindowsSqliteConversationTests.AuditText(f);
                compact.FindControl<TextBox>("ConversationQuery")!.Text = "marker";
                compact.FindControl<TextBox>("ConversationSubmittedFrom")!.Text = submitted;
                workspace.FindControl<TextBox>("ConversationSubmittedThrough")!.Text = submitted;
                compact.FindControl<ComboBox>("ConversationTaskState")!.SelectedItem =
                    model.ConversationTaskStates.Single(option => option.Value == HostTaskState.Unavailable);
                workspace.FindControl<TextBox>("ConversationSubmittedFrom")!.Text.Should().Be(submitted);
                compact.FindControl<TextBox>("ConversationSubmittedThrough")!.Text.Should().Be(submitted);
                workspace.FindControl<ComboBox>("ConversationTaskState")!.SelectedItem.Should().Be(model.ConversationTaskStateChoice);
                model.ConversationSearchMessages.Should().BeEmpty("filter edits never search");
                model.ConversationTaskStates[0].ToString().Should().Be("Any");
                foreach (var window in new Window[] { workspace, compact })
                {
                    foreach (var name in OrdinaryConversationFilterNames)
                    {
                        var control = window.FindControl<Control>(name)!;
                        ControlAutomationPeer.CreatePeerForElement(control)!.GetName().Should().Contain("editing does not search");
                        control.Focus().Should().BeTrue();
                    }
                }
                var field = compact.FindControl<TextBox>("ConversationSubmittedFrom")!;
                field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                await compact.ConversationOperation;
                model.ConversationSearchMessages.Select(message => message.Sequence).Should().Equal(Enumerable.Range(2, 25).Select(value => (long)value));
                model.ConversationSearchMessages.Should().OnlyContain(message => message.SubmittedAt == f.Time.Now.AddHours(-1));
                model.CanNextConversationSearch.Should().BeTrue();
                Click(workspace, "NextConversationSearch");
                await workspace.ConversationOperation;
                model.ConversationSearchMessages.Single().Sequence.Should().Be(27);
                model.CanNextConversationSearch.Should().BeFalse();
                model.ConversationTaskStateChoice = model.ConversationTaskStates.Single(option => option.Value == HostTaskState.Succeeded);
                model.ConversationSearchMessages.Should().BeEmpty();
                await model.SearchConversationAsync();
                model.ConversationSearchMessages.Should().BeEmpty();
                model.ConversationTaskStateChoice = model.ConversationTaskStates.Single(option => option.Value == HostTaskState.Unavailable);
                model.ConversationRole = ConversationSearchRole.USER;
                await model.SearchConversationAsync();
                model.ConversationSearchMessages.Should().BeEmpty("a HOST task state cannot match USER-only fields");
                model.ConversationRole = ConversationSearchRole.All;
                await model.SearchConversationAsync();
                model.ConversationSearchMessages.Should().HaveCount(25);
                field.Focus();
                var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
                field.RaiseEvent(escape);
                escape.Handled.Should().BeTrue();
                field.IsFocused.Should().BeTrue();
                compact.IsVisible.Should().BeTrue();
                model.ConversationSubmittedFrom.Should().BeEmpty();
                model.ConversationSubmittedThrough.Should().BeEmpty();
                model.ConversationTaskStateChoice.Value.Should().BeNull();
                model.ConversationSearchMessages.Should().BeEmpty();
                model.ComposerBody.Should().Be("independent unsent input");
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
                (await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token)).Draft.Revision.Should().Be(0);
            }
            finally { owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData("not a date", "")]
    [InlineData(" 2026-10-21T01:00:00.0000000+00:00", "")]
    [InlineData("2026-10-21T01:00:00.0000000+01:00", "")]
    [InlineData("2026-10-22T01:00:00.0000000+00:00", "2026-10-21T01:00:00.0000000+00:00")]
    public async Task InvalidConversationDatesRetainEditableInputsAndRefuseBeforeAnyBodyRead(string from, string through)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var reader = new SearchReader((_, _, _, _) => throw new InvalidOperationException("Invalid dates must not read bodies."));
        await using var composed = new Composition(f, conversationReader: reader);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.ComposerBody = "independent unsent input";
        model.ConversationQuery = "marker";
        model.ConversationSubmittedFrom = from;
        model.ConversationSubmittedThrough = through;
        await model.SearchConversationAsync();
        reader.Reads.Should().Be(0);
        model.ConversationSearchStatus.Should().Contain("InvalidRange").And.Contain("UTC");
        model.ConversationSubmittedFrom.Should().Be(from);
        model.ConversationSubmittedThrough.Should().Be(through);
        model.ConversationSearchMessages.Should().BeEmpty();
        model.ComposerBody.Should().Be("independent unsent input");
        model.ClearConversationSearch();
        model.ConversationSubmittedFrom.Should().BeEmpty();
        model.ConversationSubmittedThrough.Should().BeEmpty();
    }
}

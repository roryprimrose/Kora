using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;

using AwesomeAssertions;
using AwesomeAssertions.Execution;

using Kora.Application.Documentation;
using Kora.Application.Configuration;
using Kora.Application.Voice;
using Kora.Core.Conversation;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AssistantSearchKeyboardReadsExactKeptAnswerAndReturnsWithoutRetargetingOrChangingEitherComposer(bool compact) =>
        HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var originalSession = f.Request.SessionId;
            await RetainSearchAnswer(f, "marker first retained answer");
            const string answer = "marker second exact answer café e\u0301\r\n<script>inert</script>";
            var exact = await RetainSearchAnswer(f, answer);
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            var otherSession = f.Request.SessionId;
            await RetainSearchAnswer(f, "marker another conversation must not replace the exact answer");
            var clipboard = new ReaderRetirementClipboard();
            var playback = new NativeAnswerPlayback();
            await using var admission = new AudioControlAdmission(f.Store, f.Store, new(f.Tasks));
            await using var markdown = new MarkdownDisplayConfigurationService(new MarkdownDisplaySettingsTests.Preferences(),
                admission, new AuditLog(), f.Time, NullLogger<MarkdownDisplayConfigurationService>.Instance);
            await markdown.ObserveAsync(f.Token);
            Window? surface = null;
            DetailWindow? detail = null;
            using var details = new DetailWindowController(new EmbeddedUserDocumentationProvider(), () => true,
                new(NullLogger<NativeDetailRenderer>.Instance), clipboard,
                (state, result, copy) => detail = new(state, result, copy),
                NullLogger<DetailWindowController>.Instance, markdownDisplay: markdown.CaptureAsync);
            await using var composed = new Composition(f,
                (content, live, resolve, token) => details.OpenConversationDetailAsync(content, surface, live, resolve, token),
                revokeDetails: details.RevokeConversationSession, answerPlayback: playback);
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            surface = compact ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
            surface.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == otherSession));
                model.ComposerBody = "other conversation unsent input";
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == originalSession));
                surface.FindControl<TextBox>("ConversationComposer")!.Text = "original unsent input";
                var retention = await f.Store.ReadRetentionAsync(originalSession, f.Token);
                var otherRetention = await f.Store.ReadRetentionAsync(otherSession, f.Token);
                var audit = WindowsSqliteConversationTests.AuditText(f);
                var tasks = f.Count("host_tasks");
                var query = surface.FindControl<TextBox>("ConversationQuery")!;
                surface.FindControl<ComboBox>("ConversationRole")!.SelectedItem = ConversationSearchRole.ASSISTANT;
                query.Focus().Should().BeTrue();
                surface.KeyTextInput("marker");
                surface.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                await (surface is SessionsWindow sessions ? sessions.ConversationOperation
                    : ((CompactSessionWindow)surface).ConversationOperation);
                model.ConversationSearchMessages.Should().HaveCount(2);
                var results = surface.FindControl<ListBox>("ConversationSearchResults")!;
                bool InResults() => surface.FocusManager?.GetFocusedElement() is Control focused
                    && focused.GetVisualAncestors().Contains(results);
                for (var index = 0; index < 20 && !InResults(); index++)
                {
                    surface.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
                }
                InResults().Should().BeTrue("Tab must enter the retained match list through its native item navigation");
                surface.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
                surface.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                results.SelectedItem.Should().Be(exact);
                surface.UpdateLayout();
                var row = (ListBoxItem)results.ContainerFromItem(results.SelectedItem)!;
                var retained = row.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => string.Equals(text.Name, "RetainedAssistantText", StringComparison.Ordinal));
                retained.Text.Should().Be(answer);
                var resultName = ControlAutomationPeer.CreatePeerForElement(retained)!.GetName();
                var open = row.GetVisualDescendants().OfType<Button>()
                    .Single(button => string.Equals(button.Name, "OpenConversationDetail", StringComparison.Ordinal));
                var visited = new List<string>();
                for (var index = 0; index < 20 && !open.IsFocused; index++)
                {
                    surface.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
                    if (surface.FocusManager?.GetFocusedElement() is Control focused)
                    {
                        visited.Add($"{focused.GetType().Name}#{focused.Name}");
                    }
                }
                open.IsFocused.Should().BeTrue("the selected exact match must be openable without a pointer; visited "
                    + string.Join(", ", visited));
                surface.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                surface.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                await owner.DetailOperation;
                detail.Should().NotBeNull(model.ConversationStatus);
                detail!.IsVisible.Should().BeTrue();
                var displayed = detail.FindControl<TextBlock>("ConversationAssistantLabel")!;
                displayed.IsVisible.Should().BeTrue();
                displayed.Text.Should().EndWith(answer);
                var detailName = ControlAutomationPeer.CreatePeerForElement(displayed)!.GetName();
                detail.FindControl<TextBox>("ContentReader")!.Text.Should().Be(exact.Body);
                detail.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                detail.IsVisible.Should().BeFalse();
                surface.IsVisible.Should().BeTrue();
                model.SelectedSessionRecord!.Authority.SessionId.Should().Be(originalSession);
                model.ComposerBody.Should().Be("original unsent input");
                query.Focus().Should().BeTrue("returning must leave the original search usable");
                surface.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                model.ConversationSearchMessages.Should().BeEmpty();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == otherSession));
                model.ComposerBody.Should().Be("other conversation unsent input");
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == originalSession));
                model.ComposerBody.Should().Be("original unsent input");
                (await f.Store.ReadConversationMessageAsync(originalSession, exact.Request.RequestId, f.Token)).Should().Be(exact);
                (await f.Store.ReadRetentionAsync(originalSession, f.Token)).Should().Be(retention);
                (await f.Store.ReadRetentionAsync(otherSession, f.Token)).Should().Be(otherRetention);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
                f.Count("host_tasks").Should().Be(tasks);
                f.Count("conversation_messages").Should().Be(3);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                (await f.Store.ReadConversationDraftAsync(originalSession, f.Token)).Draft.Revision.Should().Be(0);
                (await f.Store.ReadConversationDraftAsync(otherSession, f.Token)).Draft.Revision.Should().Be(0);
                clipboard.Writes.Should().Be(0);
                playback.Calls.Should().Be(0);
                using var assertions = new AssertionScope();
                resultName.Should().EndWith(answer, "the search match's text peer must expose the actual saved answer, not only its label");
                detailName.Should().EndWith(answer, "opening Details must keep the actual answer accessible, not replace it with a label");
            }
            finally
            {
                details.ClearForPrivacy();
                owner.Revoke();
            }
        });
}

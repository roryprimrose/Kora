using Avalonia.Automation.Peers;
using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Controls;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task SharedNativeDraftMetricsAreAccessiblePassiveAndNeverImposeTextBoxLimits()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var reader = new SearchReader(f.Store.ReadConversationPageAsync, f.Store.ReadConversationDraftAsync);
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
                await model.ReadConversationAsync();
                var before = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                var audits = f.Count("security_audit_events");
                var reads = reader.Reads;
                var draftReads = reader.DraftReads;
                reader.DenyReads = true;
                var source = workspace.FindControl<TextBox>("ConversationComposer")!;
                var other = compact.FindControl<TextBox>("ConversationComposer")!;
                source.MaxLength.Should().Be(0);
                other.MaxLength.Should().Be(0);
                source.Text = "e\u0301\ud83d\ude42\r\n";
                AssertShared("9 UTF-8 bytes; 5 Unicode scalars", 6);
                source.Text = new string('x', 4097);
                AssertShared("Save shape: over limit", 4097);
                model.ComposerBody.Should().HaveLength(4097);
                source.Text = "\ud800";
                AssertShared("Malformed Unicode", 1);
                model.ComposerBody.Should().Be("\ud800");
                other.Text = " \t\r\n";
                AssertShared("Submit shape: empty/whitespace", 4);
                var changes = 0;
                model.PropertyChanged += (_, _) => changes++;
                var dirty = model.ComposerDirty;
                _ = model.ComposerDraftSizeFeedback;
                _ = model.ComposerDraftSizeFeedback;
                changes.Should().Be(0);
                model.ComposerDirty.Should().Be(dirty);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(before);
                f.Count("security_audit_events").Should().Be(audits);
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("conversation_messages").Should().Be(0);
                reader.Reads.Should().Be(reads);
                reader.DraftReads.Should().Be(draftReads);
                reader.DenyReads = false;
                model.DiscardComposerBuffer();
                AssertShared("0 UTF-8 bytes; 0 Unicode scalars", 0);
                await WindowsSqliteConversationTests.Save(f,
                    await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "saved");
                model.ComposerBody = "different";
                AssertShared("9 UTF-8 bytes", 9);
                model.DecideComposerDraft = () => Task.FromResult(ComposerDraftDecision.Discard);
                await model.ReloadConversationDraftAsync();
                AssertShared("5 UTF-8 bytes", 5);
                composed.Access.ControlRevision++;
                model.ComposerDraftSizeFeedback.Should().Contain("5 UTF-8 bytes");
                reader.DenyReads = true;
                await model.ReadConversationAsync();
                AssertShared("0 UTF-8 bytes", 0);
                model.DiscardComposerBuffer();
                await model.SelectAsync(null);
                AssertShared("0 UTF-8 bytes", 0);
                composed.Access.CanInspect = false;
                model.ComposerDraftSizeFeedback.Should().Contain("unavailable").And.NotContain("0 UTF");
            }
            finally { owner.Revoke(); }
            model.ComposerDraftSizeFeedback.Should().Contain("unavailable");

            void AssertShared(string expected, int length)
            {
                foreach (var window in new Window[] { workspace, compact })
                {
                    var feedback = window.FindControl<NamedTextBlock>("DraftSizeFeedback")!;
                    feedback.Text.Should().Be(model.ComposerDraftSizeFeedback).And.Contain(expected);
                    ControlAutomationPeer.CreatePeerForElement(feedback)!.GetName().Should().Contain(expected)
                        .And.Contain("Current editable shared conversation draft");
                    window.FindControl<TextBox>("ConversationComposer")!.Text.Should().HaveLength(length);
                }
            }
        });
    }

    [Fact]
    public async Task NewRequestMetricsFollowOnlyEditableBodyAndReadsPreserveFrozenReview()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            using var composer = composed.NewComposer();
            var model = new NewRequestViewModel(composer);
            var window = new NewRequestWindow(model, () => Task.CompletedTask, _ => { });
            window.Show();
            try
            {
                var input = window.FindControl<TextBox>("NewRequestBody")!;
                var feedback = window.FindControl<NamedTextBlock>("DraftSizeFeedback")!;
                input.MaxLength.Should().Be(0);
                input.Text = new string('x', 4097);
                composer.Body.Length.Should().Be(4097);
                feedback.Text.Should().Contain("over limit");
                input.Text = "\udc00";
                composer.Body.Should().Be("\udc00");
                feedback.Text.Should().Contain("Malformed Unicode");
                input.Text = "\ud83d\ude42";
                await composer.Create();
                composer.Review();
                var frozen = composer.ReviewedBody;
                var changes = 0;
                model.PropertyChanged += (_, _) => changes++;
                model.DraftSizeFeedback.Should().Contain("4 UTF-8 bytes; 1 Unicode scalars")
                    .And.Contain("2 UTF-16 code units");
                ControlAutomationPeer.CreatePeerForElement(feedback)!.GetName().Should()
                    .Contain("4 UTF-8 bytes").And.Contain("Current editable New request draft");
                changes.Should().Be(0);
                composer.ReviewedBody.Should().Be(frozen);
                composer.CanSubmit.Should().BeTrue();
                input.Text = string.Empty;
                feedback.Text.Should().Contain("0 UTF-8 bytes; 0 Unicode scalars")
                    .And.Contain("Submit shape: empty/whitespace");
                composed.Access.ControlRevision++;
                composer.CanSubmit.Should().BeFalse();
                feedback.Text.Should().Be(model.DraftSizeFeedback);
                composed.Access.CanInspect = false;
                model.DraftSizeFeedback.Should().Contain("unavailable");
            }
            finally { window.ClosePrivate(); }
            model.DraftSizeFeedback.Should().Contain("unavailable");
        });
    }
}

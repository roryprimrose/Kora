using Avalonia.Automation.Peers;
using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Controls;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("Release planning")]
    public async Task SharedComposerTargetShowsReadableNameAndStableIdentityWithoutAuthorityCounters(string? name)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var first = f.Request.SessionId;
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            var second = f.Request.SessionId;
            await using var composed = new Composition(f);
            if (name is not null)
            {
                await composed.Workspace.RenameAsync(first, new(1), 0, new(name), RequestOrigin.LocalUi, f.Token);
                await composed.Workspace.RenameAsync(second, new(1), 0, new(name), RequestOrigin.LocalUi, f.Token);
            }
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            var workspace = new SessionsWindow(model, owner);
            var compact = new CompactSessionWindow(model, owner);
            workspace.Show();
            compact.Show();
            try
            {
                AssertLabel("Select an exact session to write a message.");
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == first));
                model.ComposerBody = "first topic's unsent input";
                AssertLabel("Conversation: " + (name ?? "Unnamed conversation") + "\nSession: " + first.Value.ToString("D"));
                model.DecideComposerDraft = () => throw new InvalidOperationException("Reading a label or switching topics must not request a discard.");
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == second));
                AssertLabel("Conversation: " + (name ?? "Unnamed conversation") + "\nSession: " + second.Value.ToString("D"));
                model.ComposerBody.Should().BeEmpty("matching names never identify the buffer");
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == first));
                model.ComposerBody.Should().Be("first topic's unsent input");
                var original = model.SelectedSessionRecord!;
                var tasks = f.Count("host_tasks");
                var audits = WindowsSqliteConversationTests.AuditText(f);
                var retention = await f.Store.ReadRetentionAsync(first, f.Token);
                var admission = model.CaptureDraftPresentationEligibility();
                AssertLabel("Conversation: " + (name ?? "Unnamed conversation") + "\nSession: " + first.Value.ToString("D"));
                admission().Should().BeTrue();
                model.SelectedSessionRecord.Should().BeSameAs(original);
                model.NameDraft = "Renamed topic";
                await model.RenameAsync();
                AssertLabel("Conversation: Renamed topic\nSession: " + first.Value.ToString("D"));
                model.SelectedSessionRecord!.Authority.Should().Be(original.Authority);
                model.SelectedSessionRecord.Metadata!.Revision.Value.Should().Be((original.Metadata?.Revision.Value ?? 0) + 1);
                model.ComposerBody.Should().Be("first topic's unsent input");
                f.Count("conversation_messages").Should().Be(0);
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("host_tasks").Should().Be(tasks + 1, "only the explicit rename creates a control receipt");
                WindowsSqliteConversationTests.AuditText(f).Should().NotBe(audits, "the explicit rename remains audited");
                (await f.Store.ReadRetentionAsync(first, f.Token)).LastMeaningfulActivity.Should().Be(retention.LastMeaningfulActivity);
                composed.Access.ControlRevision++;
                admission().Should().BeFalse("hiding the control epoch never removes its binding");
            }
            finally { owner.Revoke(); }

            void AssertLabel(string expected)
            {
                var auditsBefore = WindowsSqliteConversationTests.AuditText(f);
                var tasksBefore = f.Count("host_tasks");
                model.ComposerTarget.Should().Be(expected);
                foreach (var view in new Window[] { workspace, compact })
                {
                    var label = view.FindControl<NamedTextBlock>("ComposerTargetLabel")!;
                    label.Text.Should().Be(expected);
                    var accessible = ControlAutomationPeer.CreatePeerForElement(label)!.GetName();
                    accessible.Should().Contain(expected)
                        .And.NotContain("generation").And.NotContain("revision").And.NotContain("epoch");
                }
                WindowsSqliteConversationTests.AuditText(f).Should().Be(auditsBefore);
                f.Count("host_tasks").Should().Be(tasksBefore);
            }
        });
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Diagnostics;
using Kora.Application.Voice;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Dependencies;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed partial class ClaudeUiContractTests
{
    [Fact]
    public async Task Shipped_unavailable_setup_disables_actual_native_credential_control_before_network()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var native = NativeUxTestFixture.Create(Path.GetTempPath());
            await native.InitializeAsync();
            var f = new ClaudeAccountBrokerTests.Fixture { Approved = false };
            await using var broker = f.Create();
            await using var admission = new AudioControlAdmission(native.Interactions, native.Interactions, new(native.Tasks));
            using var controller = new ClaudeAccountWindowController(native.Main, broker, admission,
                NullLogger<ClaudeAccountWindowController>.Instance);
            native.Main.ConfigureClaudeAccount();
            await controller.Operation;
            Dispatcher.UIThread.RunJobs();
            var opened = controller.PresentedWindow!;
            opened.Should().NotBeNull();
            var credential = opened.GetLogicalDescendants().OfType<TextBox>().Single(control => control.Name is "ClaudeCredential");
            credential.IsEnabled.Should().BeFalse();
            credential.Text.Should().BeNullOrEmpty();
            f.Requests.Should().BeEmpty();
            opened.Close();
            controller.PresentedWindow.Should().BeNull();
        });
    }

    private static readonly string[] RemovedExplicitHostedControls =
    [
        "SubmitCopilotConversation", "SubmitCopilotAnswerStream", "SubmitLocalFirst", "SubmitClaudeConversation",
        "SubmitClaudeWithClarification", "SubmitClaudeObservations", "SubmitClaudeAnswerStream", "ClaudeObservationIds",
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Single_native_Submit_uses_Settings_selected_Claude_without_explicit_hosted_controls(bool compact)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composition = new HostedClaudeSessionRequestTests.Composition(f);
            await composition.InitializeAsync();
            var sink = new WindowsSqliteEvidenceSink(f.Paths);
            sink.Initialize();
            var model = new SessionsViewModel(composition.Workspace,
                new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), composition.Access, f.Time,
                    NullLogger<DurableEvidenceQuery>.Instance), composition.Access, NullLogger<SessionsViewModel>.Instance,
                conversation: composition.Conversation);
            var owner = new SessionsPresentationOwner(model);
            var workspace = compact ? null : new SessionsWindow(model, owner);
            var small = compact ? new CompactSessionWindow(model, owner) : null;
            Window window = workspace ?? (Window)small!;
            window.Show();
            try
            {
                foreach (var name in RemovedExplicitHostedControls)
                { window.FindControl<Control>(name).Should().BeNull(name + " was retired in favour of the single Submit"); }
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                const string original = "  native settings-routed Claude body  ";
                window.FindControl<TextBox>("ConversationComposer")!.Text = original;
                var submit = window.FindControl<Button>("SubmitConversation")!;
                submit.Content.Should().Be("_Submit");
                submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await (workspace?.ConversationOperation ?? small!.ConversationOperation);
                model.ConversationStatus.Should().Contain("Succeeded");
                var stored = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
                stored.Body.Should().Be(original);
                stored.AssistantResponse?.Body.Should().Be("genuine exact answer");
                composition.Http.Requests.Where(request => request.Method is "POST").Select(request => request.Uri)
                    .Should().Equal("https://api.anthropic.com/v1/messages/count_tokens", "https://api.anthropic.com/v1/messages");
                composition.Turns.IsQuiescent.Should().BeTrue();
            }
            finally { owner.Revoke(); window.Close(); }
        });
    }
}
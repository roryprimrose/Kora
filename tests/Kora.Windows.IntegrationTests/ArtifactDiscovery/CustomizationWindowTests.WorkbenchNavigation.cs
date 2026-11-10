using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Hosting;
using Kora.Core.Customizations;
using Kora.Definitions.Artifacts;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class CustomizationWindowTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task RegisteredMcpPromptsReadCurrentMetadataAndNeverDiscloseEndpointsOrInvokeAnything(
        bool voice, bool lookup, bool registerWorkIq)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent, syntheticVoice: voice);
            await f.InitializeAsync();
            var profile = Path.Combine(f.LocalRoot, "registered-mcp-profile");
            var store = new WindowsCustomizationStore(f, profile, Path.Combine(f.LocalRoot, "registered-mcp-user"));
            if (registerWorkIq)
            {
                var source = await store.Create(CustomizationKind.McpServer, CustomizationScope.Kora, "workiq", f.Token);
                await store.Save(source, null, [new(source.PrimaryFile, "{\"url\":\"https://private-endpoint.invalid/mcp\"}"u8)],
                    static () => true, publish => Task.FromResult(publish()), f.Token);
            }
            var use = new ArtifactUsePreferences(f);
            using var catalogue = EmbeddedArtifactCatalogue.Load();
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new HostTaskCoordinator(f.Tasks));
            var management = new CustomizationManagementService(store, admission, use, catalogue, f.Audit);
            var directories = new LocalArtifactDirectoryPreferences(f);
            using var reload = new ArtifactCatalogueReloadService(catalogue, f, directories,
                new ArtifactDirectoryConfigurationService(directories, f.Audit),
                new McpServerConfigurationService(f, directories, profile),
                NullLogger<ArtifactCatalogueReloadService>.Instance, profileRoamingRoot: profile);
            var authoring = new Authoring();
            using var customizations = new CustomizationWindowController(f.Main, management, authoring, catalogue, reload,
                static () => true, static () => 1, NullLogger<CustomizationWindowController>.Instance);
            customizations.Bind();
            using var responses = new ResponseWindowController(f.Main, NullLogger<ResponseWindowController>.Instance);
            using var settings = new SettingsWindowController(f.Main, NullLogger<SettingsWindowController>.Instance);
            customizations.BindWorkbench(responses.PrepareWorkbenchOwner,
                view => responses.ShowHostedView(WorkbenchActivity.Customizations, view));
            using var sessions = new SessionsWindowController(f.Main, f.Sessions, f.Evidence, f.Access,
                NullLogger<SessionsViewModel>.Instance);
            sessions.Bind();
            using var allWork = new AllWorkWindowController(f.Main, f.Sessions, f.Access,
                NullLogger<AllWorkViewModel>.Instance, sessions);
            using var commands = new WorkspaceSurfaceCommandController(f.Main, f.Access, allWork, sessions);
            commands.BindManagement(settings, customizations);
            var navigation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Main.WorkspaceSurfaceRequested += _ => { navigation.TrySetResult(); return Task.CompletedTask; };
            commands.Bind();
            var windows = 0;
            using var observe = Window.WindowOpenedEvent.AddClassHandler<CustomizationWindow>((_, _) => windows++);
            var phrase = lookup ? "Is there an MCP server for WorkIQ?" : "show me the MCP servers";
            if (voice)
            {
                f.Main.BindVoiceOwnershipGate(static () => true);
                await f.Main.SetVoiceConsentAsync(true);
                await f.Main.BeginPushToTalkAsync();
                await f.Main.EndPushToTalkAsync();
                f.Voice.PublishTranscript(f.Main.AssistantName + ", " + phrase);
                await navigation.Task.WaitAsync(f.Token);
                await commands.NavigationOperation;
            }
            else
            {
                f.Main.CommandText = phrase;
                await f.Main.RunTypedCommand.ExecuteAsync();
            }
            var window = responses.PrepareWorkbenchOwner();
            window.ActiveActivity.Should().Be(WorkbenchActivity.Customizations, f.Main.Transcript);
            var view = (CustomizationView)window.FindControl<ContentControl>("HostedViewContent")!.Content!;
            view.Kind.Should().Be(CustomizationKind.McpServer);
            view.Status.Text.Should().Contain(lookup ? "registered metadata" : "Registered MCP metadata");
            if (lookup) { view.Status.Text.Should().Contain(registerWorkIq ? "Found 1" : "No MCP server named WorkIQ"); }
            view.Status.Text.Should().NotContain("private-endpoint");
            view.Editor.Text.Should().BeNullOrEmpty();
            view.McpUrl.Text.Should().BeNullOrEmpty();
            authoring.Calls.Should().Be(0);
            authoring.InterpretCalls.Should().Be(0);
            windows.Should().Be(0);

            if (registerWorkIq)
            {
                var source = (await store.List(f.Token)).Single(item => item.Kind == CustomizationKind.McpServer);
                var bytes = File.ReadAllBytes(source.Location);
                File.WriteAllText(source.Location, "{invalid");
                (await customizations.ShowRegisteredMcpAsync(lookupWorkIq: true, static () => true)).Should().BeFalse();
                view.Status.Text.Should().Contain("unavailable").And.NotContain("Found 1").And.NotContain("No MCP server named WorkIQ");
                File.WriteAllBytes(source.Location, bytes);
                File.Delete(source.Location);
                await customizations.ShowRegisteredMcpAsync(lookupWorkIq: true, static () => true);
                view.Status.Text.Should().Contain("No MCP server named WorkIQ",
                    "every lookup must reread registered metadata rather than retain a stale found result");
            }
        });
    }
}

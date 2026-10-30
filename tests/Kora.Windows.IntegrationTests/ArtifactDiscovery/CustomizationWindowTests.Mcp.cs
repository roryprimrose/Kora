using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Hosting;
using Kora.Core.Artifacts;
using Kora.Core.Customizations;
using Kora.Definitions.Artifacts;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class CustomizationWindowTests
{
    [Theory]
    [InlineData("native")]
    [InlineData("typed")]
    [InlineData("cancel-read")]
    [InlineData("cancel-review")]
    [InlineData("cancel-confirm")]
    [InlineData("changed-review")]
    [InlineData("privacy")]
    public Task McpNativeWorkflowCompletesOnlyAfterSeparateExactReviewConfirmationAndReadback(string route) =>
        HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            var profile = Path.Combine(f.LocalRoot, "mcp-editor-profile");
            var store = new WindowsCustomizationStore(f, profile, Path.Combine(f.LocalRoot, "mcp-user"));
            var use = new ArtifactUsePreferences(f);
            using var embedded = EmbeddedArtifactCatalogue.Load();
            using var catalogue = new ArtifactCatalogue(embedded.Artifacts, use);
            var preferences = new LocalArtifactDirectoryPreferences(f);
            var configuration = new ArtifactDirectoryConfigurationService(preferences, f.Audit);
            var mcp = new McpServerConfigurationService(f, preferences, profile, use);
            using var reload = new ArtifactCatalogueReloadService(catalogue, f, preferences, configuration, mcp,
                NullLogger<ArtifactCatalogueReloadService>.Instance, profileRoamingRoot: profile, use: use);
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new HostTaskCoordinator(f.Tasks));
            var management = new CustomizationManagementService(store, admission, use, catalogue, f.Audit, configuration);
            var authoring = new Authoring();
            using var controller = new CustomizationWindowController(f.Main, management, authoring, catalogue, reload,
                () => true, () => 1, NullLogger<CustomizationWindowController>.Instance);
            CustomizationWindow? editor = null;
            var next = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var observer = Window.WindowOpenedEvent.AddClassHandler<Window>((view, _) =>
            {
                if (view is CustomizationWindow customizations) { editor = customizations; }
                else { next.TrySetResult(view); }
            });
            static void Choose(Window view, string caption) => view.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, caption)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            async Task<Window> NextReview()
            {
                try { return await next.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Token); }
                catch (TimeoutException exception)
                { throw new InvalidOperationException("MCP native review did not appear: " + editor?.Status.Text, exception); }
            }
            async Task Run(string text)
            {
                f.Main.CommandText = text;
                await f.Main.RunTypedCommand.ExecuteAsync().WaitAsync(TimeSpan.FromSeconds(20), f.Token);
                await controller.WaitForOperation().WaitAsync(TimeSpan.FromSeconds(20), f.Token);
            }
            controller.Bind();
            if (route is "native")
            {
                controller.Open();
                await controller.WaitForOperation().WaitAsync(TimeSpan.FromSeconds(20), f.Token);
                editor!.Kind = CustomizationKind.McpServer;
                editor.NewName.Text = "example";
                Choose(editor, "Add");
                await controller.WaitForOperation().WaitAsync(TimeSpan.FromSeconds(20), f.Token);
            }
            else { await Run("add an MCP server named example"); }
            editor.Should().NotBeNull();
            editor!.McpUrl.Text = "https://first.invalid/mcp";
            editor.McpHttpType.IsChecked = false;
            var path = Path.Combine(f.RoamingRoot, "Mcp", "mcp.json");
            File.Exists(path).Should().BeFalse();
            editor.Editor.IsVisible.Should().BeFalse();
            editor.Request.IsEnabled.Should().BeFalse();
            controller.VoiceTarget.Should().BeNull("endpoints cannot be sent to model authoring");
            Choose(editor, "Review and save");
            var review = await NextReview();
            var reviewText = review.GetVisualDescendants().OfType<TextBox>().Single().Text!;
            reviewText.Should().Contain("COMPLETE PROPOSED ENTRY").And.Contain("EXACT ENTRY REPLACEMENT DIFF")
                .And.Contain(path).And.Contain("https://first.invalid/mcp").And.Contain("independently removes MCP inclusion");
            File.Exists(path).Should().BeFalse();
            if (route is "cancel-review") { Choose(review, "Cancel"); }
            else if (route is "changed-review") { editor.McpUrl.Text = "https://changed.invalid/mcp"; }
            else if (route is "privacy")
            {
                f.Main.CloseForObservedPrivacyEvent("Synthetic MCP privacy closure", hidePresentation: true);
                await f.Main.PrivacyClosureTask;
            }
            else
            {
                next = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Choose(review, "Continue to separate Save confirmation");
                var confirmation = await NextReview();
                File.Exists(path).Should().BeFalse("the first review is not save consent");
                Choose(confirmation, route is "cancel-confirm" ? "Cancel" : "Save disabled and excluded");
            }
            await controller.WaitForOperation().WaitAsync(TimeSpan.FromSeconds(20), f.Token);
            authoring.Calls.Should().Be(0);
            authoring.InterpretCalls.Should().Be(0);
            if (route is not ("native" or "typed" or "cancel-read"))
            {
                File.Exists(path).Should().BeFalse();
                File.Exists(Path.Combine(f.LocalRoot, "Preferences", "artifact-use.json")).Should().BeFalse();
                use.Revision.Should().Be(0);
                if (route is "privacy") { editor.McpUrl.Text.Should().BeEmpty(); }
                return;
            }
            var source = (await store.List(f.Token)).Single();
            management.IsDisabled(source).Should().BeTrue();
            var discovered = mcp.Load().Single();
            discovered.IsIncluded.Should().BeFalse();
            discovered.Name.Should().Be("example");
            discovered.ToString().Should().NotContain("first.invalid");
            editor.Status.Text.Should().Contain("Saved atomically").And.Contain("disabled and independently excluded");
            var before = File.ReadAllBytes(path);
            configuration.SetMcpServerIncluded(discovered.Id, true);
            use.SetDisabled(source.Id, false);
            next = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var editing = Run("edit " + source.Id + " MCP server");
            var consent = await NextReview();
            consent.GetVisualDescendants().OfType<TextBox>().Single().Text.Should().NotContain("first.invalid");
            if (route is "cancel-read")
            {
                Choose(consent, "Cancel");
                await editing;
                editor.McpUrl.Text.Should().BeEmpty();
                editor.Status.Text.Should().Contain("No endpoint was disclosed");
                File.ReadAllBytes(path).Should().Equal(before);
                mcp.Load().Single().IsIncluded.Should().BeTrue("cancelling source consent must not change independent inclusion");
                return;
            }
            Choose(consent, "Read selected HTTPS entry");
            await editing;
            editor.McpUrl.Text.Should().Be("https://first.invalid/mcp");
            editor.McpUrl.Text = "https://second.invalid/mcp";
            next = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Choose(editor, "Review and save");
            review = await NextReview();
            review.GetVisualDescendants().OfType<TextBox>().Single().Text.Should().Contain("https://first.invalid/mcp").And.Contain("https://second.invalid/mcp");
            next = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Choose(review, "Continue to separate Save confirmation");
            var final = await NextReview();
            File.ReadAllBytes(path).Should().Equal(before);
            Choose(final, "Save disabled and excluded");
            await controller.WaitForOperation().WaitAsync(TimeSpan.FromSeconds(20), f.Token);
            mcp.Load().Single().IsIncluded.Should().BeFalse();
            management.IsDisabled(source).Should().BeTrue();
            File.ReadAllText(path).Should().Contain("https://second.invalid/mcp").And.NotContain("https://first.invalid/mcp");
            preferences.Load().IncludedMcpServerIds.Should().BeEmpty();
            authoring.Calls.Should().Be(0);
        });
}

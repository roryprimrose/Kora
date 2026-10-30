using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Hosting;
using Kora.Core.Artifacts;
using Kora.Core.Communication;
using Kora.Core.Customizations;
using Kora.Definitions.Artifacts;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class CustomizationWindowTests
{
    [Theory]
    [InlineData("confirmed")]
    [InlineData("edited")]
    [InlineData("read-cancel")]
    [InlineData("review-cancel")]
    [InlineData("save-cancel")]
    [InlineData("draft")]
    [InlineData("destination-input")]
    [InlineData("destination-scope")]
    [InlineData("kind")]
    [InlineData("source")]
    [InlineData("access-revoked")]
    [InlineData("collision")]
    [InlineData("case-alias")]
    [InlineData("privacy")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("call-revision")]
    [InlineData("input")]
    [InlineData("reload")]
    [InlineData("uncertain")]
    public async Task InspectedSharedSkillAdaptationCompletesSeparateDisabledPublicationAndExactReopenOrRefusesChanges(string state)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            var calls = new OriginalCallState();
            await f.InitializeAsync(null, callState: calls);
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new HostTaskCoordinator(f.Tasks));
            var profile = Path.Combine(f.LocalRoot, "adaptation-editor");
            var store = new WindowsCustomizationStore(f, profile, Path.Combine(f.LocalRoot, "adaptation-user"));
            var use = new ArtifactUsePreferences(f);
            using var catalogue = EmbeddedArtifactCatalogue.Load();
            var management = new CustomizationManagementService(store, admission, use, catalogue, f.Audit);
            var directories = new LocalArtifactDirectoryPreferences(f);
            using var reload = new ArtifactCatalogueReloadService(catalogue, f, directories,
                new ArtifactDirectoryConfigurationService(directories, f.Audit),
                new McpServerConfigurationService(f, directories, profile),
                NullLogger<ArtifactCatalogueReloadService>.Instance, profileRoamingRoot: profile);
            var original = await store.Create(CustomizationKind.Skill, CustomizationScope.User, "original", f.Token);
            const string text = "---\r\nname: original\r\nversion: 1.2.3\r\ndescription: Review prose\r\n---\r\n\r\nCredit to original author.\r\nPlain guidance.\r\n";
            var originalBytes = ArtifactFileDocument.Encode(text, true);
            await store.Save(original, null, [new("SKILL.md", originalBytes)], () => true,
                operation => Task.FromResult(operation()), f.Token);
            var authoring = new Authoring();
            var ownership = 1L;
            using var controller = new CustomizationWindowController(f.Main, management, authoring, catalogue, reload,
                () => true, () => ownership, NullLogger<CustomizationWindowController>.Instance);
            CustomizationWindow? editor = null;
            var next = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((view, _) =>
            {
                if (view is CustomizationWindow selected) { editor = selected; }
                else { next.TrySetResult(view); }
            });
            controller.Bind();
            Task Run(string command)
            {
                f.Main.CommandText = command;
                return f.Main.RunTypedCommand.ExecuteAsync();
            }
            void Choose(Window view, string caption) => view.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, caption)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            async Task<Window> Review(Task operation)
            {
                (await Task.WhenAny(next.Task, operation)).Should().BeSameAs(next.Task, editor?.Status.Text);
                var result = await next.Task;
                next = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return result;
            }
            var destinationPath = Path.Combine(f.RoamingRoot, "Skills", "adapted", "SKILL.md");
            var inspect = Run("show original skill");
            var consent = await Review(inspect);
            consent.Title.Should().Be("Review shared skill read consent");
            editor!.Editor.Text.Should().BeEmpty("selection and metadata must not disclose source before read consent");
            Choose(consent, state is "read-cancel" ? "Cancel" : "Read exact shared skill");
            await inspect;
            if (state is "read-cancel")
            {
                editor.Editor.Text.Should().BeEmpty();
                await Run("create kora adaptation adapted");
                editor.Status.Text.Should().Contain("Inspect one read-consented shared");
            }
            else
            {
                editor.Editor.Text.Should().Be(text);
                await Run("Kora, create kora adaptation adapted");
                editor.Editor.Text.Should().Contain("attribution: Kora adaptation of original@1.2.3")
                    .And.Contain("Credit to original author.");
                editor.Details.Text.Should().Contain(destinationPath);
                File.Exists(destinationPath).Should().BeFalse("creating the adaptation only prepares an inert draft");
                if (state is "edited") { editor.Editor.Text += "Optional adapted guidance."; }
                var saving = Run("save skill adaptation");
                var review = await Review(saving);
                review.Title.Should().Be("Review complete skill adaptation");
                var disclosed = review.GetVisualDescendants().OfType<TextBox>().Single().Text;
                disclosed.Should().Contain(text).And.Contain(editor.Editor.Text).And.Contain(destinationPath).And.Contain(original.Location);
                if (state is "review-cancel") { Choose(review, "Cancel"); }
                else
                {
                    Choose(review, "Continue to separate save confirmation");
                    var confirm = await Review(saving);
                    confirm.Title.Should().Be("Confirm disabled skill adaptation");
                    switch (state)
                    {
                        case "draft": editor.Editor.Text += "Changed during confirmation."; break;
                        case "destination-input": editor.NewName.Text = "different"; break;
                        case "destination-scope": editor.Destination.SelectedItem = CustomizationScope.User; break;
                        case "kind": editor.Kind = CustomizationKind.Agent; break;
                        case "source": File.AppendAllText(original.Location, "External source edit."); break;
                        case "access-revoked": File.Move(original.Location, original.Location + ".withdrawn"); break;
                        case "collision":
                        case "case-alias":
                            var directory = Path.Combine(f.RoamingRoot, "Skills", state is "case-alias" ? "ADAPTED" : "adapted");
                            Directory.CreateDirectory(directory);
                            File.WriteAllText(Path.Combine(directory, "SKILL.md"), "Unrelated destination, never overwrite.");
                            break;
                        case "privacy":
                            f.Main.CloseForObservedPrivacyEvent("Synthetic adaptation privacy closure", hidePresentation: true);
                            await f.Main.PrivacyClosureTask;
                            break;
                        case "owner": ownership++; break;
                        case "call": calls.Set(CallState.Active); break;
                        case "call-revision":
                            var priorCall = f.Main.CallPolicyRevision;
                            calls.Set(CallState.Active);
                            calls.Set(CallState.Clear);
                            f.Main.CallPolicyRevision.Should().BeGreaterThan(priorCall);
                            break;
                        case "input": editor.Request.Text = "Changed input during review."; break;
                        case "reload": reload.ReloadNow().Should().BeTrue(); break;
                        case "uncertain": store.AfterComponentPublished = _ => throw new IOException("Synthetic uncertain readback"); break;
                    }
                    if (confirm.IsVisible)
                    { Choose(confirm, state is "save-cancel" ? "Cancel" : "Save new disabled skill"); }
                }
                await saving;
                if (state is "confirmed" or "edited")
                {
                    editor.Status.Text.Should().Contain("reopened DISABLED");
                    editor.Editor.Text.Should().Contain("attribution:").And.Contain("Credit to original author.");
                    editor.Editor.Text.Should().Be(File.ReadAllText(destinationPath));
                    var destination = (await store.List(f.Token)).Single(item => item.Scope == CustomizationScope.Kora);
                    management.IsDisabled(destination).Should().BeTrue();
                    var reopened = await store.Read(destination, f.Token);
                    reopened.Files.Single().Text.Should().Be(editor.Editor.Text);
                    reopened.Files.Single().Text.Contains("Optional adapted guidance.", StringComparison.Ordinal).Should().Be(state is "edited");
                    await Run("show adapted skill");
                    editor.Editor.Text.Should().Be(reopened.Files.Single().Text);
                    management.IsDisabled(destination).Should().BeTrue();
                }
                else if (state is "collision" or "case-alias")
                { File.ReadAllText(destinationPath).Should().Be("Unrelated destination, never overwrite."); }
                else if (state is "uncertain")
                {
                    File.Exists(destinationPath).Should().BeTrue();
                    editor.Status.Text.Should().NotContain("reopened DISABLED");
                    await Run("save skill adaptation");
                    editor.Status.Text.Should().Contain("Do not retry");
                    var destination = (await store.List(f.Token)).Single(item => item.Scope == CustomizationScope.Kora);
                    management.IsDisabled(destination).Should().BeTrue();
                }
                else { File.Exists(destinationPath).Should().BeFalse(editor.Status.Text); }
            }
            if (state is "source") { File.ReadAllText(original.Location).Should().Be(text + "External source edit."); }
            else { File.ReadAllBytes(state is "access-revoked" ? original.Location + ".withdrawn" : original.Location).Should().Equal(originalBytes); }
            authoring.Calls.Should().Be(0);
            authoring.InterpretCalls.Should().Be(0);
            f.Main.IsVoiceEnabled.Should().BeFalse();
        });
    }

    [Theory]
    [InlineData("reserved")]
    [InlineData("unknown")]
    [InlineData("dependency")]
    [InlineData("script")]
    [InlineData("built-in")]
    public async Task IncompatibleSkillAdaptationNeverStagesOrPublishesPartialGuidance(string state)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new HostTaskCoordinator(f.Tasks));
            var profile = Path.Combine(f.LocalRoot, "rejected-adaptation-editor");
            var store = new WindowsCustomizationStore(f, profile, Path.Combine(f.LocalRoot, "rejected-adaptation-user"));
            var use = new ArtifactUsePreferences(f);
            using var catalogue = EmbeddedArtifactCatalogue.Load();
            var management = new CustomizationManagementService(store, admission, use, catalogue, f.Audit);
            var directories = new LocalArtifactDirectoryPreferences(f);
            using var reload = new ArtifactCatalogueReloadService(catalogue, f, directories,
                new ArtifactDirectoryConfigurationService(directories, f.Audit),
                new McpServerConfigurationService(f, directories, profile),
                NullLogger<ArtifactCatalogueReloadService>.Instance, profileRoamingRoot: profile);
            var original = await store.Create(CustomizationKind.Skill, CustomizationScope.User, "original", f.Token);
            var field = state switch { "unknown" => "author: Unknown cannot be discarded\n",
                "dependency" => "allowed-tools: required-tool\n", _ => "" };
            var bytes = ArtifactFileDocument.Encode("---\nname: original\nversion: 1.0.0\ndescription: Example\n" + field
                + "---\nPlain guidance." + (state is "script" ? "\nRun required.ps1" : ""), false);
            await store.Save(original, null, [new("SKILL.md", bytes)], () => true, operation => Task.FromResult(operation()), f.Token);
            using var controller = new CustomizationWindowController(f.Main, management, new Authoring(), catalogue, reload,
                () => true, () => 1, NullLogger<CustomizationWindowController>.Instance);
            CustomizationWindow? editor = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((view, _) =>
            {
                if (view is CustomizationWindow selected) { editor = selected; }
                else if (view.Title is "Review shared skill read consent")
                { view.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Read exact shared skill"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            });
            controller.Bind();
            async Task Run(string command) { f.Main.CommandText = command; await f.Main.RunTypedCommand.ExecuteAsync(); }
            await Run(state is "built-in" ? "show Lock the machine skill" : "show original skill");
            await Run("create kora adaptation " + (state is "reserved" ? "settings" : "adapted"));
            editor!.Status.Text.Should().NotBeNullOrWhiteSpace().And.NotContain("Kora adaptation draft only");
            Directory.Exists(Path.Combine(f.RoamingRoot, "Skills")).Should().BeFalse();
            File.ReadAllBytes(original.Location).Should().Equal(bytes);
        });
    }
}

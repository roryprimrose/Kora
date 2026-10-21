using System.Security.AccessControl;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Hosting;
using Kora.Core.Artifacts;
using Kora.Core.Commands;
using Kora.Definitions.Artifacts;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class OwnedDefinitionEditorWindowTests
{
    [Theory]
    [InlineData(ArtifactKind.Instruction, "confirmed")]
    [InlineData(ArtifactKind.Prompt, "confirmed")]
    [InlineData(ArtifactKind.Instruction, "cancel")]
    [InlineData(ArtifactKind.Prompt, "dirty-before")]
    [InlineData(ArtifactKind.Instruction, "draft")]
    [InlineData(ArtifactKind.Prompt, "selection")]
    [InlineData(ArtifactKind.Instruction, "name")]
    [InlineData(ArtifactKind.Prompt, "kind")]
    [InlineData(ArtifactKind.Instruction, "owner")]
    [InlineData(ArtifactKind.Prompt, "owner-closed")]
    [InlineData(ArtifactKind.Prompt, "input")]
    [InlineData(ArtifactKind.Instruction, "disk")]
    [InlineData(ArtifactKind.Prompt, "query")]
    [InlineData(ArtifactKind.Prompt, "use-filter")]
    [InlineData(ArtifactKind.Instruction, "query-activate")]
    [InlineData(ArtifactKind.Prompt, "query-activate")]
    public async Task ActualNativeDisableHasExactReviewAndNoSourceMutationOrDraftDiscard(ArtifactKind kind, string condition)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new HostTaskCoordinator(f.Tasks));
            var instructionStore = new WindowsOwnedInstructionStore(f);
            var promptStore = new WindowsOwnedInstructionStore(f, ArtifactKind.Prompt);
            var instructions = new InstructionRevisionCatalogue(f);
            var prompts = new InstructionRevisionCatalogue(f, ArtifactKind.Prompt);
            var catalogue = EmbeddedArtifactCatalogue.Load();
            var controls = new BuiltInCommandRouter(new BuiltInCommandCatalog());
            var instructionActions = new OwnedInstructionEditorActions(instructionStore, admission, instructions, catalogue, f.Audit, controls);
            var promptActions = new OwnedInstructionEditorActions(promptStore, admission, prompts, catalogue, f.Audit, controls, ArtifactKind.Prompt);
            var preferences = new LocalArtifactDirectoryPreferences(f);
            using var reload = new ArtifactCatalogueReloadService(catalogue, f, preferences,
                new ArtifactDirectoryConfigurationService(preferences, f.Audit),
                new McpServerConfigurationService(f, preferences, Path.Combine(f.LocalRoot, "no-profile")),
                NullLogger<ArtifactCatalogueReloadService>.Instance, instructions, instructionStore,
                Path.Combine(f.LocalRoot, "no-profile"), prompts, promptStore);
            var ownership = 1L;
            using var controller = new OwnedInstructionEditorController(f.Main, instructionActions, new(instructionActions), reload, f,
                () => true, () => ownership, NullLogger<OwnedInstructionEditorController>.Instance, promptActions, new(promptActions));
            var store = kind == ArtifactKind.Instruction ? instructionStore : promptStore;
            var actions = kind == ArtifactKind.Instruction ? instructionActions : promptActions;
            var document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(Document(kind, "Original source").Text, true),
                kind, "kora-profile", "example");
            var source = (await store.Write("example", null, document, () => true, f.Token)).Current!;
            reload.ReconcileSavedCopy(source).Should().BeTrue();
            await actions.Activate(source, _ => Task.FromResult(true), () => true, f.Token);
            var useReview = condition is "query-activate";
            if (useReview) { await actions.Disable(source, _ => Task.FromResult(true), () => true, f.Token); }
            var path = Path.Combine(f.RoamingRoot, OwnedDefinitionKind.DirectoryName(kind), source.CanonicalFileName);
            var originalBytes = File.ReadAllBytes(path);
            var info = new FileInfo(path);
            var originalAccess = info.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
            var writeTime = info.LastWriteTimeUtc;
            var attributes = info.Attributes;
            OwnedInstructionEditorWindow? editor = null;
            Window? dialog = null;
            TaskCompletionSource<Window>? reviewOpened = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window is OwnedInstructionEditorWindow selected) { editor = selected; }
                else { dialog = window; reviewOpened?.TrySetResult(window); }
            });
            controller.Bind();
            async Task Run(string command)
            {
                f.Main.CommandText = command;
                await f.Main.RunTypedCommand.ExecuteAsync();
            }
            var prefix = kind == ArtifactKind.Instruction ? "instructions" : "prompts";
            await Run(prefix + " inspect example");
            editor.Should().NotBeNull();
            var draft = editor!.Source.Text;
            if (condition is "dirty-before")
            {
                editor.Source.Text += "Unsaved draft retained.";
                draft = editor.Source.Text;
                await Run(prefix + " disable");
                editor.Source.Text.Should().Be(draft);
                actions.IsReviewedUseEnabled(source).Should().BeTrue();
                dialog.Should().BeNull();
                return;
            }
            reviewOpened = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task disable;
            if (kind == ArtifactKind.Instruction && condition is "confirmed")
            {
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var status = editor.GetVisualDescendants().OfType<TextBlock>()
                    .Single(block => block.Text?.StartsWith("Owned kind", StringComparison.Ordinal) == true);
                status.PropertyChanged += (_, change) =>
                {
                    if (change.Property != TextBlock.TextProperty) { return; }
                    if (status.Text?.Contains("Control receipt", StringComparison.Ordinal) == true) { completion.TrySetResult(); }
                    else if (status.Text?.StartsWith("Unavailable:", StringComparison.Ordinal) == true)
                    { completion.TrySetException(new InvalidOperationException(status.Text)); }
                };
                editor.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Disable reviewed use"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                disable = completion.Task;
            }
            else { disable = Run("Kora, " + prefix + (useReview ? " activate" : " stop-use")); }
            (await Task.WhenAny(disable, reviewOpened.Task)).Should().BeSameAs(reviewOpened.Task);
            dialog = await reviewOpened.Task;
            dialog.GetVisualDescendants().OfType<TextBox>().Select(text => text.Text).Should().Equal(source.Document.Text, source.Document.Text);
            var disclosure = string.Join("\n", dialog.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
            if (useReview) { disclosure.Should().Contain(source.Document.Definition.Digest).And.Contain("Permit explicit selection"); }
            else { disclosure.Should().Contain(source.Revision).And.Contain(source.CanonicalFileName).And.Contain("NEW requests"); }
            if (condition is "owner-closed")
            {
                f.Access.SetOpen(false);
                await disable;
                await f.Main.PrivacyClosureTask;
                editor.Source.Text.Should().BeEmpty();
                editor.IsVisible.Should().BeFalse();
                File.ReadAllBytes(path).Should().Equal(originalBytes);
                return;
            }
            switch (condition)
            {
                case "draft": editor.Source.Text += "Later draft change."; draft = editor.Source.Text; break;
                case "selection": editor.Files.ItemsSource = CopySelections; editor.Files.SelectedItem = "other"; break;
                case "name": editor.NameInput.Text = "other"; break;
                case "kind": editor.KindInput.SelectedItem = kind == ArtifactKind.Instruction ? ArtifactKind.Prompt : ArtifactKind.Instruction; break;
                case "owner": ownership++; break;
                case "input": await Run(prefix + " list"); break;
                case "disk": File.WriteAllBytes(path, Document(kind, "External edit").CopyBytes()); break;
                case "query": editor.QueryInput.Text = "example"; break;
                case "query-activate": editor.QueryInput.Text = "example"; break;
                case "use-filter": editor.UseStateInput.SelectedItem = OwnedDefinitionUseFilter.UseEnabled; break;
            }
            if (condition is "query" or "use-filter" or "query-activate")
            {
                dialog.IsVisible.Should().BeFalse();
                editor.ReviewUse.IsEnabled.Should().BeFalse();
                editor.DisableReviewedUse.IsEnabled.Should().BeFalse();
                editor.Files.SelectedItem.Should().BeNull();
                editor.NextPage.IsEnabled.Should().BeFalse();
            }
            dialog.GetVisualDescendants().OfType<Button>().Single(button =>
                Equals(button.Content, condition is "cancel" ? "Cancel" : useReview ? "Permit explicit use" : "Confirm disable reviewed use"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await disable;
            editor.Source.Text.Should().Be(draft);
            var shouldHold = condition is "confirmed" or "disk" or "query-activate";
            actions.IsReviewedUseEnabled(source).Should().Be(!shouldHold);
            if (condition is "disk")
            {
                reviewOpened = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var activation = Run(prefix + " activate");
                (await Task.WhenAny(activation, reviewOpened.Task)).Should().BeSameAs(activation,
                    "an unconfirmed revocation requires explicit reload/readback before another use review");
                await activation;
                return;
            }
            File.ReadAllBytes(path).Should().Equal(originalBytes);
            var unchanged = new FileInfo(path);
            unchanged.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All).Should().Be(originalAccess);
            unchanged.LastWriteTimeUtc.Should().Be(writeTime);
            unchanged.Attributes.Should().Be(attributes);
            (await store.Read("example", f.Token))!.Revision.Should().Be(source.Revision);
            var router = new ArtifactCommandRouter(catalogue);
            router.Match("/" + kind.ToString().ToLowerInvariant() + " example", "Kora").Invocation.Should()
                .Match<ArtifactInvocation?>(invocation => shouldHold ? invocation == null : invocation != null);
            if (condition is "confirmed")
            {
                reload.ReloadNow().Should().BeTrue();
                router.Match("/" + kind.ToString().ToLowerInvariant() + " example", "Kora").Invocation.Should().BeNull();
                catalogue.Candidates.Should().Contain(item => item.Kind == kind && item.OwnedInstructionName == "example");
                await Run(prefix + " inspect example");
                editor.Source.IsReadOnly.Should().BeFalse();
                editor.Source.Text.Should().Be(source.Document.Text);
            }
        });
    }

    [Fact]
    public async Task NativeDisableButtonRaisesTheSharedControllerAction()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var actions = new List<string>();
            var editor = new OwnedInstructionEditorWindow(actions.Add);
            editor.Show();
            try
            {
                editor.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Disable reviewed use"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                actions.Should().Equal("Disable reviewed use");
            }
            finally { editor.Close(); }
            return Task.CompletedTask;
        });
    }
}

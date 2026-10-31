using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Hosting;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Customizations;
using Kora.Definitions.Artifacts;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class CustomizationWindowTests
{
    [Theory]
    [InlineData(CustomizationKind.Instruction, "native")]
    [InlineData(CustomizationKind.Prompt, "native")]
    [InlineData(CustomizationKind.Agent, "native")]
    [InlineData(CustomizationKind.Instruction, "typed")]
    [InlineData(CustomizationKind.Prompt, "typed")]
    [InlineData(CustomizationKind.Agent, "typed")]
    [InlineData(CustomizationKind.Instruction, "activated")]
    [InlineData(CustomizationKind.Prompt, "activated")]
    [InlineData(CustomizationKind.Agent, "activated")]
    [InlineData(CustomizationKind.Instruction, "cancel-review")]
    [InlineData(CustomizationKind.Instruction, "cancel-confirm")]
    [InlineData(CustomizationKind.Instruction, "draft")]
    [InlineData(CustomizationKind.Instruction, "source")]
    [InlineData(CustomizationKind.Instruction, "selection")]
    [InlineData(CustomizationKind.Instruction, "catalogue")]
    [InlineData(CustomizationKind.Instruction, "owner")]
    [InlineData(CustomizationKind.Instruction, "privacy")]
    [InlineData(CustomizationKind.Instruction, "uncertain")]
    [InlineData(CustomizationKind.Instruction, "post-effect-privacy")]
    [InlineData(CustomizationKind.Instruction, "reload-failure")]
    [InlineData(CustomizationKind.Instruction, "recover-draft")]
    [InlineData(CustomizationKind.Prompt, "recover-draft")]
    [InlineData(CustomizationKind.Agent, "recover-draft")]
    [InlineData(CustomizationKind.Instruction, "oversize")]
    [InlineData(CustomizationKind.Prompt, "oversize")]
    [InlineData(CustomizationKind.Agent, "oversize")]
    [InlineData(CustomizationKind.Instruction, "nul")]
    [InlineData(CustomizationKind.Instruction, "unicode")]
    [InlineData(CustomizationKind.Instruction, "final-catalogue")]
    [InlineData(CustomizationKind.Instruction, "final-reload-failure")]
    public async Task SharedDefinitionDeletionCompletesTheActualReviewedOperationOrPreservesSourceAndDraft(
        CustomizationKind kind, string state)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent, syntheticVoice: true);
            await f.InitializeAsync();
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new HostTaskCoordinator(f.Tasks));
            var profile = Path.Combine(f.LocalRoot, "deletion-editor");
            var store = new WindowsCustomizationStore(f, profile, Path.Combine(f.LocalRoot, "deletion-user"));
            var use = new ArtifactUsePreferences(f);
            using var bundled = EmbeddedArtifactCatalogue.Load();
            using var catalogue = new ArtifactCatalogue(bundled.SourceCandidates, use);
            var audit = new DeletionAudit(f.Audit);
            var management = new CustomizationManagementService(store, admission, use, catalogue, audit);
            var directories = new LocalArtifactDirectoryPreferences(f);
            using var reload = new ArtifactCatalogueReloadService(catalogue, f, directories,
                new ArtifactDirectoryConfigurationService(directories, f.Audit),
                new McpServerConfigurationService(f, directories, profile),
                NullLogger<ArtifactCatalogueReloadService>.Instance, profileRoamingRoot: profile, use: use);
            var original = await store.Create(kind, CustomizationScope.Kora, "example", f.Token);
            var unrelated = await store.Create(kind, CustomizationScope.Kora, "unrelated", f.Token);
            var text = CustomizationLayout.Template(kind, "example");
            var bytes = ArtifactFileDocument.Encode(text, true);
            var otherBytes = ArtifactFileDocument.Encode(CustomizationLayout.Template(kind, "unrelated"), false);
            await store.Save(original, null, [new(original.PrimaryFile, bytes)], () => true,
                operation => Task.FromResult(operation()), f.Token);
            await store.Save(unrelated, null, [new(unrelated.PrimaryFile, otherBytes)], () => true,
                operation => Task.FromResult(operation()), f.Token);
            reload.ReloadNow().Should().BeTrue();
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
            async Task<Window> Review()
            {
                var result = await next.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Token);
                next = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return result;
            }
            await Run("edit example " + kind.ToString().ToLowerInvariant());
            editor.Should().NotBeNull();
            editor!.Editor.Text.Should().Be(text);
            editor.Editor.Text += "\nPrivate unsaved recovery.";
            if (state is "oversize")
            {
                editor.Editor.Text += new string('x', 65536 - ArtifactFileDocument.Encode(editor.Editor.Text!, true).Length + 1);
                ArtifactFileDocument.Encode(editor.Editor.Text!, true).Length.Should().Be(65537);
            }
            if (state is "nul") { editor.Editor.Text += "\0"; }
            if (state is "unicode") { editor.Editor.Text += "\ud800"; }
            var rawDraft = editor.Editor.Text;
            Task operation;
            if (state is "activated")
            {
                f.Main.BindVoiceOwnershipGate(() => true);
                await f.Main.SetVoiceConsentAsync(true);
                await f.Main.BeginPushToTalkCommand.ExecuteAsync();
                await f.Main.EndPushToTalkCommand.ExecuteAsync();
                f.Voice.PublishTranscript(f.Main.AssistantName + ", delete selected owned definition");
                await next.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Token);
                operation = controller.WaitForOperation();
            }
            else if (state is "typed") { operation = Run("delete selected owned definition"); }
            else
            {
                Choose(editor, "Delete owned definition");
                operation = controller.WaitForOperation();
            }
            var review = await Review();
            review.Title.Should().Be("Review exact owned definition deletion");
            var disclosure = review.GetVisualDescendants().OfType<TextBox>().Single().Text;
            disclosure.Should().Contain(original.Location).And.Contain(kind.ToString()).And.Contain(text)
                .And.Contain("Unsaved editor content is retained");
            File.ReadAllBytes(original.Location).Should().Equal(bytes);
            Choose(review, state is "cancel-review" ? "Cancel" : "Continue deletion");
            if (state is not "cancel-review")
            {
                var confirm = await Review();
                confirm.Title.Should().Be("Confirm exact owned definition deletion");
                switch (state)
                {
                    case "draft": editor.Editor.Text += "\nChanged during review."; rawDraft = editor.Editor.Text; break;
                    case "source": File.AppendAllText(original.Location, "\nExternal change."); break;
                    case "selection":
                        editor.GetVisualDescendants().OfType<Button>().Single(button => button.Content is StackPanel panel
                            && panel.Children.OfType<TextBlock>().Any(label => string.Equals(label.Text, unrelated.Location, StringComparison.Ordinal)))
                            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;
                    case "catalogue": reload.ReloadNow().Should().BeTrue(); break;
                    case "final-catalogue":
                        store.BeforeDeletionComparison = () => catalogue.Publish(catalogue.SourceCandidates);
                        break;
                    case "final-reload-failure":
                        store.BeforeDeletionComparison = () => catalogue.ReportReloadFailure(new IOException("Synthetic final reload failure"));
                        break;
                    case "owner": ownership++; break;
                    case "privacy":
                        f.Main.CloseForObservedPrivacyEvent("Synthetic deletion privacy closure", hidePresentation: true);
                        await f.Main.PrivacyClosureTask;
                        break;
                    case "uncertain": store.AfterDeletionCommitted = () => throw new IOException("Synthetic readback failure"); break;
                    case "post-effect-privacy": store.AfterDeletionCommitted = () => ownership++; break;
                    case "reload-failure":
                        store.AfterDeletionCommitted = () => File.WriteAllText(unrelated.Location, "Incompatible source");
                        break;
                }
                if (confirm.IsVisible) { Choose(confirm, state is "cancel-confirm" ? "Cancel" : "Delete exact owned file"); }
            }
            await operation.WaitAsync(TimeSpan.FromSeconds(20), f.Token);
            var deleted = state is "native" or "typed" or "activated" or "uncertain" or "post-effect-privacy"
                or "reload-failure" or "recover-draft" or "oversize" or "nul" or "unicode";
            File.Exists(original.Location).Should().Be(!deleted, editor.Status.Text);
            if (deleted)
            {
                management.IsDisabled(original).Should().BeTrue();
                Directory.Exists(Path.GetDirectoryName(original.Location)).Should().BeTrue();
                if (state is "uncertain" or "post-effect-privacy")
                { editor.Status.Text.Should().Contain("deletion committed").And.Contain("do not retry or assume rollback"); }
                else { editor.Status.Text.Should().Contain("Exact owned source deleted"); }
            }
            else if (state is not "source") { File.ReadAllBytes(original.Location).Should().Equal(bytes); }
            if (state is not "privacy") { editor.Editor.Text.Should().Be(rawDraft); }
            else { editor.Editor.Text.Should().BeEmpty(); }
            if (state is not "reload-failure") { File.ReadAllBytes(unrelated.Location).Should().Equal(otherBytes); }
            authoring.Calls.Should().Be(0);
            authoring.InterpretCalls.Should().Be(0);
            if (state is "final-catalogue" or "final-reload-failure")
            {
                management.IsDisabled(original).Should().BeTrue();
                audit.Events.Should().NotContain(entry => entry.ActionId == "definition.source.delete"
                    && entry.Outcome == SecurityAuditOutcome.Succeeded);
            }
            if (state is "recover-draft" or "oversize" or "nul" or "unicode")
            {
                async Task DisableAndKeepDraft(CustomizationSource target, string retained)
                {
                    var saveReceipts = audit.Events.Count(entry => string.Equals(entry.ActionId, "definition.source.save", StringComparison.Ordinal)
                        && entry.Outcome == SecurityAuditOutcome.Succeeded);
                    var disableReceipts = audit.Events.Count(entry => string.Equals(entry.ActionId, "definition.use.disable", StringComparison.Ordinal)
                        && string.Equals(entry.TargetId, target.Id, StringComparison.Ordinal)
                        && entry.Outcome == SecurityAuditOutcome.Succeeded);
                    Choose(editor, "Disable");
                    var disabling = controller.WaitForOperation();
                    var disableReview = await Review();
                    disableReview.Title.Should().Be("Disable in Kora");
                    Choose(disableReview, "Disable");
                    await disabling;
                    editor.Editor.Text.Should().Be(retained);
                    management.IsDisabled(target).Should().BeTrue();
                    audit.Events.Count(entry => string.Equals(entry.ActionId, "definition.use.disable", StringComparison.Ordinal)
                        && string.Equals(entry.TargetId, target.Id, StringComparison.Ordinal)
                        && entry.Outcome == SecurityAuditOutcome.Succeeded).Should().Be(disableReceipts + 1);
                    File.Exists(original.Location).Should().BeFalse();
                    File.Exists(target.Location).Should().BeFalse();
                    audit.Events.Count(entry => string.Equals(entry.ActionId, "definition.source.save", StringComparison.Ordinal)
                        && entry.Outcome == SecurityAuditOutcome.Succeeded).Should().Be(saveReceipts);
                    var closing = controller.PrepareClose();
                    var closeReview = await Review();
                    closeReview.Title.Should().Be("Unsaved customization");
                    Choose(closeReview, "Cancel");
                    (await closing).Should().BeFalse();
                    editor.Editor.Text.Should().Be(retained);
                    editor.IsVisible.Should().BeTrue();
                }
                await DisableAndKeepDraft(original, rawDraft!);
                foreach (var name in new[] { original.Name, original.Name.ToUpperInvariant() })
                {
                    editor.NewName.Text = name;
                    Choose(editor, "Create Kora copy");
                    await controller.WaitForOperation();
                    editor.Status.Text.Should().Contain("distinct Kora artifact ID");
                    editor.Editor.Text.Should().Be(rawDraft);
                    File.Exists(original.Location).Should().BeFalse();
                    management.IsDisabled(original).Should().BeTrue();
                    next.Task.IsCompleted.Should().BeFalse("identity rejection must precede any discard or rebase review");
                }
                if (state is "recover-draft")
                {
                    Choose(editor, "Review and save");
                    var oldSaving = controller.WaitForOperation();
                    Choose(await Review(), "Save disabled");
                    await oldSaving;
                    File.Exists(original.Location).Should().BeFalse("the frozen missing baseline cannot be reset by rejected copies");
                    editor.Editor.Text.Should().Be(rawDraft);
                }
                editor.NewName.Text = "recovered";
                Choose(editor, "Create Kora copy");
                var copying = controller.WaitForOperation();
                var dirty = await Review();
                dirty.Title.Should().Be("Unsaved customization");
                if (state is "oversize")
                {
                    Choose(dirty, "Cancel");
                    await copying;
                    editor.Editor.Text.Should().Be(rawDraft);
                    File.Exists(original.Location).Should().BeFalse();
                    Choose(editor, "Create Kora copy");
                    copying = controller.WaitForOperation();
                    dirty = await Review();
                }
                Choose(dirty, "Discard");
                await copying;
                var recoveredText = rawDraft!.Replace("name: example", "name: recovered", StringComparison.Ordinal);
                editor.Editor.Text.Should().Be(recoveredText);
                var recovered = await store.Create(kind, CustomizationScope.Kora, "recovered", f.Token);
                recovered.Id.Should().NotBe(original.Id);
                File.Exists(recovered.Location).Should().BeFalse("copy creates only a draft until its separate Save");
                await DisableAndKeepDraft(recovered, recoveredText);
                Choose(editor, "Review and save");
                var saving = controller.WaitForOperation();
                if (state is "recover-draft") { Choose(await Review(), "Save disabled"); }
                await saving;
                if (state is "recover-draft")
                {
                    File.ReadAllBytes(recovered.Location).Should().Equal(ArtifactFileDocument.Encode(recoveredText, true));
                    new ArtifactUsePreferences(f).IsDisabled(recovered.Id).Should().BeTrue();
                    management.IsDisabled(original).Should().BeTrue();
                }
                else
                {
                    File.Exists(recovered.Location).Should().BeFalse("retained raw text must not bypass the existing store bounds");
                    editor.Editor.Text.Should().Be(recoveredText);
                    editor.Status.Text.Should().Contain(state is "oversize" ? "64 KiB"
                        : state is "nul" ? "Binary components" : "valid Unicode");
                    next.Task.IsCompleted.Should().BeFalse("invalid recovery bytes cannot reach a Save review");
                    audit.Events.Should().NotContain(entry => entry.ActionId == "definition.source.save"
                        && entry.TargetId == recovered.Id && entry.Outcome == SecurityAuditOutcome.Succeeded);
                }
                File.Exists(original.Location).Should().BeFalse();
                editor.NewName.Text = original.Name;
                Choose(editor, "Create Kora copy");
                await controller.WaitForOperation();
                editor.Status.Text.Should().Contain("distinct Kora artifact ID");
                editor.Editor.Text.Should().Be(recoveredText);
                File.Exists(original.Location).Should().BeFalse("the recovery constraint survives rebasing and separate Save");
                if (state is "oversize")
                {
                    var boundedText = recoveredText[..(65536 - System.Text.Encoding.UTF8.Preamble.Length)];
                    editor.Editor.Text = boundedText;
                    var bounded = ArtifactFileDocument.Encode(boundedText, true);
                    bounded.Length.Should().Be(65536);
                    Choose(editor, "Review and save");
                    var boundedSaving = controller.WaitForOperation();
                    Choose(await Review(), "Save disabled");
                    await boundedSaving;
                    File.ReadAllBytes(recovered.Location).Should().Equal(bounded);
                    new ArtifactUsePreferences(f).IsDisabled(recovered.Id).Should().BeTrue();
                    management.IsDisabled(original).Should().BeTrue();
                    File.Exists(original.Location).Should().BeFalse();
                }
            }
        });
    }

    private sealed class DeletionAudit(ISecurityAuditLog inner) : ISecurityAuditLog
    {
        internal readonly List<SecurityAuditEvent> Events = [];
        public void Write(SecurityAuditEvent entry)
        {
            inner.Write(entry);
            Events.Add(entry);
        }
    }
}

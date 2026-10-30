using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using AwesomeAssertions;
using Kora.Application.Skills;
using Kora.Core.Skills;
using Kora.NativeUxFixture;
using Kora.Windows.Skills;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class OwnedSkillAuthoringNativeTests
{
    [Theory]
    [InlineData("typed")]
    [InlineData("native")]
    [InlineData("voice")]
    [InlineData("cancel-review")]
    [InlineData("cancel-confirm")]
    [InlineData("dirty")]
    [InlineData("edited-review")]
    [InlineData("target-edited-review")]
    [InlineData("owner")]
    [InlineData("privacy")]
    [InlineData("recovery")]
    public async Task OriginalUserRenameCompletesNativeReviewPublicationAndReopenOrPreservesDraftAndSource(string scenario)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent, syntheticVoice: true);
            await f.InitializeAsync();
            if (scenario is "voice")
            {
                f.Main.BindVoiceOwnershipGate(static () => true);
                await f.Main.SetVoiceConsentAsync(true);
                await f.Main.BeginPushToTalkCommand.ExecuteAsync();
                await f.Voice.Started.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Token);
                await f.Main.EndPushToTalkCommand.ExecuteAsync();
            }
            await using var admission = new OwnedSkillControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var store = new WindowsOwnedDeclarativeSkillStore(f);
            var document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Readable", "Attribution", "Exact source\r\nliteral");
            var saved = await store.Publish(document, static () => true, operation => Task.FromResult(operation()), f.Token);
            var save = new OwnedSkillSaveWorkflow(store, admission, f.Audit);
            var renames = new OwnedSkillRenameWorkflow(store, store, admission, f.Audit, TimeProvider.System);
            var owner = 1L;
            await using var controller = new OwnedSkillEditorController(f.Main, save, admission, static () => true,
                () => owner, NullLogger<OwnedSkillEditorController>.Instance, renames: renames);
            controller.Bind();
            OwnedSkillEditorWindow? editor = null;
            using var observed = Window.WindowOpenedEvent.AddClassHandler<OwnedSkillEditorWindow>((window, _) => editor = window);
            async Task Run(string text) { f.Main.CommandText = text; await f.Main.RunTypedCommand.ExecuteAsync(); }
            await Run("owned skill list");
            var window = editor ?? throw new InvalidOperationException("The owned editor did not open.");
            window.Saved.SelectedIndex = 0;
            window.RenameId.Text = "renamed";
            if (scenario is "dirty") { window.Guidance.Text = "Unsaved private draft"; }
            if (scenario is "recovery")
            {
                store.Checkpoint = point => { if (point is "rename-component-retired") { throw new IOException("injected boundary"); } };
                await store.Invoking(value => value.Rename(new(saved, "renamed"), false, static () => true,
                    operation => Task.FromResult(operation()), f.Token)).Should().ThrowAsync<OwnedSkillRenameUnconfirmedException>();
                store.Checkpoint = null;
                await Run("owned skill list");
                window.PendingRenames.Items.Should().ContainSingle();
            }
            var reviews = new List<string>();
            using var dialogs = Window.WindowOpenedEvent.AddClassHandler<Window>((dialog, _) =>
            {
                if (ReferenceEquals(dialog, window)) { return; }
                reviews.Add(dialog.Title!);
                var text = dialog.GetLogicalDescendants().OfType<TextBox>().Select(box => box.Text).ToArray();
                text.Should().HaveCount(2);
                text[0].Should().Contain(document.ManifestText).And.Contain(document.Guidance);
                text[1].Should().Contain("id: renamed\n").And.Contain(document.Guidance);
                var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToArray();
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (scenario is "edited-review" && reviews.Count == 1) { window.Guidance.Text = "Changed during review"; return; }
                    if (scenario is "target-edited-review" && reviews.Count == 1) { window.RenameId.Text = "other-target"; return; }
                    if (scenario is "privacy")
                    { f.Main.CloseForObservedPrivacyEvent("Synthetic rename closure", hidePresentation: true); return; }
                    if (scenario is "owner") { owner++; }
                    var cancel = scenario is "cancel-review" || scenario is "cancel-confirm" && reviews.Count == 2;
                    buttons[cancel ? ^1 : 0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
            });
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var status = window.GetLogicalDescendants().OfType<TextBlock>()
                .Single(block => block.Text?.StartsWith("Saved immutable", StringComparison.Ordinal) == true);
            status.PropertyChanged += (_, args) =>
            {
                if (args.Property == TextBlock.TextProperty)
                { completed.TrySetResult(); }
            };
            if (scenario is "native")
            {
                window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Rename selected package"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Token);
            }
            else if (scenario is "voice")
            {
                f.Voice.PublishTranscript(f.Main.AssistantName + ", owned skill rename example 1.0.0 renamed");
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Token);
            }
            else { await Run(scenario is "recovery" ? "owned skill recover-rename example 1.0.0" : "owned skill rename example 1.0.0 renamed"); }
            var success = scenario is "typed" or "native" or "voice" or "recovery";
            if (success)
            {
                reviews.Should().HaveCount(2, "the current status is: {0}", status.Text);
                status.Text.Should().StartWith("Complete package rename");
                (await new WindowsOwnedDeclarativeSkillStore(f).Read("example", "1.0.0", f.Token)).Should().BeNull();
                var reopened = await store.Read("renamed", "1.0.0", f.Token);
                reopened!.Document.InstructionBytes.ToArray().Should().Equal(document.InstructionBytes.ToArray());
                reopened.Document.IsAvailableForInvocation.Should().BeFalse();
                await Run("owned skill reopen renamed 1.0.0");
                window.IdInput.Text.Should().Be("renamed");
                window.Guidance.Text.Should().Be(document.Guidance);
            }
            else
            {
                (await store.Read("example", "1.0.0", f.Token))!.IsSameRevision(saved).Should().BeTrue();
                (await store.Read("renamed", "1.0.0", f.Token)).Should().BeNull();
                (await store.PendingRenames(f.Token)).Should().BeEmpty();
                if (scenario is "dirty") { window.Guidance.Text.Should().Be("Unsaved private draft"); reviews.Should().BeEmpty(); }
                if (scenario is "edited-review") { window.Guidance.Text.Should().Be("Changed during review"); }
                if (scenario is "target-edited-review") { window.RenameId.Text.Should().Be("other-target"); }
            }
        });
    }
}

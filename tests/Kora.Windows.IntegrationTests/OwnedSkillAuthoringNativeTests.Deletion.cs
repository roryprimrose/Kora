using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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
    [InlineData("escape-review")]
    [InlineData("escape-confirm")]
    [InlineData("dirty-cancel")]
    [InlineData("dirty-discard")]
    [InlineData("dirty-save")]
    [InlineData("draft")]
    [InlineData("selection")]
    [InlineData("privacy")]
    [InlineData("owner")]
    [InlineData("control")]
    [InlineData("source")]
    [InlineData("uncertain")]
    public async Task ExactAdmittedDeletionCompletesNativeReviewsAndRealFixtureCatalogueOrTruthfullyDenies(string scenario)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent, syntheticVoice: scenario is "voice");
            await f.InitializeAsync();
            await using var admission = new OwnedSkillControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var store = new WindowsOwnedDeclarativeSkillStore(f);
            var document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "complete literal guidance");
            var saved = await store.Publish(document, static () => true, operation => Task.FromResult(operation()), f.Token);
            var otherDocument = OwnedDeclarativeSkillDocument.Create("another", "1.0.0", "Another", "Description", "other literal guidance");
            var other = await store.Publish(otherDocument, static () => true, operation => Task.FromResult(operation()), f.Token);
            var ownership = 1L;
            await using var controller = new OwnedSkillEditorController(f.Main, new(store, admission, f.Audit), admission,
                static () => true, () => ownership, NullLogger<OwnedSkillEditorController>.Instance,
                deletion: new(store, admission, f.Audit, TimeProvider.System));
            controller.Bind();
            OwnedSkillEditorWindow? editor = null;
            var next = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window is OwnedSkillEditorWindow skill) { editor = skill; }
                else { next.TrySetResult(window); }
            });
            async Task Run(string text) { f.Main.CommandText = text; await f.Main.RunTypedCommand.ExecuteAsync(); }
            async Task<Window> Review(Task operation)
            {
                (await Task.WhenAny(next.Task, operation)).Should().BeSameAs(next.Task,
                    "the operation must reach its distinct native review: {0}", string.Join("\n",
                        editor!.GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text)));
                var result = await next.Task;
                next = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return result;
            }
            static void Choose(Window dialog, string caption) => dialog.GetLogicalDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, caption)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            static bool Escape(Window dialog)
            {
                dialog.GetLogicalDescendants().OfType<TextBox>().Last().Focus().Should().BeTrue();
                dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                var dismissed = !dialog.IsVisible;
                if (!dismissed) { dialog.Close(); }
                return dismissed;
            }
            if (scenario is "voice")
            {
                f.Main.BindVoiceOwnershipGate(static () => true);
                await f.Main.SetVoiceConsentAsync(true);
                await f.Main.BeginPushToTalkCommand.ExecuteAsync();
                await f.Voice.Started.Task.WaitAsync(f.Token);
                await f.Main.EndPushToTalkCommand.ExecuteAsync();
            }
            await Run("owned skill list");
            var editing = editor!;
            editing.Saved.SelectedItem = editing.Saved.Items.Cast<OwnedDeclarativeSkillSnapshot>()
                .Single(row => string.Equals(row.Document.Id, "example", StringComparison.Ordinal));
            if (scenario.StartsWith("dirty-", StringComparison.Ordinal))
            {
                await Run("owned skill reopen example 1.0.0");
                editing.Guidance.Text = "unsaved draft must not be lost";
                if (scenario is "dirty-save")
                {
                    editing.VersionInput.Text = "1.0.2";
                    await Run("owned skill validate");
                }
            }
            Task operation;
            if (scenario is "native")
            {
                Choose(editing, "Delete");
                operation = controller.ActiveOperation;
            }
            else if (scenario is "voice")
            {
                f.Voice.PublishTranscript(f.Main.AssistantName + ", owned skill delete example 1.0.0");
                await next.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Token);
                operation = controller.ActiveOperation;
            }
            else { operation = Run("owned skill delete example 1.0.0"); }
            if (scenario.StartsWith("dirty-", StringComparison.Ordinal))
            {
                var dirty = await Review(operation);
                dirty.Title.Should().Be("Unsaved skill draft");
                Choose(dirty, scenario is "dirty-cancel" ? "Cancel" : scenario is "dirty-save" ? "Save" : "Discard");
                if (scenario is "dirty-cancel")
                {
                    await operation;
                    editing.Guidance.Text.Should().Be("unsaved draft must not be lost");
                    (await store.Read("example", "1.0.0", f.Token))!.IsSameRevision(saved).Should().BeTrue();
                    return;
                }
                if (scenario is "dirty-save")
                {
                    Choose(await Review(operation), "Continue to separate native publication confirmation");
                    Choose(await Review(operation), "Publish exact new version");
                }
            }
            var sourceReview = scenario is "native" or "voice" ? await next.Task : await Review(operation);
            if (scenario is "native" or "voice") { next = new(TaskCreationOptions.RunContinuationsAsynchronously); }
            sourceReview.Title.Should().Be("Review exact owned skill source deletion");
            sourceReview.GetLogicalDescendants().OfType<TextBox>().Select(box => box.Text)
                .Should().Equal(document.ManifestText, document.Guidance);
            var disclosure = string.Join("\n", sourceReview.GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text));
            disclosure.Should().Contain(store.Destination(document)).And.Contain("references: none")
                .And.Contain("Immutable admitted tasks are not cancelled").And.Contain("independent grants");
            var originalGuidance = editing.Guidance.Text;
            if (scenario is "draft") { editing.Guidance.Text = "changed draft"; }
            if (scenario is "selection") { editing.Saved.SelectedIndex = 0; }
            if (scenario is "owner") { ownership++; }
            if (scenario is "control") { f.Access.SetOpen(false); }
            if (scenario is "privacy") { f.Main.CloseForObservedPrivacyEvent("Synthetic deletion input withdrawal", hidePresentation: true); }
            if (scenario is "source") { await File.WriteAllTextAsync(Path.Combine(store.Destination(document), "instructions.md"), "changed source", f.Token); }
            var dismissed = false;
            if (scenario is "escape-review") { dismissed = Escape(sourceReview); }
            else { Choose(sourceReview, scenario is "cancel-review" ? "Cancel" : "Continue to separate deletion confirmation"); }
            if (scenario is "draft" or "selection" or "owner" or "control" or "privacy" or "source" or "cancel-review" or "escape-review")
            {
                await operation;
                File.Exists(Path.Combine(store.Destination(document), "manifest.yaml")).Should().BeTrue();
                if (scenario is "privacy") { await f.Main.PrivacyClosureTask; }
                if (scenario is "escape-review")
                {
                    dismissed.Should().BeTrue("Escape rejects the exact review without entering confirmation");
                    next.Task.IsCompleted.Should().BeFalse();
                    editing.IsVisible.Should().BeTrue();
                    editing.Guidance.Text.Should().Be(originalGuidance);
                    (await store.Read("example", "1.0.0", f.Token))!.IsSameRevision(saved).Should().BeTrue();
                    (await store.Read("another", "1.0.0", f.Token))!.IsSameRevision(other).Should().BeTrue();
                }
                return;
            }
            var confirmation = await Review(operation);
            confirmation.Title.Should().Be("Confirm exact owned skill source deletion");
            Directory.Exists(store.Destination(document)).Should().BeTrue();
            if (scenario is "uncertain")
            { store.Checkpoint = point => { if (point is "after-delete") { throw new IOException("fixture readback unavailable"); } }; }
            if (scenario is "escape-confirm") { dismissed = Escape(confirmation); }
            else { Choose(confirmation, scenario is "cancel-confirm" ? "Cancel" : "Delete only this exact owned source"); }
            await operation;
            store.Checkpoint = null;
            if (scenario is "uncertain")
            {
                string.Join("\n", editing.GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text))
                    .Should().Contain("Mutation is held").And.Contain("Do not retry");
                await Run("owned skill delete example 1.0.0");
                next.Task.IsCompleted.Should().BeFalse();
            }
            else if (scenario is "cancel-confirm" or "escape-confirm")
            {
                (await store.Read("example", "1.0.0", f.Token))!.IsSameRevision(saved).Should().BeTrue();
                if (scenario is "escape-confirm")
                {
                    dismissed.Should().BeTrue("Escape rejects the separate confirmation without deletion");
                    next.Task.IsCompleted.Should().BeFalse();
                    editing.IsVisible.Should().BeTrue();
                    editing.Guidance.Text.Should().Be(originalGuidance);
                    (await store.Read("another", "1.0.0", f.Token))!.IsSameRevision(other).Should().BeTrue();
                }
            }
            else
            {
                (await store.Read("example", "1.0.0", f.Token)).Should().BeNull();
                var rows = await store.List(f.Token);
                rows.Should().HaveCount(scenario is "dirty-save" ? 2 : 1);
                rows.Should().Contain(row => row.IsSameRevision(other));
                if (scenario is "dirty-save")
                { (await store.Read("example", "1.0.2", f.Token))!.Document.Guidance.Should().Be("unsaved draft must not be lost"); }
                editing.Saved.Items.Should().BeEmpty();
            }
        });
    }
}

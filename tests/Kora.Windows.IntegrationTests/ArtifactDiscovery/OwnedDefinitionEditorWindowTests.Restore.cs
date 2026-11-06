using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Artifacts;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class OwnedDefinitionEditorWindowTests
{
    [Fact]
    public async Task ConfirmedConflictSaveRestoresTheExactReviewedReplacedSourceNotAnEarlierEditorBaseline()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false);
            var old = f.Editor!.Source.Text!;
            var external = old + "\r\nPRIVATE_EXTERNAL_CURRENT";
            File.WriteAllText(f.SourcePath, external);
            f.Editor.Source.Text = old + "\r\nPRIVATE_KORA_DRAFT";
            var first = NextRestoreDialog();
            using var firstSubscription = first.Subscription;
            var save = f.Run("instructions save");
            var originalReview = await AwaitRestoreDialog(f, first.Task, save);
            var conflict = NextRestoreDialog();
            using var conflictSubscription = conflict.Subscription;
            RebaseClick(originalReview, "Confirm save");
            var conflictReview = await AwaitRestoreDialog(f, conflict.Task, save);
            var overwrite = NextRestoreDialog();
            using var overwriteSubscription = overwrite.Subscription;
            RebaseClick(conflictReview, "Overwrite with Kora changes");
            var overwriteReview = await AwaitRestoreDialog(f, overwrite.Task, save);
            overwriteReview.GetVisualDescendants().OfType<TextBox>().First().Text.Should().Be(external);
            RebaseClick(overwriteReview, "Confirm save");
            await save;
            f.Editor.RestorePrevious.IsEnabled.Should().BeTrue();
            var (preview, restoring) = await BeginRestore(f);
            preview.GetVisualDescendants().OfType<TextBox>().Last().Text.Should().Be(external);
            var confirmation = NextRestoreDialog();
            using var confirmationSubscription = confirmation.Subscription;
            RebaseClick(preview, "Review separate Save confirmation");
            RebaseClick(await AwaitRestoreDialog(f, confirmation.Task, restoring), "Confirm save");
            await restoring;
            File.ReadAllText(f.SourcePath).Should().Be(external);
        });
    }

    [Theory]
    [InlineData("opened")]
    [InlineData("created")]
    [InlineData("legacy")]
    [InlineData("unchanged")]
    [InlineData("unconfirmed")]
    [InlineData("recovery")]
    [InlineData("reload")]
    [InlineData("source-before-preview")]
    [InlineData("reconcile-change")]
    public async Task RestoreAvailabilityRequiresKnownChangedCompatibleSaveInThisEditorAndExplicitRecovery(string state)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, state is "created");
            f.Editor!.RestorePrevious.IsEnabled.Should().BeFalse();
            if (state is "opened") { return; }
            if (state is "legacy")
            {
                File.WriteAllText(f.SourcePath, f.Editor.Source.Text!.Replace("kora-format: 1\n", "", StringComparison.Ordinal));
                await f.Run("instructions reload");
            }
            if (state is "reload" or "source-before-preview" or "recovery")
            { await SaveRestoreFixtureDraft(f, f.Editor.Source.Text + "\r\nSaved once"); }
            if (state is "reload")
            { await f.Run("instructions reload"); f.Editor.RestorePrevious.IsEnabled.Should().BeFalse(); return; }
            if (state is "source-before-preview")
            {
                File.AppendAllText(f.SourcePath, "External");
                var bytes = File.ReadAllBytes(f.SourcePath);
                var writes = f.Store.Writes;
                await f.Run("instructions restore");
                File.ReadAllBytes(f.SourcePath).Should().Equal(bytes);
                f.Store.Writes.Should().Be(writes);
                f.Editor.RestorePrevious.IsEnabled.Should().BeFalse();
                return;
            }
            if (state is "unconfirmed") { f.FailAfterInstructionPublication(); }
            if (state is "reconcile-change") { f.Store.AfterWrite = () => File.AppendAllText(f.SourcePath, "External after native readback"); }
            if (state is "recovery") { f.Store.DenyAccess = true; }
            if (state is not "unchanged") { f.Editor.Source.Text += "\r\nNew change"; }
            var next = NextRestoreDialog();
            using var subscription = next.Subscription;
            var save = f.Run("instructions save");
            var dialog = await AwaitRestoreDialog(f, next.Task, save);
            RebaseClick(dialog, "Confirm save");
            await save;
            f.Editor.RestorePrevious.IsEnabled.Should().BeFalse();
            if (state is "unconfirmed") { f.Status.Should().Contain("publication occurred").And.Contain("unconfirmed"); }
            if (state is "recovery")
            {
                f.Store.DenyAccess = false;
                var discard = NextRestoreDialog();
                using var discardSubscription = discard.Subscription;
                var loading = f.Run("instructions reload");
                var discardReview = await AwaitRestoreDialog(f, discard.Task, loading);
                RebaseClick(discardReview, "Discard draft and continue");
                await loading;
                await SaveRestoreFixtureDraft(f, f.Editor.Source.Text + "\r\nRecovered separate Save");
                f.Editor.RestorePrevious.IsEnabled.Should().BeTrue();
            }
        });
    }

    [Fact]
    public async Task RestoreCannotClaimCanonicalReconciliationOrRenewProofAfterAnotherWriteFollowingNativeReadback()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false);
            var previous = f.Editor!.Source.Text!;
            await SaveRestoreFixtureDraft(f, previous + "\r\nSaved");
            f.Store.AfterWrite = () => File.AppendAllText(f.SourcePath, "\r\nExternal after native readback");
            var (preview, operation) = await BeginRestore(f);
            var next = NextRestoreDialog();
            using var subscription = next.Subscription;
            RebaseClick(preview, "Review separate Save confirmation");
            RebaseClick(await AwaitRestoreDialog(f, next.Task, operation), "Confirm save");
            await operation;
            f.Status.Should().Contain("Atomic source publication occurred").And.Contain("unconfirmed")
                .And.Contain("Explicitly Reload/reconcile").And.NotContain("Previous complete source saved/read back");
            f.Editor.RestorePrevious.IsEnabled.Should().BeFalse();
            File.ReadAllText(f.SourcePath).Should().Be(previous + "\r\nExternal after native readback");
            f.Editor.Source.Text.Should().Be(previous + "\r\nSaved");
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task LastConfirmedSaveRestoresWholeExactSourceThroughSeparateNativeSaveAsNewDisabledRevision(bool bom, bool native)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false);
            var editor = f.Editor!;
            editor.RestorePrevious.IsEnabled.Should().BeFalse();
            var previous = ArtifactFileDocument.Encode(
                "---\r\nkora-format: 1\r\nname: example\r\ndescription: 'Previous description'\r\nattribution: \"Original credit\"\r\n---\r\nPRIVATE_PREVIOUS_FULL_BODY\r\n", bom);
            File.WriteAllBytes(f.SourcePath, previous);
            await f.Run("instructions reload");
            editor.RestorePrevious.IsEnabled.Should().BeFalse();
            var original = (await f.Store.Read("example", f.Session.Token))!;
            await f.Actions.Activate(original, _ => Task.FromResult(true), () => true, f.Session.Token);
            await SaveRestoreFixtureDraft(f, "---\nkora-format: 1\nname: example\ndescription: Current metadata\n---\nPRIVATE_CURRENT_FULL_BODY\n");
            var current = (await f.Store.Read("example", f.Session.Token))!;
            editor.RestorePrevious.IsEnabled.Should().BeTrue();
            f.Status.Should().Contain("Last confirmed save in this editor");
            var (preview, operation) = await BeginRestore(f, native);
            var panes = preview.GetVisualDescendants().OfType<TextBox>().ToArray();
            panes.Select(box => box.Text).Should().Equal(current.Document.Text, original.Document.Text);
            preview.GetVisualDescendants().OfType<TextBlock>().First().Text.Should().Contain("NEW disabled")
                .And.Contain("BOM=" + bom).And.Contain("not content CAS").And.Contain(current.Revision);
            File.ReadAllBytes(f.SourcePath).Should().Equal(current.Document.CopyBytes());
            var next = NextRestoreDialog();
            RebaseClick(preview, "Review separate Save confirmation");
            var confirmation = await AwaitRestoreDialog(f, next.Task, operation);
            using var subscription = next.Subscription;
            confirmation.Title.Should().Be("Save exact owned Instruction");
            confirmation.GetVisualDescendants().OfType<TextBox>().Select(box => box.Text)
                .Should().Equal(current.Document.Text, original.Document.Text);
            File.ReadAllBytes(f.SourcePath).Should().Equal(current.Document.CopyBytes());
            RebaseClick(confirmation, "Confirm save");
            await operation;
            var restored = (await f.Store.Read("example", f.Session.Token))!;
            restored.Revision.Should().NotBe(original.Revision).And.NotBe(current.Revision);
            restored.Document.CopyBytes().Should().Equal(previous);
            File.ReadAllBytes(f.SourcePath).Should().Equal(previous);
            editor.Source.Text.Should().Be(original.Document.Text);
            f.Actions.IsReviewedUseEnabled(restored).Should().BeFalse();
            f.Status.Should().Contain("NEW disabled revision").And.Contain("No old review");
            panes.Should().OnlyContain(box => box.Text == string.Empty);
            Directory.EnumerateFiles(Path.GetDirectoryName(f.SourcePath)!).Should().ContainSingle()
                .Which.Should().Be(f.SourcePath);
            editor.Close();
            await f.Run("instructions inspect example");
            f.Editor!.RestorePrevious.IsEnabled.Should().BeFalse();
        });
    }

    [Theory]
    [InlineData("preview")]
    [InlineData("confirmation")]
    [InlineData("dirty-cancel")]
    [InlineData("dirty-discard")]
    [InlineData("dirty-save")]
    public async Task RestoreCancellationAndDirtyDraftChoicesAreSeparateAndNeverSilentlyDiscard(string choice)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false);
            var previous = f.Editor!.Source.Text!;
            await SaveRestoreFixtureDraft(f, previous + "\r\nPRIVATE_SAVED_EDIT");
            var current = File.ReadAllBytes(f.SourcePath);
            var dirty = f.Editor.Source.Text + "\r\nPRIVATE_UNSAVED_DRAFT";
            if (choice.StartsWith("dirty-", StringComparison.Ordinal)) { f.Editor.Source.Text = dirty; }
            var (dialog, operation) = await BeginRestore(f);
            if (choice is "dirty-cancel" or "preview")
            { RebaseClick(dialog, "Cancel"); }
            else
            {
                var next = NextRestoreDialog();
                if (choice is "dirty-save") { RebaseClick(dialog, "Save current draft"); }
                else if (choice is "dirty-discard") { RebaseClick(dialog, "Discard current draft"); }
                else { RebaseClick(dialog, "Review separate Save confirmation"); }
                var review = await AwaitRestoreDialog(f, next.Task, operation);
                using var subscription = next.Subscription;
                if (choice is "dirty-save") { RebaseClick(review, "Confirm save"); }
                else { RebaseClick(review, "Cancel"); }
            }
            await operation;
            if (choice is "dirty-save")
            {
                File.ReadAllBytes(f.SourcePath).Should().Equal(ArtifactFileDocument.Encode(dirty, false));
                f.Editor.Source.Text.Should().Be(dirty);
                f.Editor.RestorePrevious.IsEnabled.Should().BeTrue();
            }
            else
            {
                File.ReadAllBytes(f.SourcePath).Should().Equal(current);
                f.Editor.Source.Text.Should().Be(choice is "dirty-cancel" ? dirty : previous + "\r\nPRIVATE_SAVED_EDIT");
            }
        });
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("draft-aba")]
    [InlineData("input")]
    [InlineData("name")]
    [InlineData("kind")]
    [InlineData("call")]
    [InlineData("owner")]
    [InlineData("privacy")]
    [InlineData("dispose")]
    [InlineData("cancel")]
    [InlineData("catalogue-aba")]
    [InlineData("reload-failure")]
    [InlineData("disk")]
    [InlineData("identity")]
    [InlineData("rename")]
    [InlineData("deleted")]
    [InlineData("access")]
    [InlineData("occupied")]
    [InlineData("expiry")]
    public async Task RestoreRetiresStaleAuthorityOrSourceAndLateNativeConfirmationCannotWrite(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false);
            await SaveRestoreFixtureDraft(f, f.Editor!.Source.Text + "\r\nPRIVATE_SAVED");
            var current = File.ReadAllBytes(f.SourcePath);
            var (preview, operation) = await BeginRestore(f);
            var next = NextRestoreDialog();
            RebaseClick(preview, "Review separate Save confirmation");
            var confirmation = await AwaitRestoreDialog(f, next.Task, operation);
            using var subscription = next.Subscription;
            var confirm = confirmation.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Confirm save"));
            var draft = f.Editor.Source.Text;
            FileStream? occupied = null;
            switch (change)
            {
                case "draft": f.Editor.Source.Text += "Changed"; break;
                case "draft-aba": f.Editor.Source.Text = "Changed"; f.Editor.Source.Text = draft; break;
                case "input": f.Session.Main.CommandText = "unrelated"; break;
                case "name": f.Editor.NameInput.Text = "other"; break;
                case "kind": f.Editor.SelectKind(ArtifactKind.Prompt); break;
                case "call": await f.Run("call on"); break;
                case "owner": f.Ownership++; break;
                case "privacy":
                    f.Session.Main.CloseForObservedPrivacyEvent("Synthetic restore privacy closure", hidePresentation: true);
                    break;
                case "dispose": f.DisposeController(); break;
                case "cancel": await f.Run("instructions cancel"); break;
                case "catalogue-aba":
                    var candidates = f.Catalogue.Candidates;
                    f.Catalogue.Publish(candidates.Where(item => !string.Equals(item.OwnedInstructionName, "example", StringComparison.Ordinal)).ToArray());
                    f.Catalogue.Publish(candidates);
                    break;
                case "reload-failure": f.Catalogue.ReportReloadFailure(new IOException("Unavailable")); break;
                case "disk": File.AppendAllText(f.SourcePath, "External"); current = File.ReadAllBytes(f.SourcePath); break;
                case "identity":
                    var replacement = f.SourcePath + ".replacement";
                    File.WriteAllBytes(replacement, current);
                    File.Move(replacement, f.SourcePath, overwrite: true);
                    break;
                case "rename": File.Move(f.SourcePath, f.SourcePath + ".renamed"); break;
                case "deleted": File.Delete(f.SourcePath); break;
                case "access": f.Store.DenyAccess = true; break;
                case "occupied": occupied = new(f.SourcePath, FileMode.Open, FileAccess.Read, FileShare.None); break;
                case "expiry": f.ExpireRestore(); break;
            }
            try
            {
                confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await operation;
            }
            finally { occupied?.Dispose(); }
            if (change is not ("rename" or "deleted")) { File.ReadAllBytes(f.SourcePath).Should().Equal(current); }
            f.Status.Should().NotContain("Previous complete source saved/read back");
            confirmation.GetVisualDescendants().OfType<TextBox>().Should().OnlyContain(box => box.Text == string.Empty);
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (change is "privacy") { await f.Session.Main.PrivacyClosureTask; }
        });
    }

    private static async Task SaveRestoreFixtureDraft(DraftValidationFixture f, string draft)
    {
        f.Editor!.Source.Text = draft;
        var next = NextRestoreDialog();
        using var subscription = next.Subscription;
        var save = f.Run("instructions save");
        var review = await AwaitRestoreDialog(f, next.Task, save);
        RebaseClick(review, "Confirm save");
        await save;
        f.Status.Should().Contain("Complete bytes saved/read back");
    }

    private static async Task<(Window Review, Task Operation)> BeginRestore(DraftValidationFixture f, bool native = false)
    {
        var next = NextRestoreDialog();
        using var subscription = next.Subscription;
        Task operation;
        if (native)
        {
            f.Editor!.RestorePrevious.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            operation = f.WaitForEditorOperation();
        }
        else { operation = f.Run("instructions restore"); }
        return (await AwaitRestoreDialog(f, next.Task, operation), operation);
    }

    private static async Task<Window> AwaitRestoreDialog(DraftValidationFixture f, Task<Window> shown, Task operation)
    {
        var completed = await Task.WhenAny(shown, operation);
        completed.Should().BeSameAs(shown, "the admitted operation must reach review; status: {0}", f.Status);
        return await shown.WaitAsync(f.Session.Token);
    }

    private static (Task<Window> Task, IDisposable Subscription) NextRestoreDialog()
    {
        var completion = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        { if (window is not OwnedInstructionEditorWindow) { completion.TrySetResult(window); } });
        return (completion.Task, subscription);
    }
}
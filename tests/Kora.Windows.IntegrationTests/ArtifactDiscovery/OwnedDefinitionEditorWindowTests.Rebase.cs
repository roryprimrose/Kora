using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Artifacts;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class OwnedDefinitionEditorWindowTests
{
    [Theory]
    [InlineData(ArtifactKind.Instruction, "changed", true)]
    [InlineData(ArtifactKind.Prompt, "unchanged", false)]
    [InlineData(ArtifactKind.Instruction, "invalid", false)]
    [InlineData(ArtifactKind.Prompt, "whitespace", false)]
    public async Task RebaseReviewsThreeCompleteFrozenBodiesAndAdoptsOnlyLatestBaseline(ArtifactKind kind, string state, bool native)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(kind, false);
            var editor = f.Editor!;
            var original = f.Source!;
            var latest = state is "unchanged" ? original : original with { Document = Document(kind, "PRIVATE_LATEST_BODY") };
            f.Store.ReadOverride = (_, _) => Task.FromResult<OwnedInstructionSnapshot?>(latest);
            editor.Source.Text = state switch
            {
                "invalid" => "PRIVATE_INVALID \r\n \u03a9 \U0001f642 ",
                "whitespace" => " \t\r\n \n ",
                _ => original.Document.Text + "\r\n  PRIVATE_DRAFT \u03a9 \U0001f642 \r\n ",
            };
            var draft = editor.Source.Text;
            var candidates = f.Catalogue.Candidates;
            var bytes = File.ReadAllBytes(f.SourcePath);
            var (review, operation) = await BeginRebase(f, native);
            var panes = review.GetVisualDescendants().OfType<TextBox>().ToArray();
            panes.Should().HaveCount(3).And.OnlyContain(box => box.IsReadOnly && box.AcceptsReturn && !box.IsUndoEnabled);
            panes.Select(box => box.Text).Should().Equal(original.Document.Text, latest.Document.Text, draft);
            panes.Select(AutomationProperties.GetName).Should().OnlyContain(name => !string.IsNullOrEmpty(name));
            review.GetVisualDescendants().OfType<TextBlock>().First().Text.Should().Contain(original.Revision)
                .And.Contain(latest.Revision).And.Contain("No automatic merge", because: "the complete raw draft must never be silently rewritten");
            f.Store.Reads.Should().Be(1);
            foreach (var pane in panes)
            {
                var copy = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.C, KeyModifiers = KeyModifiers.Control };
                pane.RaiseEvent(copy);
                copy.Handled.Should().BeTrue();
                pane.Copy();
                var menu = new ContextRequestedEventArgs();
                pane.RaiseEvent(menu);
                menu.Handled.Should().BeTrue();
            }
            RebaseClick(review, "Adopt latest baseline only");
            await operation;
            editor.Source.Text.Should().Be(draft);
            f.Status.Should().Contain("baseline adopted in memory").And.Contain("Save is separate");
            editor.ReviewUse.IsEnabled.Should().BeFalse();
            editor.DisableReviewedUse.IsEnabled.Should().BeFalse();
            f.Store.Reads.Should().Be(2);
            f.Store.Writes.Should().Be(0);
            f.Store.Lists.Should().Be(0);
            f.Catalogue.Candidates.Should().BeSameAs(candidates);
            File.ReadAllBytes(f.SourcePath).Should().Equal(bytes);
            panes.Should().OnlyContain(box => box.Text == string.Empty);
            review.Content.Should().BeNull();
            await AssertFrozenBaseline(f, latest.Document.Text, draft!);
        });
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("escape")]
    [InlineData("close-review")]
    [InlineData("control-cancel")]
    public async Task RebaseCancellationRetainsOldBaselineAndRawDraftAndClearsRetiredReview(string cancel)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false);
            var original = f.Source!;
            f.Store.ReadOverride = (_, _) => Task.FromResult<OwnedInstructionSnapshot?>(original with { Document = Document(ArtifactKind.Instruction, "Latest") });
            f.Editor!.Source.Text += "\r\n PRIVATE_DRAFT ";
            var draft = f.Editor.Source.Text!;
            var (review, operation) = await BeginRebase(f);
            var panes = review.GetVisualDescendants().OfType<TextBox>().ToArray();
            var confirm = review.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Adopt latest baseline only"));
            if (cancel is "cancel") { RebaseClick(review, "Cancel"); }
            if (cancel is "escape") { review.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }); }
            if (cancel is "close-review") { review.Close(); }
            if (cancel is "control-cancel") { await f.Run("instructions cancel"); }
            await operation;
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            f.Editor.Source.Text.Should().Be(draft);
            f.Status.Should().NotContain("baseline adopted");
            f.Store.Reads.Should().Be(1);
            f.Store.Writes.Should().Be(0);
            panes.Should().OnlyContain(box => box.Text == string.Empty);
            await AssertFrozenBaseline(f, original.Document.Text, draft);
        });
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("draft-aba")]
    [InlineData("name")]
    [InlineData("kind")]
    [InlineData("selection")]
    [InlineData("input")]
    [InlineData("copy")]
    [InlineData("catalogue-aba")]
    [InlineData("failure")]
    [InlineData("filter")]
    [InlineData("owner")]
    [InlineData("privacy")]
    [InlineData("close-editor")]
    public async Task RebaseRetiresOnEditorSourceControlAndPrivateChangesAndLateConfirmCannotAdopt(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false);
            var editor = f.Editor!;
            var original = f.Source!;
            var draft = original.Document.Text + "\r\n PRIVATE_DRAFT ";
            editor.Source.Text = draft;
            f.Store.ReadOverride = (_, _) => Task.FromResult<OwnedInstructionSnapshot?>(original with { Document = Document(ArtifactKind.Instruction, "Latest") });
            var (review, operation) = await BeginRebase(f);
            var panes = review.GetVisualDescendants().OfType<TextBox>().ToArray();
            var confirm = review.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Adopt latest baseline only"));
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            review.Closed += (_, _) => closed.TrySetResult();
            switch (change)
            {
                case "draft": editor.Source.Text += "Changed"; break;
                case "draft-aba": editor.Source.Text = "Changed"; editor.Source.Text = draft; break;
                case "name": editor.NameInput.Text = "other"; break;
                case "kind": editor.SelectKind(ArtifactKind.Prompt); break;
                case "selection": editor.Files.SelectedItem = null; break;
                case "input": f.Session.Main.CommandText = "unrelated"; break;
                case "copy": editor.CopyNameInput.Text = "other"; break;
                case "catalogue-aba":
                    var prior = f.Catalogue.Candidates;
                    f.Catalogue.Publish([]);
                    f.Catalogue.Publish(prior);
                    break;
                case "failure": f.Catalogue.ReportReloadFailure(new IOException("Unavailable")); break;
                case "filter": editor.QueryInput.Text = "other"; break;
                case "owner": f.Ownership++; break;
                case "privacy": f.Session.Main.CloseForObservedPrivacyEvent("Synthetic rebase closure", hidePresentation: true); break;
                case "close-editor": editor.Close(); break;
            }
            await closed.Task.WaitAsync(TestContext.Current.CancellationToken);
            await operation;
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            f.Status.Should().NotContain("baseline adopted");
            f.Store.Reads.Should().Be(1);
            f.Store.Writes.Should().Be(0);
            panes.Should().OnlyContain(box => box.Text == string.Empty);
            review.Content.Should().BeNull();
            if (change is "privacy")
            { editor.Source.Text.Should().BeEmpty(); editor.IsVisible.Should().BeFalse(); }
            else if (change is not "draft") { editor.Source.Text.Should().Be(draft); }
        });
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("changed-during-review")]
    [InlineData("deleted-during-review")]
    [InlineData("kind")]
    [InlineData("new")]
    [InlineData("unavailable")]
    [InlineData("uncertain")]
    public async Task RebaseFailuresNeverWriteAdoptOrClearPublicationUncertainty(string failure)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, failure is "new");
            var editor = f.Editor!;
            if (failure is "uncertain")
            {
                var retained = editor.Source.Text;
                editor.Source.Text = "Invalid save";
                await f.Run("instructions save");
                editor.Source.Text = retained;
            }
            if (failure is "unavailable") { f.Catalogue.ReportReloadFailure(new IOException("Unavailable")); }
            var original = f.Source;
            editor.Source.Text += "\r\n PRIVATE_DRAFT ";
            var draft = editor.Source.Text;
            var reads = 0;
            f.Store.ReadOverride = (_, _) =>
            {
                reads++;
                var source = failure is "kind" ? original! with { Document = Document(ArtifactKind.Prompt, "Foreign") }
                    : original! with { Document = Document(ArtifactKind.Instruction, reads == 1 ? "Latest" : "Changed again") };
                return Task.FromResult<OwnedInstructionSnapshot?>(failure is "deleted" || failure is "deleted-during-review" && reads > 1 ? null : source);
            };
            if (failure is "changed-during-review" or "deleted-during-review")
            {
                var (review, operation) = await BeginRebase(f);
                RebaseClick(review, "Adopt latest baseline only");
                await operation;
            }
            else { await f.Run("instructions rebase"); }
            f.Status.Should().NotContain("baseline adopted");
            editor.Source.Text.Should().Be(draft);
            f.Store.Writes.Should().Be(0);
            if (failure is "new" or "unavailable" or "uncertain") { f.Store.Reads.Should().Be(0); }
            if (failure is "uncertain")
            {
                await f.Run("instructions save");
                f.Status.Should().Contain("Reload/reconcile first");
                f.Store.Reads.Should().Be(0);
            }
        });
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("owner")]
    [InlineData("privacy")]
    [InlineData("catalogue")]
    [InlineData("close")]
    public async Task LateRebaseAdmissionCannotOpenAReviewOrReadAfterLifetimeChanges(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false);
            var editor = f.Editor!;
            var draft = editor.Source.Text!;
            f.Sessions.PauseNext = true;
            var operation = f.Run("instructions rebase");
            await f.Sessions.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            if (change is "draft") { editor.Source.Text += "Edited"; }
            if (change is "owner") { f.Ownership++; }
            if (change is "privacy") { f.Session.Main.CloseForObservedPrivacyEvent("Synthetic late rebase closure", hidePresentation: true); }
            if (change is "catalogue") { f.Catalogue.Publish(f.Catalogue.Candidates); }
            if (change is "close") { editor.Close(); }
            f.Sessions.Release.TrySetResult();
            await operation;
            f.Store.Reads.Should().Be(0);
            f.Store.Writes.Should().Be(0);
            f.Status.Should().NotContain("baseline adopted");
            if (change is "privacy") { editor.Source.Text.Should().BeEmpty(); }
            else { editor.Source.Text.Should().Be(change is "draft" ? draft + "Edited" : draft); }
        });
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("owner")]
    [InlineData("privacy")]
    public async Task ReentrantRebaseOpeningCannotLeavePrivateBodiesVisibleOrAdoptALateBaseline(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false);
            var editor = f.Editor!;
            var draft = editor.Source.Text!;
            Window? review = null;
            using var subscription = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window is not OwnedInstructionRebaseReviewWindow) { return; }
                review = window;
                if (change is "draft") { editor.Source.Text += "Reentrant edit"; }
                if (change is "owner") { f.Ownership++; }
                if (change is "privacy") { f.Session.Main.CloseForObservedPrivacyEvent("Synthetic reentrant rebase closure", hidePresentation: true); }
            });
            await f.Run("instructions rebase");
            review.Should().NotBeNull();
            review!.IsVisible.Should().BeFalse();
            review.Content.Should().BeNull();
            f.Status.Should().NotContain("baseline adopted");
            f.Store.Writes.Should().Be(0);
            if (change is "privacy") { editor.Source.Text.Should().BeEmpty(); }
            else { editor.Source.Text.Should().Be(change is "draft" ? draft + "Reentrant edit" : draft); }
        });
    }

    private static async Task<(Window Review, Task Operation)> BeginRebase(DraftValidationFixture fixture, bool native = false)
    {
        var opened = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        { if (window is OwnedInstructionRebaseReviewWindow) { opened.TrySetResult(window); } });
        Task operation;
        if (native)
        {
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            foreach (var block in fixture.Editor!.GetVisualDescendants().OfType<TextBlock>())
            {
                block.PropertyChanged += (_, _) =>
                { if (fixture.Status.Contains("baseline adopted in memory", StringComparison.Ordinal)) { completed.TrySetResult(); } };
            }
            RebaseClick(fixture.Editor, "Rebase");
            operation = completed.Task;
        }
        else { operation = fixture.Run("Kora, " + fixture.Prefix + " rebase"); }
        (await Task.WhenAny(operation, opened.Task)).Should().BeSameAs(opened.Task, "Rebase must reach review: {0}", fixture.Status);
        return (await opened.Task.WaitAsync(TestContext.Current.CancellationToken), operation);
    }

    private static void RebaseClick(Window window, string label) =>
        window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, label))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task AssertFrozenBaseline(DraftValidationFixture fixture, string expected, string draft)
    {
        fixture.Store.DenyAccess = true;
        Window? comparison = null;
        using var subscription = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        { if (window.Title?.StartsWith("Passive owned", StringComparison.Ordinal) == true) { comparison = window; } });
        await fixture.Run(fixture.Prefix + " compare");
        comparison.Should().NotBeNull();
        var bodies = comparison!.GetVisualDescendants().OfType<TextBox>().Where(box => box.IsReadOnly).ToArray();
        bodies.Select(box => box.Text).Should().Equal(expected, draft);
        comparison.Close();
    }
}

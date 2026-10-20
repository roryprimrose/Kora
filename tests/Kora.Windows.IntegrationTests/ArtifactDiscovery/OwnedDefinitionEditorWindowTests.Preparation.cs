using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Core.Artifacts;
using Kora.Windows.Artifacts;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class OwnedDefinitionEditorWindowTests
{
    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task ExactPreparationIsReadFreeRetiresReadinessAndPreservesDirtySourceAndEveryOtherFilter(ArtifactKind sourceKind)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(sourceKind, false);
            var editor = f.Editor!;
            editor.Source.Text += "\r\nPRIVATE_DIRTY_SOURCE\r\n";
            editor.CopyNameInput.Text = "uncommitted-copy";
            editor.DigestInput.Text = new string('a', 64);
            editor.RevisionInput.Text = "invalid-unconfirmed-revision-draft";
            editor.ObservedFromInput.Text = "unfinished-from";
            editor.ObservedUntilInput.Text = "unfinished-until";
            editor.OrderInput.SelectedItem = OwnedDefinitionInventoryOrder.DisplayNameDescending;
            editor.UseStateInput.SelectedItem = OwnedDefinitionUseFilter.UseEnabled;
            var draft = editor.Source.Text;
            var name = editor.NameInput.Text;
            var bytes = File.ReadAllBytes(f.SourcePath);
            f.Store.DenyAccess = true;
            var catalogue = f.Catalogue.Candidates;
            var queryKind = sourceKind == ArtifactKind.Instruction ? ArtifactKind.Prompt : ArtifactKind.Instruction;
            await PrepareOwnedQuery(f, queryKind, "private-query-id");
            f.Session.Main.ResponseTitle.Should().Contain("PREPARED");
            editor.InventoryKindInput.SelectedItem.Should().Be(queryKind);
            editor.QueryModeInput.SelectedItem.Should().Be(OwnedDefinitionInventoryQueryMode.ExactOwnedId);
            editor.QueryInput.Text.Should().Be("private-query-id");
            editor.QueryInput.IsKeyboardFocusWithin.Should().BeTrue();
            AutomationProperties.GetName(editor.QueryInput).Should().Contain("canonical lowercase");
            AutomationProperties.GetName(editor.InventoryKindInput).Should().Contain("does not change");
            editor.KindInput.SelectedItem.Should().Be(sourceKind);
            editor.Source.Text.Should().Be(draft);
            editor.NameInput.Text.Should().Be(name);
            editor.CopyNameInput.Text.Should().Be("uncommitted-copy");
            editor.DigestInput.Text.Should().Be(new string('a', 64));
            editor.RevisionInput.Text.Should().Be("invalid-unconfirmed-revision-draft");
            editor.ObservedFromInput.Text.Should().Be("unfinished-from");
            editor.ObservedUntilInput.Text.Should().Be("unfinished-until");
            editor.OrderInput.SelectedItem.Should().Be(OwnedDefinitionInventoryOrder.DisplayNameDescending);
            editor.UseStateInput.SelectedItem.Should().Be(OwnedDefinitionUseFilter.UseEnabled);
            editor.Files.ItemsSource.Should().BeNull();
            editor.Files.SelectedItem.Should().BeNull();
            editor.NextPage.IsEnabled.Should().BeFalse();
            editor.ReviewUse.IsEnabled.Should().BeFalse();
            editor.DisableReviewedUse.IsEnabled.Should().BeFalse();
            f.Status.Should().Contain("AND-composed");
            f.Catalogue.Candidates.Should().BeSameAs(catalogue);
            f.Store.Reads.Should().Be(0); f.Store.Lists.Should().Be(0); f.Store.Writes.Should().Be(0);
            File.ReadAllBytes(f.SourcePath).Should().Equal(bytes);
        });
    }

    [Fact]
    public async Task FreshEditorPreparationDoesNotListReadOrChangeExistingSourceDefaults()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false);
            var previous = f.Editor!;
            previous.Close();
            f.Store.DenyAccess = true;
            await PrepareOwnedQuery(f, ArtifactKind.Prompt, "missing");
            var editor = f.Editor!;
            editor.Should().NotBeSameAs(previous);
            editor.KindInput.SelectedItem.Should().Be(ArtifactKind.Instruction);
            editor.Source.Text.Should().BeNullOrEmpty();
            editor.NameInput.Text.Should().BeNullOrEmpty();
            editor.QueryInput.Text.Should().Be("missing");
            editor.OrderInput.SelectedItem.Should().Be(OwnedDefinitionInventoryOrder.FileNameAscending);
            editor.UseStateInput.SelectedItem.Should().Be(OwnedDefinitionUseFilter.All);
            editor.DigestInput.Text.Should().BeNullOrEmpty();
            editor.RevisionInput.Text.Should().BeNullOrEmpty();
            editor.ObservedFromInput.Text.Should().BeNullOrEmpty();
            editor.ObservedUntilInput.Text.Should().BeNullOrEmpty();
            editor.Files.ItemsSource.Should().BeNull();
            await PrepareOwnedQuery(f, ArtifactKind.Instruction, "missing-again");
            f.Editor.Should().BeSameAs(editor);
            f.Store.Reads.Should().Be(0); f.Store.Lists.Should().Be(0); f.Store.Writes.Should().Be(0);
        });
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction, false)]
    [InlineData(ArtifactKind.Prompt, false)]
    [InlineData(ArtifactKind.Instruction, true)]
    public async Task SeparateExplicitCrossKindInspectUsesNativeDiscardCancelWithoutPreparationAuthority(ArtifactKind sourceKind, bool discard)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(sourceKind, false);
            var editor = f.Editor!;
            var targetKind = sourceKind == ArtifactKind.Instruction ? ArtifactKind.Prompt : ArtifactKind.Instruction;
            var target = new WindowsOwnedInstructionStore(f.Session, targetKind);
            await target.Write("other", null, Document(targetKind, "PRIVATE_OTHER_SOURCE").WithOwnedCopyName("other"), () => true, f.Session.Token);
            editor.Source.Text += "\r\nPRIVATE_UNSAVED\r\n";
            var draft = editor.Source.Text;
            await PrepareOwnedQuery(f, targetKind, "other");
            editor.Source.Text.Should().Be(draft);
            editor.Files.ItemsSource.Should().BeNull();
            await ClickEditorAction(editor, "List");
            var row = editor.Files.ItemsSource!.Cast<OwnedDefinitionInventoryRow>().Single(item => item.Name is "other");
            editor.Files.SelectedItem = row;
            Window? review = null;
            using var handler = Window.WindowOpenedEvent.AddClassHandler<Window>((opened, _) =>
            { if (!ReferenceEquals(opened, editor)) { review = opened; } });
            var inspected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            editor.Source.PropertyChanged += (_, change) =>
            { if (change.Property == TextBox.TextProperty && editor.Source.Text?.Contains("PRIVATE_OTHER_SOURCE", StringComparison.Ordinal) == true) { inspected.TrySetResult(); } };
            editor.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Inspect"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            review.Should().NotBeNull();
            // A new preparation must not replace or answer the pending native guard.
            await PrepareOwnedQuery(f, sourceKind, "not-admitted");
            editor.QueryInput.Text.Should().Be("other");
            review!.IsVisible.Should().BeTrue();
            review.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, discard ? "Discard draft and continue" : "Cancel"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (discard) { await inspected.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken); }
            else { await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background); }
            editor.InventoryKindInput.SelectedItem.Should().Be(targetKind);
            editor.QueryInput.Text.Should().Be("other");
            editor.KindInput.SelectedItem.Should().Be(discard ? targetKind : sourceKind);
            if (discard) { editor.Source.Text.Should().Contain("PRIVATE_OTHER_SOURCE"); }
            else
            {
                editor.Source.Text.Should().Be(draft);
                editor.NameInput.Text.Should().Be("example");
                editor.Files.SelectedItem.Should().BeSameAs(row);
            }
            File.ReadAllText(f.SourcePath).Should().NotContain("PRIVATE_UNSAVED");
        });
    }

    [Fact]
    public async Task PreparationRetiresPendingBodyAndInventoryCallbacksWithoutImplicitSourceReads()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false);
            var editor = f.Editor!;
            var original = editor.Source.Text;
            var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var readRelease = new TaskCompletionSource<OwnedInstructionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Store.ReadOverride = (_, _) => { readStarted.TrySetResult(); return readRelease.Task; };
            var read = f.Run("instructions inspect example");
            await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            var prepared = f.Preparation.TryPrepare(ArtifactKind.Instruction, "future-only", () => true);
            readRelease.TrySetResult(f.Source);
            await read;
            prepared.Should().BeTrue();
            editor.Source.Text.Should().Be(original);
            editor.QueryInput.Text.Should().Be("future-only");
            editor.Files.ItemsSource.Should().BeNull();
            editor.ReviewUse.IsEnabled.Should().BeFalse();
            f.Store.ReadOverride = null;
            f.Store.Reads = 0;
            f.Sessions.PauseNext = true;
            var list = f.Run("instructions list");
            await f.Sessions.Started.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            prepared = f.Preparation.TryPrepare(ArtifactKind.Prompt, "other-future", () => true);
            f.Sessions.Release.TrySetResult();
            await list;
            prepared.Should().BeTrue();
            editor.Files.ItemsSource.Should().BeNull();
            editor.QueryInput.Text.Should().Be("other-future");
            f.Store.Reads.Should().Be(0);
            f.Store.Writes.Should().Be(0);
        });
    }

    [Theory]
    [InlineData("source")]
    [InlineData("filters")]
    [InlineData("close-reopen")]
    [InlineData("owner")]
    [InlineData("host")]
    public async Task CapturedNativePreparationCannotFollowAnotherBufferFilterHostOrWindow(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false);
            var current = f.Preparation.CaptureLifetime();
            current().Should().BeTrue();
            switch (change)
            {
                case "source": f.Editor!.Source.Text += "changed"; break;
                case "filters": f.Editor!.DigestInput.Text = "incomplete"; break;
                case "owner": f.Ownership++; break;
                case "host": f.Owned = false; break;
                case "close-reopen":
                    f.Editor!.Close();
                    await PrepareOwnedQuery(f, ArtifactKind.Instruction, "another");
                    break;
            }
            current().Should().BeFalse();
            f.Store.Reads.Should().Be(0); f.Store.Lists.Should().Be(0); f.Store.Writes.Should().Be(0);
        });
    }

    [Fact]
    public async Task UnconfirmedNativeFocusCannotPrepareFieldsOrClaimSuccess()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false);
            var editor = f.Editor!;
            var source = editor.Source.Text;
            var kind = editor.InventoryKindInput.SelectedItem;
            var mode = editor.QueryModeInput.SelectedItem;
            var query = editor.QueryInput.Text;
            editor.QueryInput.IsEnabled = false;
            f.Store.DenyAccess = true;
            await PrepareOwnedQuery(f, ArtifactKind.Prompt, "not-prepared");
            f.Session.Main.ResponseTitle.Should().Contain("unavailable");
            editor.QueryInput.Text.Should().Be(query);
            editor.QueryModeInput.SelectedItem.Should().Be(mode);
            editor.InventoryKindInput.SelectedItem.Should().Be(kind);
            editor.Source.Text.Should().Be(source);
            f.Store.Reads.Should().Be(0); f.Store.Lists.Should().Be(0); f.Store.Writes.Should().Be(0);
        });
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction, false)]
    [InlineData(ArtifactKind.Prompt, false)]
    [InlineData(ArtifactKind.Instruction, true)]
    [InlineData(ArtifactKind.Prompt, true)]
    public async Task SaveCancelOrChangedBufferCannotAnswerReviewOrContinueStalePreparation(ArtifactKind kind, bool changeDuringReview)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(kind, false);
            var editor = f.Editor!;
            editor.Source.Text += "\r\nPRIVATE_UNSAVED\r\n";
            var draft = editor.Source.Text;
            var current = f.Preparation.CaptureLifetime();
            var reviewOpened = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var handler = Window.WindowOpenedEvent.AddClassHandler<Window>((opened, _) =>
            { if (!ReferenceEquals(opened, editor)) { reviewOpened.TrySetResult(opened); } });
            var save = f.Run(f.Prefix + " save");
            var review = await reviewOpened.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            current().Should().BeFalse();
            var reads = f.Store.Reads;
            f.Preparation.TryPrepare(kind, "not-authorized", () => true).Should().BeFalse();
            f.Store.Reads.Should().Be(reads);
            review.IsVisible.Should().BeTrue();
            editor.QueryInput.Text.Should().NotBe("not-authorized");
            editor.Source.Text.Should().Be(draft);
            if (changeDuringReview) { editor.Source.Text += "changed during exact review"; }
            review.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, changeDuringReview ? "Confirm save" : "Cancel"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await save;
            f.Store.Writes.Should().Be(0);
            editor.Source.Text.Should().Contain("PRIVATE_UNSAVED");
            File.ReadAllText(f.SourcePath).Should().NotContain("PRIVATE_UNSAVED");
            editor.QueryInput.Text.Should().NotBe("not-authorized");
            await PrepareOwnedQuery(f, kind, "fresh-only");
            editor.QueryInput.Text.Should().Be("fresh-only");
            editor.Files.ItemsSource.Should().BeNull();
            f.Store.Writes.Should().Be(0);
        });
    }

    [Fact]
    public async Task SourceChangeDuringNativePreparationCannotReceiveAcknowledgementOrDiscardBuffer()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false);
            var editor = f.Editor!;
            editor.QueryInput.PropertyChanged += (_, change) =>
            {
                if (change.Property == TextBox.TextProperty)
                { editor.Source.Text += "independent buffer edit"; }
            };
            await PrepareOwnedQuery(f, ArtifactKind.Prompt, "prepared-predicate");
            f.Session.Main.ResponseTitle.Should().Contain("unavailable");
            editor.Source.Text.Should().Contain("independent buffer edit");
            f.Status.Should().NotContain("Exact owned metadata query PREPARED");
            f.Store.Reads.Should().Be(0); f.Store.Lists.Should().Be(0); f.Store.Writes.Should().Be(0);
        });
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task PreparedIdIsOnlyAQueryAndOtherPredicatesRemainAndComposedUntilSeparateInspect(ArtifactKind kind)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(kind, false);
            var editor = f.Editor!;
            editor.DigestInput.Text = new string('f', 64);
            f.Store.DenyAccess = true;
            await PrepareOwnedQuery(f, kind, "example");
            editor.Files.ItemsSource.Should().BeNull();
            f.Store.Reads.Should().Be(0); f.Store.Lists.Should().Be(0); f.Store.Writes.Should().Be(0);
            f.Store.DenyAccess = false;
            await f.Run(f.Prefix + " list");
            editor.Files.ItemsSource!.Cast<OwnedDefinitionInventoryRow>().Should().BeEmpty();
            editor.DigestInput.Text = f.Source!.Document.Definition.Digest;
            editor.Files.ItemsSource!.Cast<OwnedDefinitionInventoryRow>().Should().ContainSingle();
            editor.Files.SelectedItem.Should().BeNull();
            editor.ReviewUse.IsEnabled.Should().BeFalse();
            f.Store.Reads.Should().Be(0);
            await f.Run(f.Prefix + " inspect example");
            f.Store.Reads.Should().Be(1);
            editor.ReviewUse.IsEnabled.Should().BeTrue();
            f.Store.Writes.Should().Be(0);
        });
    }

    private static async Task PrepareOwnedQuery(DraftValidationFixture f, ArtifactKind kind, string id)
    {
        await f.Run("prepare owned definition " + kind.ToString().ToLowerInvariant() + " " + id);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private static async Task ClickEditorAction(OwnedInstructionEditorWindow editor, string action)
    {
        // Button work owns its asynchronous lifecycle; use a status change rather than a delay.
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var status = editor.GetVisualDescendants().OfType<TextBlock>().ToArray();
        void Changed(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == TextBlock.TextProperty && args.NewValue is string text
                && text.Contains("Owned file inventory refreshed", StringComparison.Ordinal))
            { complete.TrySetResult(); }
        }
        foreach (var block in status) { block.PropertyChanged += Changed; }
        try
        {
            editor.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, action))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await complete.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        }
        finally { foreach (var block in status) { block.PropertyChanged -= Changed; } }
    }
}

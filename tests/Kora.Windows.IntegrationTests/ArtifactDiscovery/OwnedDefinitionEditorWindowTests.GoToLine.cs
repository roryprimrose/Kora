using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Artifacts;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class OwnedDefinitionEditorWindowTests
{
    [Theory]
    [InlineData(ArtifactKind.Instruction, false)]
    [InlineData(ArtifactKind.Prompt, true)]
    public async Task GoToLineUsesExactInvalidDirtyDisplayedBufferWithoutAnyStoreOrUseAccess(ArtifactKind kind, bool newDraft)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(kind, newDraft);
            var editor = f.Editor!;
            const string draft = "INVALID_PRIVATE_DRAFT\U0001f600\r\nsecond\rthird\n";
            editor.Source.Text = draft;
            editor.Source.SelectionStart = 1;
            editor.Source.SelectionEnd = 5;
            var selectionStart = editor.Source.SelectionStart;
            var selectionEnd = editor.Source.SelectionEnd;
            var canUndo = editor.Source.CanUndo;
            var canRedo = editor.Source.CanRedo;
            var selected = editor.Files.SelectedItem;
            var catalogue = f.Catalogue.Candidates;
            f.Store.DenyAccess = true;
            Click(editor.GoToLineButton);
            editor.LineInput.IsFocused.Should().BeTrue();
            AutomationProperties.GetName(editor.LineInput).Should().Contain("One-based").And.Contain("current displayed owned source only");
            f.Status.Should().Contain("4 logical lines").And.NotContain("INVALID_PRIVATE_DRAFT");
            editor.LineInput.Text = "03";
            Click(editor.LineGo);
            f.Status.Should().Contain("canonical ASCII integer");
            editor.Source.SelectionStart.Should().Be(selectionStart);
            editor.Source.SelectionEnd.Should().Be(selectionEnd);
            editor.LineInput.Text = "3";
            editor.Source.SelectionStart.Should().Be(selectionStart);
            editor.Source.SelectionEnd.Should().Be(selectionEnd);
            Click(editor.LineGo);
            editor.Source.CaretIndex.Should().Be(draft.IndexOf("third", StringComparison.Ordinal));
            editor.Source.IsFocused.Should().BeTrue();
            f.Status.Should().Contain("Go to line completed");
            editor.Source.Text.Should().Be(draft);
            editor.Source.CanUndo.Should().Be(canUndo);
            editor.Source.CanRedo.Should().Be(canRedo);
            editor.Files.SelectedItem.Should().BeSameAs(selected);
            f.Catalogue.Candidates.Should().BeSameAs(catalogue);
            Click(editor.GoToLineButton);
            editor.LineInput.Text = "4";
            Click(editor.LineGo);
            editor.Source.CaretIndex.Should().Be(draft.Length);
            f.Store.Reads.Should().Be(0);
            f.Store.Writes.Should().Be(0);
            f.Store.Lists.Should().Be(0);
        });
    }

    [Theory]
    [InlineData("Reload")]
    [InlineData("Rebase")]
    [InlineData("Save as new copy")]
    public async Task ExplicitEditorOperationsRetireLineRequestsEvenWhenTheDisplayedTextDoesNotChange(string action)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false);
            var editor = f.Editor!;
            var text = editor.Source.Text;
            Click(editor.GoToLineButton);
            editor.LineInput.Text = "2";
            f.Store.DenyAccess = true;
            Click(editor.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, action)));
            var reads = f.Store.Reads;
            var writes = f.Store.Writes;
            var lists = f.Store.Lists;
            var caret = editor.Source.CaretIndex;
            Click(editor.LineGo);
            editor.Source.CaretIndex.Should().Be(caret);
            editor.Source.Text.Should().Be(text);
            f.Status.Should().NotContain("Go to line completed");
            f.Store.Reads.Should().Be(reads);
            f.Store.Writes.Should().Be(writes);
            f.Store.Lists.Should().Be(lists);
        });
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("name")]
    [InlineData("copy")]
    [InlineData("kind")]
    [InlineData("file")]
    [InlineData("readonly")]
    [InlineData("owner")]
    [InlineData("privacy")]
    [InlineData("validate")]
    [InlineData("close")]
    [InlineData("dispose")]
    [InlineData("detach")]
    public async Task EnteredRequestCannotNavigateAfterSourceOrNativeLifetimeChanges(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false);
            var editor = f.Editor!;
            editor.Source.Text = "first\nsecond";
            Click(editor.GoToLineButton);
            editor.LineInput.Text = "2";
            f.Store.DenyAccess = true;
            switch (change)
            {
                case "edit": editor.Source.Text = "a newer\nbuffer"; break;
                case "name": editor.NameInput.Text = "different"; break;
                case "copy": editor.CopyNameInput.Text = "copy"; break;
                case "kind": editor.SelectKind(ArtifactKind.Prompt); break;
                case "file": editor.Files.SelectedItem = null; break;
                case "readonly": editor.Source.IsReadOnly = true; break;
                case "owner": f.Ownership++; break;
                case "privacy": f.Session.Main.CloseForObservedPrivacyEvent("Synthetic Go to line closure", hidePresentation: true); break;
                case "validate": await f.Run("instructions validate"); break;
                case "close": editor.ClearPrivateText(); editor.Close(); break;
                case "dispose": f.DisposeController(); break;
                case "detach":
                    var parent = (Panel)editor.Source.Parent!;
                    parent.Children.Remove(editor.Source);
                    parent.Children.Add(editor.Source);
                    break;
            }
            var caret = editor.Source.CaretIndex;
            // Simulates delivery of a click queued before retirement.
            Click(editor.LineGo);
            editor.Source.CaretIndex.Should().Be(caret);
            f.Status.Should().NotContain("Go to line completed");
            f.Store.Reads.Should().Be(0);
            f.Store.Writes.Should().Be(0);
            f.Store.Lists.Should().Be(0);
            if (change is "privacy") { await f.Session.Main.PrivacyClosureTask; }
        });
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("cancel")]
    [InlineData("close")]
    public async Task SynchronousFocusReentrancyAndQueuedClicksCannotNavigateANewerBuffer(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var editor = new OwnedInstructionEditorWindow(_ => { });
            editor.BindGoToLine(() => () => true);
            editor.Show();
            try
            {
                editor.Source.Text = "first\nsecond";
                Click(editor.GoToLineButton);
                editor.LineInput.Text = "2";
                editor.Source.GotFocus += (_, _) =>
                {
                    if (change is "edit") { editor.Source.Text = "newer\nbuffer"; }
                    else if (change is "cancel") { Click(editor.LineCancel); }
                    else { editor.Close(); }
                };
                Click(editor.LineGo);
                Status(editor).Should().NotContain("Go to line completed");
                if (change is not "close")
                {
                    Click(editor.GoToLineButton);
                    editor.LineInput.Text = "2";
                    var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    Dispatcher.UIThread.Post(() => { Click(editor.LineGo); queued.SetResult(); });
                    editor.Source.Text = "replacement\nsource";
                    var caret = editor.Source.CaretIndex;
                    await queued.Task.WaitAsync(TestContext.Current.CancellationToken);
                    editor.Source.CaretIndex.Should().Be(caret);
                    Status(editor).Should().NotContain("Go to line completed");
                }
            }
            finally { editor.Close(); }
        });
    }

    [Fact]
    public async Task UnknownReadonlyOversizedAndUnfocusableSourcesFailWithoutRepairAndInputCannotTransferClipboard()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var editor = new OwnedInstructionEditorWindow(_ => { });
            editor.Show();
            try
            {
                Click(editor.GoToLineButton);
                Status(editor).Should().Contain("unavailable");
                editor.BindGoToLine(() => () => true);
                editor.Source.IsReadOnly = true;
                Click(editor.GoToLineButton);
                Status(editor).Should().Contain("unavailable");
                editor.Source.IsReadOnly = false;
                var oversized = new string('x', ArtifactDefinition.MaximumContentLength + 1);
                editor.Source.Text = oversized;
                Click(editor.GoToLineButton);
                Status(editor).Should().Contain("character bound");
                editor.Source.Text.Should().Be(oversized);
                editor.Source.Text = string.Empty;
                editor.LineInput.Focusable = false;
                Click(editor.GoToLineButton);
                Status(editor).Should().Contain("could not focus");
                editor.LineGo.IsEnabled.Should().BeFalse();
                editor.LineInput.Focusable = true;
                Click(editor.GoToLineButton);
                Status(editor).Should().Contain("1 logical lines");
                editor.LineInput.Text = "1";
                foreach (var transfer in new[] { TextBox.CopyingToClipboardEvent, TextBox.CuttingToClipboardEvent, TextBox.PastingFromClipboardEvent })
                {
                    var args = new RoutedEventArgs(transfer);
                    editor.LineInput.RaiseEvent(args);
                    args.Handled.Should().BeTrue();
                }
                editor.LineInput.ContextMenu.Should().BeNull();
                Click(editor.LineGo);
                editor.Source.CaretIndex.Should().Be(0);
                editor.Source.Text.Should().BeEmpty();
                Status(editor).Should().Contain("completed");
            }
            finally { editor.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task QueuedOldNativeGoControlCannotNavigateAnExplicitlyReopenedNewerBuffer()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var editor = new OwnedInstructionEditorWindow(_ => { });
            editor.BindGoToLine(() => () => true);
            editor.Show();
            try
            {
                editor.Source.Text = "first\nold";
                Click(editor.GoToLineButton);
                editor.LineInput.Text = "2";
                var retiredGo = editor.LineGo;
                var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Dispatcher.UIThread.Post(() => { Click(retiredGo); queued.SetResult(); });
                editor.Source.Text = "new\ncurrent";
                Click(editor.GoToLineButton);
                editor.LineInput.Text = "2";
                editor.LineGo.Should().NotBeSameAs(retiredGo);
                var caret = editor.Source.CaretIndex;
                await queued.Task.WaitAsync(TestContext.Current.CancellationToken);
                editor.Source.CaretIndex.Should().Be(caret);
                editor.LineInput.IsFocused.Should().BeTrue();
                editor.LineGo.IsEnabled.Should().BeTrue();
                Status(editor).Should().Contain("Earlier Go to line gesture retired").And.NotContain("completed");
                Click(editor.LineGo);
                editor.Source.CaretIndex.Should().Be(4);
                Status(editor).Should().Contain("completed");
            }
            finally { editor.Close(); }
        });
    }

    [Fact]
    public async Task KeyboardCancelReentrantFocusAndNativeCaretVisibilityAreExplicitAndFailClosed()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var editor = new OwnedInstructionEditorWindow(_ => { });
            var current = true;
            editor.BindGoToLine(() => () => current);
            editor.Show();
            try
            {
                editor.Source.Text = string.Join('\n', Enumerable.Range(1, 100).Select(_ => "line"));
                editor.Source.Focus();
                editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.G, KeyModifiers = KeyModifiers.Control });
                editor.LineInput.IsFocused.Should().BeTrue();
                editor.LineInput.Text = "100";
                Click(editor.LineCancel);
                editor.Source.CaretIndex.Should().Be(0);
                Click(editor.GoToLineButton);
                editor.LineInput.Text = "100";
                Click(editor.LineGo);
                editor.Source.IsFocused.Should().BeTrue();
                editor.Source.CaretIndex.Should().Be(495);
                editor.UpdateLayout();
                var presenter = editor.Source.GetVisualDescendants().OfType<TextPresenter>().Single();
                var scroll = editor.Source.GetVisualDescendants().OfType<ScrollViewer>().Single();
                var caret = presenter.TextLayout.HitTestTextPosition(495);
                scroll.Offset.Y.Should().BeGreaterThan(0);
                caret.Bottom.Should().BeLessThanOrEqualTo(scroll.Offset.Y + scroll.Viewport.Height + 1);
                Click(editor.GoToLineButton);
                editor.LineInput.Text = "1";
                editor.Source.GotFocus += (_, _) =>
                {
                    current = false;
                    editor.Source.Text = "new\nbuffer";
                    Click(editor.LineGo);
                };
                Click(editor.LineGo);
                editor.Source.CaretIndex.Should().NotBe(0);
                Status(editor).Should().Contain("could not focus").And.NotContain("completed");
                current = true;
                Click(editor.GoToLineButton);
                editor.LineInput.Text = "1";
                editor.Source.IsEnabled = false;
                Click(editor.LineGo);
                Status(editor).Should().Contain("retired");
            }
            finally { editor.Close(); }
            return Task.CompletedTask;
        });
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static string Status(OwnedInstructionEditorWindow editor) =>
        string.Join("\n", editor.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
}

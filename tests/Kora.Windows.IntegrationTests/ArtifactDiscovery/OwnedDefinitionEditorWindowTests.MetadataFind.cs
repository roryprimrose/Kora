using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Core.Artifacts;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class OwnedDefinitionEditorWindowTests
{
    private static readonly string[] MetadataAbsentText =
        ["BodyOnlySecret", "HiddenDescriptionSecret", "HiddenDeclarationSecret", "HiddenOtherRowSecret", "HiddenPathSecret", "Unsaved BodyOnlySecret"];
    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task MetadataFindIsExactDisplayedSelectedDetailsOnlyWithPrivateKeyboardAndNoOperationAuthority(ArtifactKind kind)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var operations = new List<string>();
            var window = new OwnedInstructionEditorWindow(operations.Add);
            window.Show();
            try
            {
                var page = MetadataPage(kind);
                window.SelectKind(kind);
                window.ShowInventory(page);
                var row = page.Rows[0];
                window.Source.Text = "Unsaved BodyOnlySecret aa aa";
                window.NameInput.Text = "dirty-name";
                window.CopyNameInput.Text = "dirty-copy";
                var draft = window.Source.Text;
                window.Files.SelectedItem = row;
                window.AdmitMetadataFind(row, () => true);
                operations.Clear();
                ClickMetadata(window.MetadataFindFocus);
                window.MetadataFindQuery.IsFocused.Should().BeTrue();
                AutomationProperties.GetName(window.MetadataFindQuery).Should().Contain("metadata details").And.Contain("512");
                foreach (var absent in MetadataAbsentText)
                {
                    FindMetadataText(window, absent);
                    window.MetadataFindStatus.Text.Should().Contain("0 nonoverlapping matches").And.Contain("no displayed match");
                    window.MetadataFindMatch.Text.Should().BeEmpty();
                }
                FindMetadataText(window, "aa");
                var expectedCount = Kora.Core.Context.SnapshotLiteralFind.FindCounted(row.Details, "aa", Kora.Core.Context.SnapshotFindDirection.First).Count;
                window.MetadataFindStatus.Text.Should().Contain($"1 of {expectedCount}");
                window.MetadataFindMatch.IsFocused.Should().BeTrue();
                window.MetadataFindMatch.Text.Should().Be("aa");
                var count = 1;
                while (count++ < expectedCount) { ClickMetadata(window.MetadataFindNext); }
                ClickMetadata(window.MetadataFindNext);
                window.MetadataFindStatus.Text.Should().Contain("no wrap").And.Contain($"{expectedCount} of {expectedCount}");
                window.MetadataFindMatch.Text.Should().BeEmpty();
                ClickMetadata(window.MetadataFindPrevious);
                window.MetadataFindStatus.Text.Should().Contain($"{expectedCount - 1} of {expectedCount}");
                window.MetadataFindQuery.Focus();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.MetadataFindStatus.Text.Should().Contain($"1 of {expectedCount}");
                window.KeyPress(Key.F3, RawInputModifiers.None, PhysicalKey.F3, null);
                window.MetadataFindStatus.Text.Should().Contain($"2 of {expectedCount}");
                window.KeyPress(Key.F3, RawInputModifiers.Shift, PhysicalKey.F3, null);
                window.MetadataFindStatus.Text.Should().Contain($"1 of {expectedCount}");
                foreach (var key in new[] { Key.S, Key.F, Key.C, Key.X, Key.V, Key.Enter })
                {
                    var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = KeyModifiers.Control };
                    window.MetadataFindMatch.RaiseEvent(args);
                    args.Handled.Should().BeTrue();
                }
                foreach (var transfer in new[] { TextBox.CopyingToClipboardEvent, TextBox.CuttingToClipboardEvent, TextBox.PastingFromClipboardEvent })
                {
                    var args = new RoutedEventArgs(transfer);
                    window.MetadataFindQuery.RaiseEvent(args);
                    args.Handled.Should().BeTrue();
                }
                var sourceCtrlF = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F, KeyModifiers = KeyModifiers.Control };
                window.Source.RaiseEvent(sourceCtrlF);
                sourceCtrlF.Handled.Should().BeFalse();
                window.MetadataFindQuery.Text = "AA";
                window.MetadataFindStatus.Text.Should().BeEmpty();
                window.MetadataFindMatch.Text.Should().BeEmpty();
                ClickMetadata(window.MetadataFindFirst);
                window.MetadataFindStatus.Text.Should().Contain("0 nonoverlapping matches").And.Contain("no displayed match");
                FindMetadataText(window, "\u200b");
                window.MetadataFindStatus.Text.Should().StartWith("Invalid");
                FindMetadataText(window, "aa");
                window.MetadataFindQuery.Focus();
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                window.MetadataFindQuery.Text.Should().BeEmpty();
                window.MetadataFindStatus.Text.Should().BeEmpty();
                window.Files.SelectedItem.Should().BeSameAs(row);
                window.Source.Text.Should().Be(draft);
                window.NameInput.Text.Should().Be("dirty-name");
                window.CopyNameInput.Text.Should().Be("dirty-copy");
                window.QueryInput.Text.Should().BeNullOrEmpty();
                operations.Should().BeEmpty();
                window.Close();
                AssertMetadataRetired(window);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task MetadataFindRetiresOnEveryNativeBoundaryAndCannotRebindOldRowToSameIdReplacement()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var window = new OwnedInstructionEditorWindow(_ => { });
            window.Show();
            try
            {
                var page = MetadataPage(ArtifactKind.Instruction);
                var row = page.Rows[0];
                foreach (var retire in new Action[]
                {
                    window.ClearInventory, window.ClearValidation, window.CancelReview,
                    () => window.ShowInventory(page),
                    () => window.Files.SelectedItem = page.Rows[1],
                    window.ClearPrivateText,
                    window.RetireMetadataFind,
                    () => window.KindInput.SelectedItem = ArtifactKind.Prompt,
                })
                {
                    window.ShowInventory(page);
                    window.Files.SelectedItem = row;
                    window.AdmitMetadataFind(row, () => true);
                    FindMetadataText(window, "aa");
                    retire();
                    AssertMetadataRetired(window);
                }
                window.ShowInventory(page);
                window.Files.SelectedItem = row;
                var eligible = true;
                window.AdmitMetadataFind(row, () => eligible);
                FindMetadataText(window, "aa");
                eligible = false;
                ClickMetadata(window.MetadataFindNext);
                AssertMetadataRetired(window);
                eligible = true;
                ClickMetadata(window.MetadataFindFirst);
                AssertMetadataRetired(window);
                var replacement = MetadataPage(ArtifactKind.Instruction, 'b');
                window.ShowInventory(replacement);
                window.Files.SelectedItem = replacement.Rows[0];
                window.AdmitMetadataFind(row, () => true);
                AssertMetadataRetired(window);
                window.AdmitMetadataFind(replacement.Rows[0], () => true);
                FindMetadataText(window, "bb");
                window.MetadataFindMatch.Text.Should().Be("bb");
                window.MetadataFindMatch.Focusable = false;
                FindMetadataText(window, "bb");
                window.MetadataFindStatus.Text.Should().Contain("focus is unavailable").And.Contain("no match or focus claimed");
                window.MetadataFindNext.IsEnabled.Should().BeFalse();
                window.MetadataFindMatch.Text.Should().BeEmpty();
                window.MetadataFindMatch.Focusable = true;
                window.MetadataFindQuery.Focus();
                EventHandler<FocusChangedEventArgs> stealFocus = (_, _) => window.Source.Focus();
                window.MetadataFindMatch.GotFocus += stealFocus;
                FindMetadataText(window, "bb");
                window.MetadataFindStatus.Text.Should().Contain("focus is unavailable");
                window.MetadataFindMatch.Text.Should().BeEmpty();
                window.MetadataFindMatch.GotFocus -= stealFocus;
                window.MetadataFindQuery.Focusable = false;
                ClickMetadata(window.MetadataFindFocus);
                window.MetadataFindStatus.Text.Should().Contain("focus is unavailable");
                window.MetadataFindQuery.Focusable = true;
                window.IsEnabled = false;
                AssertMetadataRetired(window);
                window.IsEnabled = true;
                window.AdmitMetadataFind(replacement.Rows[0], () => true);
                FindMetadataText(window, "bb");
                window.MetadataFindQuery.IsEnabled = false;
                AssertMetadataRetired(window);
                window.AdmitMetadataFind(replacement.Rows[0], () => true);
                FindMetadataText(window, "bb");
                window.MetadataFindMatch.GotFocus += (_, _) => window.Close();
                window.MetadataFindQuery.Focus();
                ClickMetadata(window.MetadataFindFirst);
                AssertMetadataRetired(window);
                window.AdmitMetadataFind(replacement.Rows[0], () => true);
                AssertMetadataRetired(window);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("query")]
    [InlineData("same-query")]
    [InlineData("clear")]
    [InlineData("private")]
    [InlineData("replace")]
    [InlineData("close")]
    [InlineData("control")]
    public async Task MetadataFindReentrantPublicationCannotRestoreRetiredOrEditedMatch(string change)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var window = new OwnedInstructionEditorWindow(_ => { });
            window.Show();
            try
            {
                var page = MetadataPage(ArtifactKind.Instruction);
                window.ShowInventory(page);
                window.Files.SelectedItem = page.Rows[0];
                window.AdmitMetadataFind(page.Rows[0], () => true);
                var changed = false;
                window.MetadataFindMatch.PropertyChanged += (_, args) =>
                {
                    if (changed || args.Property != TextBlock.TextProperty || window.MetadataFindMatch.Text is not "aa") { return; }
                    changed = true;
                    switch (change)
                    {
                        case "query":
                        case "same-query":
                            window.MetadataFindQuery.Text = "new";
                            if (change is "same-query") { window.MetadataFindQuery.Text = "aa"; }
                            break;
                        case "clear": ClickMetadata(window.MetadataFindClear); break;
                        case "private": window.ClearPrivateText(); break;
                        case "replace":
                            var replacement = MetadataPage(ArtifactKind.Instruction, 'b');
                            window.ShowInventory(replacement);
                            window.Files.SelectedItem = replacement.Rows[0];
                            window.AdmitMetadataFind(replacement.Rows[0], () => true);
                            break;
                        case "close": window.Close(); break;
                        case "control": window.MetadataFindQuery.IsEnabled = false; break;
                    }
                };
                FindMetadataText(window, "aa");
                changed.Should().BeTrue();
                window.MetadataFindMatch.Text.Should().BeEmpty();
                window.MetadataFindMatch.IsVisible.Should().BeFalse();
                window.MetadataFindStatus.Text.Should().BeEmpty();
                window.MetadataFindNext.IsEnabled.Should().BeFalse();
                window.MetadataFindMatch.IsFocused.Should().BeFalse();
                window.MetadataFindQuery.Text.Should().Be(change is "query" ? "new" : change is "same-query" ? "aa" : string.Empty);
            }
            finally { window.Close(); }
        });
    }

    private static OwnedDefinitionInventoryPage MetadataPage(ArtifactKind kind, char digest = 'a')
    {
        var candidate = new ArtifactDefinition("owned.example", kind, "aa aa", "HiddenDescriptionSecret", "hidden-command",
            ["HiddenDeclarationSecret"], "kora-profile", "1", new string(digest, 64), "BodyOnlySecret")
        {
            OwnedInstructionName = "example",
            OwnedInstructionRevision = new string(digest, 64) + "." + new string('c', 48) + "." + new string('d', 48) + "." + new string('e', 64),
        };
        var other = new ArtifactDefinition("owned.other", kind, "HiddenOtherRowSecret", "Other description", "other-command",
            ["other spoken"], "kora-profile", "1", new string('f', 64), "Other body") { OwnedInstructionName = "other" };
        return new OwnedDefinitionInventory(kind, ["example", "other"], Array.AsReadOnly(new[] { candidate, other }), 0, DateTimeOffset.UtcNow)
            .Page("", OwnedDefinitionUseFilter.All);
    }

    private static void ClickMetadata(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void FindMetadataText(OwnedInstructionEditorWindow window, string text)
    {
        window.MetadataFindQuery.Text = text;
        ClickMetadata(window.MetadataFindFirst);
    }
    private static void AssertMetadataRetired(OwnedInstructionEditorWindow window)
    {
        window.MetadataFindQuery.Text.Should().BeEmpty();
        window.MetadataFindMatch.Text.Should().BeEmpty();
        window.MetadataFindStatus.Text.Should().BeEmpty();
        window.MetadataFindQuery.IsEnabled.Should().BeFalse();
        window.MetadataFindFocus.IsEnabled.Should().BeFalse();
        window.MetadataFindNext.IsEnabled.Should().BeFalse();
    }
}

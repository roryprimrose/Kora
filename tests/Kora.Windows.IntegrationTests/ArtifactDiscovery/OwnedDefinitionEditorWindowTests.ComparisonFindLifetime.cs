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
    [InlineData("edit")]
    [InlineData("same-query")]
    [InlineData("clear")]
    [InlineData("privacy")]
    [InlineData("focus")]
    public async Task CompareFindFencesReentrantMatchPublicationAndFocus(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false);
            var dialog = await OpenFindComparison(f);
            var pane = dialog.GetVisualDescendants().OfType<SnapshotComparisonFindPane>().Last();
            var query = FindQuery(pane);
            var span = FindSpan(pane);
            var changed = false;
            query.Text = "PRIVATE";
            query.Focus().Should().BeTrue();
            span.PropertyChanged += (_, args) =>
            {
                if (changed || change is "focus" || args.Property != TextBlock.TextProperty || span.Text is not "PRIVATE") { return; }
                changed = true;
                if (change is "privacy") { f.Editor!.ClearPrivateText(); }
                else if (change is "clear") { FindClick(pane, "Clear Find"); }
                else
                {
                    query.Text = "NEW";
                    if (change is "same-query") { query.Text = "PRIVATE"; }
                }
            };
            span.GotFocus += (_, _) =>
            {
                if (change is not "focus") { return; }
                changed = true;
                f.Ownership++;
            };
            FindClick(pane, "Find first");
            changed.Should().BeTrue();
            span.Text.Should().BeNull();
            span.IsVisible.Should().BeFalse();
            FindButton(pane, "Find next").IsEnabled.Should().BeFalse();
            if (change is "privacy" or "focus")
            {
                dialog.IsVisible.Should().BeFalse();
                FindSource(pane).Text.Should().BeEmpty();
                query.Text.Should().BeEmpty();
            }
            else
            {
                query.Text.Should().Be(change is "clear" ? string.Empty : change is "same-query" ? "PRIVATE" : "NEW");
                FindOutcome(pane).Text.Should().BeNull();
            }
            dialog.Close();
        });
    }

    [Fact]
    public async Task CompareFindReplacementClearsOldSourcesAndCallbacksCannotRetargetNewDialog()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Prompt, false);
            var old = await OpenFindComparison(f);
            var retired = old.GetVisualDescendants().OfType<SnapshotComparisonFindPane>().ToArray();
            foreach (var pane in retired)
            {
                FindQuery(pane).Text = "PRIVATE";
                FindClick(pane, "Find first");
            }
            var replacement = await OpenFindComparison(f);
            replacement.Should().NotBeSameAs(old);
            old.IsVisible.Should().BeFalse();
            foreach (var pane in retired)
            {
                FindQuery(pane).Text.Should().BeEmpty();
                FindSource(pane).Text.Should().BeEmpty();
                FindClick(pane, "Find first");
                FindClick(pane, "Clear Find");
                pane.RaiseEvent(FindKey(Key.F3));
                FindSpan(pane).Text.Should().BeNull();
                FindOutcome(pane).Text.Should().BeNull();
                pane.Dispose();
            }
            foreach (var pane in replacement.GetVisualDescendants().OfType<SnapshotComparisonFindPane>())
            {
                FindSource(pane).Text.Should().Contain("PRIVATE");
                FindQuery(pane).Text.Should().BeNullOrEmpty();
                FindOutcome(pane).Text.Should().BeNull();
            }
            replacement.Close();
        });
    }

    [Fact]
    public async Task CompareFindTransferGuardsCoverQueriesSourcesMatchesAndRetiredControls()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false);
            var dialog = await OpenFindComparison(f);
            var panes = dialog.GetVisualDescendants().OfType<SnapshotComparisonFindPane>().ToArray();
            foreach (var pane in panes)
            {
                FindQuery(pane).Text = "PRIVATE";
                FindClick(pane, "Find first");
            }
            for (var phase = 0; phase < 2; phase++)
            {
                if (phase == 1) { dialog.Close(); }
                foreach (var pane in panes)
                {
                    foreach (var control in new Control[] { FindQuery(pane), FindSource(pane), FindSpan(pane) })
                    {
                        foreach (var modifier in new[] { KeyModifiers.Control, KeyModifiers.Meta })
                        {
                            foreach (var key in new[] { Key.C, Key.X, Key.V, Key.Insert })
                            {
                                var args = FindKey(key, modifier);
                                control.RaiseEvent(args);
                                args.Handled.Should().BeTrue();
                            }
                        }
                        foreach (var key in new[] { Key.Insert, Key.Delete, Key.F10 })
                        {
                            var args = FindKey(key, KeyModifiers.Shift);
                            control.RaiseEvent(args);
                            args.Handled.Should().BeTrue();
                        }
                        var menu = new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent };
                        control.RaiseEvent(menu);
                        menu.Handled.Should().BeTrue();
                    }
                    foreach (var control in new[] { FindQuery(pane), FindSource(pane) })
                    {
                        foreach (var transfer in new[] { TextBox.CopyingToClipboardEvent, TextBox.CuttingToClipboardEvent, TextBox.PastingFromClipboardEvent })
                        {
                            var args = new RoutedEventArgs(transfer);
                            control.RaiseEvent(args);
                            args.Handled.Should().BeTrue();
                        }
                        control.ContextMenu.Should().BeNull();
                        control.IsUndoEnabled.Should().BeFalse();
                    }
                }
            }
        });
    }

    [Fact]
    public async Task CompareCancellationDuringAdmissionCannotPublishFindOrReadSources()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Prompt, false);
            var opened = 0;
            using var subscription = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            { if (window.Title?.StartsWith("Passive owned", StringComparison.Ordinal) == true) { opened++; } });
            f.Store.DenyAccess = true;
            f.Sessions.PauseNext = true;
            var operation = f.Run("prompts compare");
            await f.Sessions.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            f.Editor!.ClearPrivateText();
            f.Session.Main.CloseForObservedPrivacyEvent("Synthetic cancelled Compare", hidePresentation: true);
            f.Sessions.Release.TrySetResult();
            await operation;
            await f.Session.Main.PrivacyClosureTask;
            opened.Should().Be(0);
            f.Store.Reads.Should().Be(0); f.Store.Lists.Should().Be(0); f.Store.Writes.Should().Be(0);
        });
    }
}

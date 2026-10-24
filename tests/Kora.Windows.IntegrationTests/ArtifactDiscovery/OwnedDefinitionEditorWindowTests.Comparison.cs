using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Artifacts;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class OwnedDefinitionEditorWindowTests
{
    [Theory]
    [InlineData(ArtifactKind.Instruction, "clean")]
    [InlineData(ArtifactKind.Instruction, "dirty")]
    [InlineData(ArtifactKind.Instruction, "invalid")]
    [InlineData(ArtifactKind.Prompt, "new")]
    [InlineData(ArtifactKind.Prompt, "enabled")]
    [InlineData(ArtifactKind.Prompt, "unavailable")]
    public async Task ExplicitCompareShowsWholeInertFrozenBaseAndRawDraftWithoutReadingSavingOrApproving(ArtifactKind kind, string state)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(kind, state is "new");
            var editor = f.Editor!;
            if (state is "dirty") { editor.Source.Text += "\r\n  PRIVATE_NEW_BODY  \r\n"; }
            if (state is "invalid") { editor.Source.Text = "PRIVATE_INVALID_RAW_TEXT"; }
            if (state is "enabled")
            { await f.Actions.Activate(f.Source!, _ => Task.FromResult(true), () => true, f.Session.Token); }
            if (state is "unavailable") { f.Catalogue.ReportReloadFailure(new IOException("Unavailable")); }
            var draft = editor.Source.Text;
            var candidates = f.Catalogue.Candidates;
            var items = editor.Files.ItemsSource;
            var query = editor.QueryInput.Text;
            f.Store.Reads = 0; f.Store.Lists = 0; f.Store.Writes = 0; f.Store.DenyAccess = true;
            Window? comparison = null;
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var subscription = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window.Title?.StartsWith("Passive owned", StringComparison.Ordinal) == true)
                { comparison = window; opened.TrySetResult(); }
            });
            await f.Run("Kora, " + f.Prefix + " compare");
            comparison.Should().NotBeNull();
            var panes = comparison!.GetVisualDescendants().OfType<TextBox>().Where(pane => pane.IsReadOnly).ToArray();
            panes.Should().HaveCount(2).And.OnlyContain(pane => pane.IsReadOnly && pane.AcceptsReturn && !pane.IsUndoEnabled);
            panes[0].Text.Should().Be(f.Source?.Document.Text ?? string.Empty);
            panes[1].Text.Should().Be(draft);
            var summary = comparison.GetVisualDescendants().OfType<TextBlock>().First().Text;
            summary.Should().Contain("NOT canonical Save bytes").And.Contain("historical");
            if (state is "invalid") { summary.Should().Contain("Prepared Save proposal rejected"); }
            comparison.GetVisualDescendants().OfType<Button>().Should().ContainSingle(button => Equals(button.Content, "Close"));
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
            editor.Source.Text.Should().Be(draft);
            editor.Files.ItemsSource.Should().BeSameAs(items);
            editor.QueryInput.Text.Should().Be(query);
            f.Catalogue.Candidates.Should().BeSameAs(candidates);
            comparison.Close();
            panes.Should().OnlyContain(pane => pane.Text == string.Empty);
            opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
            editor.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Compare"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await opened.Task.WaitAsync(TestContext.Current.CancellationToken);
            editor.Source.Text.Should().Be(draft);
            f.Store.Reads.Should().Be(0); f.Store.Lists.Should().Be(0); f.Store.Writes.Should().Be(0);
        });
    }

    [Theory]
    [InlineData("draft", false)]
    [InlineData("name", false)]
    [InlineData("kind", false)]
    [InlineData("selection", false)]
    [InlineData("catalogue", false)]
    [InlineData("source", false)]
    [InlineData("input", false)]
    [InlineData("same-input", false)]
    [InlineData("privacy", false)]
    [InlineData("call", false)]
    [InlineData("owner", false)]
    [InlineData("close", false)]
    [InlineData("draft", true)]
    [InlineData("catalogue", true)]
    [InlineData("privacy", true)]
    [InlineData("input", true)]
    [InlineData("owner", true)]
    public async Task ComparisonDisplayAndLatePublicationRetireOnCapturedIdentityAndPrivateEpochChanges(string change, bool late)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false);
            Window? comparison = null;
            using var subscription = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            { if (window.Title?.StartsWith("Passive owned", StringComparison.Ordinal) == true) { comparison = window; } });
            f.Store.DenyAccess = true;
            f.Sessions.PauseNext = late;
            var operation = f.Run("instructions compare");
            if (late) { await f.Sessions.Started.Task.WaitAsync(TestContext.Current.CancellationToken); }
            else { await operation; comparison.Should().NotBeNull(); }
            var displayed = comparison;
            var panes = comparison?.GetVisualDescendants().OfType<TextBox>().ToArray();
            var finds = comparison?.GetVisualDescendants().OfType<SnapshotComparisonFindPane>().ToArray();
            if (finds is not null)
            {
                foreach (var pane in finds)
                {
                    FindQuery(pane).Text = "PRIVATE";
                    FindClick(pane, "Find first");
                    FindSpan(pane).Text.Should().Be("PRIVATE");
                }
            }
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (comparison is not null) { comparison.Closed += (_, _) => closed.TrySetResult(); }
            var editor = f.Editor!;
            switch (change)
            {
                case "draft": editor.Source.Text += "\nLater edit."; break;
                case "name": editor.NameInput.Text = "other"; break;
                case "kind": editor.SelectKind(ArtifactKind.Prompt); break;
                case "selection": editor.Files.SelectedItem = null; break;
                case "catalogue": f.Catalogue.Publish(f.Catalogue.Candidates); break;
                case "source": f.Catalogue.ReportReloadFailure(new IOException("Unavailable")); break;
                case "input": f.Session.Main.CommandText = "instructions save"; break;
                case "same-input": await f.Session.Main.RunTypedCommand.ExecuteAsync(); break;
                case "privacy": f.Session.Main.CloseForObservedPrivacyEvent("Synthetic comparison closure", hidePresentation: true); break;
                case "call": f.Session.Main.CommandText = "call mode on"; await f.Session.Main.RunTypedCommand.ExecuteAsync(); break;
                case "owner": f.Ownership++; break;
                case "close": editor.Close(); break;
            }
            if (late) { f.Sessions.Release.TrySetResult(); await operation; comparison.Should().BeNull(); }
            else
            {
                await closed.Task.WaitAsync(TestContext.Current.CancellationToken);
                displayed!.IsVisible.Should().BeFalse();
                panes!.Should().OnlyContain(pane => pane.Text == string.Empty);
                foreach (var pane in finds!)
                {
                    FindClick(pane, "Find first");
                    FindClick(pane, "Find next");
                    FindClick(pane, "Find previous");
                    FindClick(pane, "Clear Find");
                    FindSpan(pane).Text.Should().BeNull();
                    FindOutcome(pane).Text.Should().BeNull();
                    FindSource(pane).Text.Should().BeEmpty();
                    FindQuery(pane).Text.Should().BeEmpty();
                    FindButton(pane, "Find first").IsEnabled.Should().BeFalse();
                    pane.Dispose();
                }
            }
            if (change is "privacy") { await f.Session.Main.PrivacyClosureTask; editor.Source.Text.Should().BeEmpty(); }
            f.Store.Reads.Should().Be(0); f.Store.Lists.Should().Be(0); f.Store.Writes.Should().Be(0);
        });
    }

    [Theory]
    [InlineData("different-kind")]
    [InlineData("closed")]
    [InlineData("missing")]
    [InlineData("readonly")]
    [InlineData("name")]
    [InlineData("overbudget")]
    [InlineData("unicode")]
    public async Task CompareFailsClosedWithoutOpeningSourcesOrSwitchingDrafts(string failure)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false);
            var editor = f.Editor!;
            if (failure is "closed") { editor.Close(); }
            if (failure is "missing") { await f.Run("instructions inspect missing"); }
            if (failure is "readonly") { editor.Source.IsReadOnly = true; }
            if (failure is "name") { editor.NameInput.Text = "other"; }
            if (failure is "overbudget") { editor.Source.Text = new string('\u4e00', 30000); }
            if (failure is "unicode") { editor.Source.Text = "\ud800"; }
            var draft = editor.Source.Text;
            var opened = false;
            using var subscription = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            { if (window.Title?.StartsWith("Passive owned", StringComparison.Ordinal) == true) { opened = true; } });
            f.Store.Reads = 0; f.Store.Lists = 0; f.Store.Writes = 0; f.Store.DenyAccess = true;
            await f.Run(failure is "different-kind" ? "prompts compare" : "instructions compare");
            opened.Should().BeFalse();
            editor.Source.Text.Should().Be(draft);
            f.Store.Reads.Should().Be(0); f.Store.Lists.Should().Be(0); f.Store.Writes.Should().Be(0);
        });
    }

    [Fact]
    public async Task PrivateRevocationDuringViewerOpeningClearsPanesAndCannotRestartObservation()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false);
            Window? comparison = null;
            TextBox[]? panes = null;
            using var subscription = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window.Title?.StartsWith("Passive owned", StringComparison.Ordinal) != true) { return; }
                comparison = window;
                panes = window.GetLogicalDescendants().OfType<TextBox>().ToArray();
                f.Session.Main.CloseForObservedPrivacyEvent("Synthetic opening revocation", hidePresentation: true);
            });
            f.Store.DenyAccess = true;
            await f.Run("instructions compare");
            await f.Session.Main.PrivacyClosureTask;
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            comparison.Should().NotBeNull();
            comparison!.IsVisible.Should().BeFalse();
            panes!.Should().HaveCount(4).And.OnlyContain(pane => pane.Text == string.Empty && !pane.IsUndoEnabled);
            f.Editor!.Source.Text.Should().BeEmpty();
            f.Store.Reads.Should().Be(0); f.Store.Lists.Should().Be(0); f.Store.Writes.Should().Be(0);
        });
    }
}

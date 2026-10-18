using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Core.Artifacts;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class OwnedDefinitionEditorWindowTests
{
    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task NativeOrderSelectorRetiresSelectionValidationAndComparisonWithoutReadingOrChangingRawDraftAndBaseline(ArtifactKind kind)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(kind, false);
            var editor = f.Editor!;
            await f.Run(f.Prefix + " list");
            f.Store.Reads = 0; f.Store.Lists = 0; f.Store.Writes = 0;
            editor.Source.Text += "\r\n  PRIVATE_UNSAVED_RAW_DRAFT  \r\n";
            var draft = editor.Source.Text;
            var baseline = f.Source!.Document.Text;
            var bytes = File.ReadAllBytes(f.SourcePath);
            var catalogue = f.Catalogue.Candidates;
            f.Store.DenyAccess = true;
            editor.OrderInput.SelectedItem.Should().Be(OwnedDefinitionInventoryOrder.FileNameAscending);
            editor.OrderInput.Focus().Should().BeTrue();
            AutomationProperties.GetName(editor.OrderInput).Should().Contain("ordinal").And.Contain("absent display names last")
                .And.Contain("filename ascending ties").And.Contain("metadata only");
            Window? comparison = null;
            using var subscription = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            { if (window.Title?.StartsWith("Passive owned", StringComparison.Ordinal) == true) { comparison = window; } });
            foreach (var order in Enum.GetValues<OwnedDefinitionInventoryOrder>().Skip(1))
            {
                editor.Files.SelectedItem = editor.Files.ItemsSource!.Cast<OwnedDefinitionInventoryRow>().Single();
                editor.SetUseReviewAvailable(true);
                await f.Run(f.Prefix + " compare");
                comparison.Should().NotBeNull();
                var old = comparison!;
                var panes = old.GetVisualDescendants().OfType<TextBox>().Where(box => box.IsReadOnly).ToArray();
                panes[0].Text.Should().Be(baseline);
                panes[1].Text.Should().Be(draft);
                editor.OrderInput.SelectedItem = order;
                old.IsVisible.Should().BeFalse();
                panes.Should().OnlyContain(box => box.Text == string.Empty);
                editor.Files.ItemsSource!.Cast<OwnedDefinitionInventoryRow>().Should().ContainSingle();
                editor.Files.SelectedItem.Should().BeNull();
                editor.NextPage.IsEnabled.Should().BeFalse();
                editor.ReviewUse.IsEnabled.Should().BeFalse();
                editor.DisableReviewedUse.IsEnabled.Should().BeFalse();
                editor.Source.Text.Should().Be(draft);
                f.Status.Should().NotContain("Supported structural draft observed");
            }
            editor.OrderInput.SelectedItem = null;
            editor.Files.ItemsSource.Should().BeNull();
            f.Status.Should().Contain("Select an explicit owned metadata order");
            editor.OrderInput.SelectedItem = (OwnedDefinitionInventoryOrder)99;
            editor.Files.ItemsSource.Should().BeNull();
            editor.OrderInput.SelectedItem = OwnedDefinitionInventoryOrder.DisplayNameDescending;
            editor.QueryInput.Text = "example";
            var clear = editor.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Clear search"));
            AutomationProperties.GetName(clear).Should().Contain("FileNameAscending");
            clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            editor.OrderInput.SelectedItem.Should().Be(OwnedDefinitionInventoryOrder.FileNameAscending);
            editor.QueryInput.Text.Should().BeEmpty();
            editor.Files.SelectedItem.Should().BeNull();
            editor.OrderInput.SelectedItem = OwnedDefinitionInventoryOrder.DisplayNameAscending;
            var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
            editor.OrderInput.RaiseEvent(escape);
            escape.Handled.Should().BeTrue();
            editor.OrderInput.SelectedItem.Should().Be(OwnedDefinitionInventoryOrder.FileNameAscending);
            await f.Run(f.Prefix + " compare");
            var historical = comparison!.GetVisualDescendants().OfType<TextBox>().Where(box => box.IsReadOnly).ToArray();
            historical[0].Text.Should().Be(baseline);
            historical[1].Text.Should().Be(draft);
            editor.Source.Text.Should().Be(draft);
            f.Catalogue.Candidates.Should().BeSameAs(catalogue);
            f.Store.Reads.Should().Be(0); f.Store.Lists.Should().Be(0); f.Store.Writes.Should().Be(0);
            File.ReadAllBytes(f.SourcePath).Should().Equal(bytes);
        });
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction, false)]
    [InlineData(ArtifactKind.Prompt, false)]
    [InlineData(ArtifactKind.Instruction, true)]
    [InlineData(ArtifactKind.Prompt, true)]
    public async Task OrderEditFencesPendingObservationAndCancellationCannotRestoreRetiredPrivateState(ArtifactKind kind, bool privacy)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(kind, false);
            var editor = f.Editor!;
            await f.Run(f.Prefix + " list");
            f.Store.Reads = 0; f.Store.Lists = 0; f.Store.Writes = 0;
            editor.Source.Text += "\r\nPRIVATE_DRAFT\r\n";
            var draft = editor.Source.Text;
            f.Store.DenyAccess = true;
            Window? comparison = null;
            using var subscription = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            { if (window.Title?.StartsWith("Passive owned", StringComparison.Ordinal) == true) { comparison = window; } });
            f.Sessions.PauseNext = true;
            var pending = f.Run(f.Prefix + " compare");
            await f.Sessions.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            editor.OrderInput.SelectedItem = OwnedDefinitionInventoryOrder.FileNameDescending;
            if (privacy) { f.Session.Main.CloseForObservedPrivacyEvent("Synthetic order cancellation", hidePresentation: true); }
            f.Sessions.Release.TrySetResult();
            await pending;
            comparison.Should().BeNull();
            editor.Files.SelectedItem.Should().BeNull();
            editor.ReviewUse.IsEnabled.Should().BeFalse();
            editor.Source.Text.Should().Be(privacy ? string.Empty : draft);
            if (privacy)
            {
                editor.IsVisible.Should().BeFalse();
                editor.Files.ItemsSource.Should().BeNull();
                editor.OrderInput.SelectedItem.Should().Be(OwnedDefinitionInventoryOrder.FileNameAscending);
                editor.OrderInput.SelectedItem = OwnedDefinitionInventoryOrder.DisplayNameDescending;
                editor.Files.ItemsSource.Should().BeNull();
            }
            else
            {
                f.Catalogue.ReportReloadFailure(new IOException("Synthetic source unavailable"));
                editor.OrderInput.SelectedItem = OwnedDefinitionInventoryOrder.DisplayNameDescending;
                editor.Files.ItemsSource.Should().BeNull();
                editor.Source.Text.Should().Be(draft);
                f.Status.Should().Contain("Explicit List/Refresh required");
            }
            f.Store.Reads.Should().Be(0); f.Store.Lists.Should().Be(0); f.Store.Writes.Should().Be(0);
        });
    }

    [Fact]
    public async Task DisposedInventoryOrderCallbacksCannotReadOrRestorePrivateControls()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false);
            var editor = f.Editor!;
            f.Store.DenyAccess = true;
            await f.DisposeAsync();
            editor.OrderInput.SelectedItem = OwnedDefinitionInventoryOrder.DisplayNameDescending;
            editor.ClearSearch();
            editor.IsVisible.Should().BeFalse();
            editor.Source.Text.Should().BeEmpty();
            editor.Files.ItemsSource.Should().BeNull();
            editor.Files.SelectedItem.Should().BeNull();
            editor.NextPage.IsEnabled.Should().BeFalse();
            f.Store.Reads.Should().Be(0); f.Store.Lists.Should().Be(0); f.Store.Writes.Should().Be(0);
        });
    }
}

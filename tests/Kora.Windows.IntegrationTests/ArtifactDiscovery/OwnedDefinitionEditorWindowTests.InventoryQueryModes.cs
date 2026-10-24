using Avalonia.Controls;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Core.Artifacts;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class OwnedDefinitionEditorWindowTests
{
    [Theory]
    [InlineData(ArtifactKind.Instruction, OwnedDefinitionInventoryQueryMode.ExactOwnedId)]
    [InlineData(ArtifactKind.Instruction, OwnedDefinitionInventoryQueryMode.ExactArtifactId)]
    [InlineData(ArtifactKind.Prompt, OwnedDefinitionInventoryQueryMode.ExactOwnedId)]
    [InlineData(ArtifactKind.Prompt, OwnedDefinitionInventoryQueryMode.ExactArtifactId)]
    public async Task ExactInventoryModeRetiresPendingObservationAndPrivateCancellationReleasesWithoutSourceAccess(
        ArtifactKind kind, OwnedDefinitionInventoryQueryMode mode)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(kind, false);
            var editor = f.Editor!;
            await f.Run(f.Prefix + " list");
            f.Store.Reads = 0; f.Store.Lists = 0; f.Store.Writes = 0;
            editor.Source.Text += "\r\nPRIVATE_UNSAVED_RAW_DRAFT\r\n";
            var draft = editor.Source.Text;
            var sourceBytes = File.ReadAllBytes(f.SourcePath);
            f.Store.DenyAccess = true;
            editor.QueryModeInput.SelectedItem = null;
            editor.Files.ItemsSource.Should().BeNull();
            f.Status.Should().Contain("Select an explicit owned metadata navigation mode");
            editor.QueryModeInput.SelectedItem = mode;
            editor.QueryInput.Text = mode == OwnedDefinitionInventoryQueryMode.ExactOwnedId ? "example" : f.Source!.Document.Definition.Id;
            editor.Files.ItemsSource!.Cast<OwnedDefinitionInventoryRow>().Should().ContainSingle();
            editor.Files.SelectedItem.Should().BeNull();
            editor.Source.Text.Should().Be(draft);
            editor.ReviewUse.IsEnabled.Should().BeFalse();
            f.Sessions.PauseNext = true;
            var pending = f.Run(f.Prefix + " compare");
            await f.Sessions.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            editor.QueryModeInput.SelectedItem = OwnedDefinitionInventoryQueryMode.Literal;
            editor.Source.Text.Should().Be(draft);
            f.Session.Main.CloseForObservedPrivacyEvent("Synthetic inventory cancellation", hidePresentation: true);
            await pending;
            await f.Session.Main.PrivacyClosureTask;
            editor.IsVisible.Should().BeFalse();
            editor.Source.Text.Should().BeEmpty();
            editor.QueryInput.Text.Should().BeEmpty();
            editor.QueryModeInput.SelectedItem.Should().Be(OwnedDefinitionInventoryQueryMode.Literal);
            editor.Files.ItemsSource.Should().BeNull();
            editor.Files.SelectedItem.Should().BeNull();
            editor.NextPage.IsEnabled.Should().BeFalse();
            editor.GetVisualDescendants().OfType<TextBlock>().Should().NotContain(block =>
                block.Text != null && block.Text.Contains("PRIVATE_UNSAVED_RAW_DRAFT", StringComparison.Ordinal));
            f.Sessions.Release.TrySetResult();
            f.Store.Reads.Should().Be(0);
            f.Store.Lists.Should().Be(0);
            f.Store.Writes.Should().Be(0);
            File.ReadAllBytes(f.SourcePath).Should().Equal(sourceBytes);
        });
    }
}

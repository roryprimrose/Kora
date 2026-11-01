using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using AwesomeAssertions;
using Kora.Core.Skills;
using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed partial class OwnedSkillAuthoringNativeTests
{
    [WindowsFact]
    public async Task NativeReviewsShowBothCompleteExactFilesAndRequireDistinctConfirmation()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "Literal\nowned skill save\n");
            var editor = new OwnedSkillEditorWindow(_ => { }, static () => true);
            editor.IdInput.Text = document.Id;
            editor.VersionInput.Text = document.Version;
            editor.NameInput.Text = document.Name;
            editor.DescriptionInput.Text = document.Description;
            editor.Guidance.Text = document.Guidance;
            editor.Document().Guidance.Should().Be(document.Guidance);
            editor.Show();
            var reviews = new List<string>();
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((dialog, _) =>
            {
                if (ReferenceEquals(dialog, editor)) { return; }
                reviews.Add(dialog.Title!);
                var children = dialog.GetLogicalDescendants().ToArray();
                var sources = children.OfType<TextBox>().ToArray();
                sources.Should().HaveCount(2);
                sources[0].Text.Should().Be(document.ManifestText);
                sources[1].Text.Should().Be(document.Guidance);
                sources.Should().OnlyContain(source => source.IsReadOnly);
                children.OfType<TextBlock>().Should().Contain(label => label.Text != null
                    && label.Text.Contains("fixed-owned-package-destination", StringComparison.Ordinal));
                var buttons = children.OfType<Button>().ToArray();
                buttons[0].Content.Should().Be(reviews.Count == 1
                    ? "Continue to separate native publication confirmation" : "Publish exact new version");
                Avalonia.Threading.Dispatcher.UIThread.Post(() => buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            });
            (await editor.Review(document, "fixed-owned-package-destination", TestContext.Current.CancellationToken)).Should().BeTrue();
            reviews.Should().ContainSingle();
            (await editor.ConfirmPublication(document, "fixed-owned-package-destination", TestContext.Current.CancellationToken)).Should().BeTrue();
            reviews.Should().Equal("Review complete skill package", "Confirm exact new skill publication");
            editor.Close();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeEscapeRejectsSaveReviewWithoutConfirmingOrEditing(bool publication)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "Literal\nowned skill save\n");
            var commands = 0;
            var editor = new OwnedSkillEditorWindow(_ => commands++, static () => true);
            editor.IdInput.Text = document.Id;
            editor.VersionInput.Text = document.Version;
            editor.NameInput.Text = document.Name;
            editor.DescriptionInput.Text = document.Description;
            editor.Guidance.Text = document.Guidance;
            editor.Show();
            var next = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((dialog, _) =>
            {
                if (!ReferenceEquals(dialog, editor)) { next.TrySetResult(dialog); }
            });
            var operation = publication
                ? editor.ConfirmPublication(document, "fixed-owned-package-destination", TestContext.Current.CancellationToken)
                : editor.Review(document, "fixed-owned-package-destination", TestContext.Current.CancellationToken);
            var review = await next.Task;
            var sources = review.GetLogicalDescendants().OfType<TextBox>().ToArray();
            sources.Should().HaveCount(2).And.OnlyContain(source => source.IsReadOnly);
            sources[0].Text.Should().Be(document.ManifestText);
            sources[1].Text.Should().Be(document.Guidance);
            sources[1].Focus().Should().BeTrue();
            review.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            review.IsVisible.Should().BeFalse();
            (await operation).Should().BeFalse();
            editor.IsVisible.Should().BeTrue();
            editor.Document().ManifestText.Should().Be(document.ManifestText);
            editor.Document().Guidance.Should().Be(document.Guidance);
            commands.Should().Be(0);
            editor.Close();
        });
    }

    [WindowsFact]
    public async Task DirtyCancelAndRetiredDraftNeverCreatePublicationAuthority()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var current = true;
            var editor = new OwnedSkillEditorWindow(_ => { }, () => current);
            editor.Show();
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((dialog, _) =>
            {
                if (ReferenceEquals(dialog, editor)) { return; }
                var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToArray();
                Avalonia.Threading.Dispatcher.UIThread.Post(() => buttons[^1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            });
            (await editor.DirtyChoice(true, static () => true, TestContext.Current.CancellationToken)).Should().Be(2);
            current = false;
            (await editor.DirtyChoice(false, static () => true, TestContext.Current.CancellationToken)).Should().Be(2);
            var document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "Literal");
            (await editor.ConfirmPublication(document, "fixed-destination", TestContext.Current.CancellationToken)).Should().BeFalse();
            editor.Close();
        });
    }

    [WindowsFact]
    public async Task HiddenRetiredDraftRequiresFreshPrivatePresentationAndAllowsOnlyExplicitDiscardOrCancel()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var editor = new OwnedSkillEditorWindow(_ => { }, static () => false);
            editor.Show();
            editor.Guidance.Text = "Unsaved private draft";
            editor.Hide();
            editor.ShowForPrivateRecovery(static () => false).Should().BeFalse();
            editor.IsVisible.Should().BeFalse();
            editor.ShowForPrivateRecovery(static () => true).Should().BeTrue();
            editor.IsVisible.Should().BeTrue();
            editor.Guidance.Text.Should().Be("Unsaved private draft");
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((dialog, _) =>
            {
                if (ReferenceEquals(dialog, editor)) { return; }
                var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToArray();
                buttons.Select(button => button.Content).Should().Equal("Discard", "Cancel");
                Avalonia.Threading.Dispatcher.UIThread.Post(() => buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            });
            (await editor.DirtyChoice(false, static () => true, TestContext.Current.CancellationToken)).Should().Be(1);
            editor.Close();
        });
    }
}

using Avalonia.Controls;
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
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try { AssertModalHandoff(editor, dialog); }
                    finally { buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                });
            });
            (await editor.Review(document, "fixed-owned-package-destination", TestContext.Current.CancellationToken)).Should().BeTrue();
            reviews.Should().ContainSingle();
            (await editor.ConfirmPublication(document, "fixed-owned-package-destination", TestContext.Current.CancellationToken)).Should().BeTrue();
            reviews.Should().Equal("Review complete skill package", "Confirm exact new skill publication");
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
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try { AssertModalHandoff(editor, dialog); }
                    finally { buttons[^1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                });
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

    [Theory]
    [InlineData("close")]
    [InlineData("retire")]
    [InlineData("cancel")]
    [InlineData("owner-hidden")]
    [InlineData("owner-closed")]
    public async Task AcceptedChoiceWaitsForNativeClosureAndRechecksOriginalEligibility(string scenario)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var current = true;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "Literal");
            var editor = new OwnedSkillEditorWindow(_ => { }, () => current);
            editor.Guidance.Text = document.Guidance;
            editor.Show();
            Window? review = null;
            var holdClose = true;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((dialog, _) =>
            {
                if (ReferenceEquals(dialog, editor)) { return; }
                review = dialog;
                dialog.Closing += (_, args) => args.Cancel = holdClose;
            });
            var choosing = editor.Review(document, "fixed-destination", cancellation.Token);
            try
            {
                var dialog = review!;
                var button = dialog.GetLogicalDescendants().OfType<Button>().First();
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                dialog.IsVisible.Should().BeTrue();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                choosing.IsCompleted.Should().BeFalse("a clicked choice cannot complete before its native window closes");
                if (scenario is "retire") { current = false; }
                if (scenario is "cancel") { cancellation.Cancel(); }
                if (scenario is "owner-hidden") { editor.Hide(); }
                if (scenario is "owner-closed") { holdClose = false; editor.Close(); }
                if (scenario is "owner-hidden" or "owner-closed") { editor.IsVisible.Should().BeFalse(); }
                holdClose = false;
                dialog.Close();
                (await choosing).Should().Be(scenario is "close");
                dialog.IsVisible.Should().BeFalse();
                editor.Guidance.Text.Should().Be(document.Guidance);
                editor.IsVisible.Should().Be(scenario is not ("owner-hidden" or "owner-closed"));
            }
            finally
            {
                holdClose = false;
                review?.Close();
                editor.Close();
            }
        });
    }

    private static void AssertModalHandoff(Window editor, Window dialog)
    {
        dialog.Owner.Should().BeSameAs(editor);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var editorActivations = 0;
        var dialogActivations = 0;
        void EditorActivated(object? sender, EventArgs args) => editorActivations++;
        void DialogActivated(object? sender, EventArgs args) => dialogActivations++;
        editor.Activated += EditorActivated;
        dialog.Activated += DialogActivated;
        try
        {
            // Headless input has no public gesture for Avalonia's internal disabled-window callback.
            var disabledInput = editor.PlatformImpl!.GetType().GetProperty("GotInputWhenDisabled",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            disabledInput.Should().NotBeNull();
            disabledInput!.GetValue(editor.PlatformImpl).Should().BeOfType<Action>().Which.Invoke();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            dialogActivations.Should().Be(1, "blocked owner input must return to the exact native review");
            editorActivations.Should().Be(0, "the draft editor must not reactivate behind its review");
        }
        finally
        {
            editor.Activated -= EditorActivated;
            dialog.Activated -= DialogActivated;
        }
    }
}

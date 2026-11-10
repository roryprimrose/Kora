using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.NativeUxFixture;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class NativeReviewTabsTests
{
    [Fact]
    public async Task SwitchingAndHidingExactReviewTabsRetiresBodiesWithoutOpeningAnotherWindow()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetFullPath("."), silentCaption: true);
            await f.InitializeAsync();
            var window = new ResponseWindow(f.Main);
            window.Show();
            window.Activate();
            try
            {
                var firstOwner = new Border();
                var secondOwner = new Border();
                var first = new TextBox { IsReadOnly = true };
                var second = new TextBox { IsReadOnly = true };
                var firstPresented = false;
                window.ShowNativeReview(firstOwner, "First original review", first, static () => true, shown =>
                { firstPresented = shown; first.Text = shown ? "first exact private review" : string.Empty; });
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                first.Text.Should().Be("first exact private review");
                window.IsExpanded.Should().BeTrue();
                window.ShowNativeReview(secondOwner, "Second original review", second, static () => true,
                    shown => second.Text = shown ? "second exact private review" : string.Empty);
                Dispatcher.UIThread.RunJobs();
                first.Text.Should().BeNullOrEmpty();
                firstPresented.Should().BeFalse();
                second.Text.Should().Be("second exact private review");
                var host = window.FindControl<ContentControl>("NativeReviewContent")!;
                var tabs = host.GetVisualDescendants().OfType<TabControl>().Single();
                tabs.ItemCount.Should().Be(2);
                tabs.SelectedItem = tabs.Items[0];
                Dispatcher.UIThread.RunJobs();
                first.Text.Should().Be("first exact private review");
                second.Text.Should().BeNullOrEmpty();
                var item = (TabItem)tabs.Items[0]!;
                ((StackPanel)item.Header!).Children.OfType<Button>()
                    .Single(button => string.Equals(button.Name, "HideNativeReview", StringComparison.Ordinal))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                first.Text.Should().BeNullOrEmpty();
                tabs.ItemCount.Should().Be(1);
                window.RemoveNativeReview(secondOwner);
                second.Text.Should().BeNullOrEmpty();
                host.IsVisible.Should().BeFalse();
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("hide-return")]
    [InlineData("minimize-restore")]
    [InlineData("collapse-expand")]
    public async Task RestoredWorkbenchCannotAutomaticallyRenewAnOldReview(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetFullPath("."), silentCaption: true);
            await f.InitializeAsync();
            var window = new ResponseWindow(f.Main);
            window.Show();
            window.Activate();
            try
            {
                var reviewOwner = new Border();
                var body = new TextBox { IsReadOnly = true };
                window.ShowNativeReview(reviewOwner, "Original native review", body, static () => true,
                    shown => body.Text = shown ? "private original review" : string.Empty);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                body.Text.Should().NotBeNullOrEmpty();
                switch (boundary)
                {
                    case "hide-return": window.Hide(); window.Show(); window.Activate(); break;
                    case "minimize-restore": window.WindowState = WindowState.Minimized; window.WindowState = WindowState.Normal; break;
                    case "collapse-expand":
                        window.FindControl<Button>("CollapseWorkbench")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        window.ShowSessions(expanded: true);
                        break;
                }
                Dispatcher.UIThread.RunJobs();
                body.Text.Should().BeNullOrEmpty("restoration is not fresh exact review admission");
                window.ShowNativeReview(reviewOwner, "Original native review", body, static () => true,
                    shown => body.Text = shown ? "private original review" : string.Empty);
                Dispatcher.UIThread.RunJobs();
                body.Text.Should().Be("private original review");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task NinthNativeReviewIsRefusedWithoutEvictingExistingReviewDescriptors()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetFullPath("."), silentCaption: true);
            await f.InitializeAsync();
            var window = new ResponseWindow(f.Main);
            window.Show();
            window.Activate();
            try
            {
                for (var index = 0; index < 8; index++)
                {
                    var owner = new Border();
                    var body = new TextBox { IsReadOnly = true };
                    window.ShowNativeReview(owner, "Original review " + index, body,
                        static () => true, shown => body.Text = shown ? "exact original" : string.Empty);
                }
                Dispatcher.UIThread.RunJobs();
                var host = window.FindControl<ContentControl>("NativeReviewContent")!;
                var tabs = host.GetVisualDescendants().OfType<TabControl>().Single();
                tabs.ItemCount.Should().Be(8);
                var retained = tabs.Items.ToArray();
                Action ninth = () => window.ShowNativeReview(new Border(), "Ninth original review",
                    new TextBox { IsReadOnly = true }, static () => true, _ => { });
                ninth.Should().Throw<InvalidOperationException>().WithMessage("*Eight native review tabs*");
                tabs.Items.Should().Equal(retained);
                tabs.ItemCount.Should().Be(8);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task NarrowNativeReviewUsesTheReadingAreaWithADividerOnlyInWideMode()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetFullPath("."), silentCaption: true);
            await f.InitializeAsync();
            var window = new ResponseWindow(f.Main);
            window.Show();
            window.Activate();
            try
            {
                var body = new TextBox { IsReadOnly = true };
                window.ShowNativeReview(new Border(), "Original review", body, static () => true,
                    shown => body.Text = shown ? "original native source" : string.Empty);
                window.Width = 640;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var host = window.FindControl<ContentControl>("NativeReviewContent")!;
                Grid.GetColumn(host).Should().Be(1);
                host.Bounds.Width.Should().BeGreaterThan(300);
                host.IsEffectivelyVisible.Should().BeTrue();
                window.FindControl<GridSplitter>("NativeReviewDivider")!.IsVisible.Should().BeFalse();
                body.Text.Should().Be("original native source");
                var back = host.GetVisualDescendants().OfType<Button>()
                    .Single(button => string.Equals(button.Name, "ReturnFromNativeReview", StringComparison.Ordinal));
                back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                host.IsVisible.Should().BeFalse();
                body.Text.Should().BeNullOrEmpty();
                window.FindControl<Button>("ShowNativeReviews")!.IsVisible.Should().BeTrue();
                window.FindControl<Button>("ShowNativeReviews")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                body.Text.Should().BeNullOrEmpty("showing metadata tabs is not renewed admission");
                window.ShowNativeReview(new Border(), "Another original review", new TextBox(),
                    static () => true, _ => { });
                window.Width = 1120;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Grid.GetColumn(host).Should().Be(3);
                window.FindControl<GridSplitter>("NativeReviewDivider")!.IsVisible.Should().BeTrue();
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task ReopeningAnOwnedReviewReplacesItsAdmissionAndPublicationBindingWithoutSubstitutingTheControl()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetFullPath("."), silentCaption: true);
            await f.InitializeAsync();
            var window = new ResponseWindow(f.Main);
            window.Show();
            window.Activate();
            try
            {
                var reviewOwner = new Border();
                var body = new TextBox { IsReadOnly = true };
                var oldAdmitted = true;
                var oldPublications = 0;
                window.ShowNativeReview(reviewOwner, "Original review", body, () => oldAdmitted, shown =>
                { oldPublications++; body.Text = shown ? "original review" : string.Empty; });
                Dispatcher.UIThread.RunJobs();
                body.Text.Should().Be("original review");
                oldAdmitted = false;
                var newAdmitted = true;
                window.ShowNativeReview(reviewOwner, "Revalidated review", body, () => newAdmitted,
                    shown => body.Text = shown ? "revalidated review" : string.Empty);
                Dispatcher.UIThread.RunJobs();
                body.Text.Should().Be("revalidated review");
                var retiredPublications = oldPublications;
                var tabs = window.FindControl<ContentControl>("NativeReviewContent")!
                    .GetVisualDescendants().OfType<TabControl>().Single();
                tabs.ItemCount.Should().Be(1);
                newAdmitted = false;
                window.Hide();
                body.Text.Should().BeNullOrEmpty();
                oldPublications.Should().Be(retiredPublications, "the old callback must not receive replacement lifetime changes");
                Action substitute = () => window.ShowNativeReview(reviewOwner, "Substitute",
                    new TextBox(), static () => true, _ => { });
                window.Show();
                substitute.Should().Throw<InvalidOperationException>().WithMessage("*cannot substitute*");
                tabs.ItemCount.Should().Be(1);
            }
            finally { window.Close(); }
        });
    }
}

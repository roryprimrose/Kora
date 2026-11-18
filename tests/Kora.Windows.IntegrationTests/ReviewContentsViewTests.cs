using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Presentation;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class ReviewContentsViewTests
{
    private static ReviewContents Package() => new("Example skill", "0123456789abcdef",
    [
        ReviewDocument.FromText("SKILL.md", "# Skill"),
        ReviewDocument.FromText("scripts/run.ps1", "Write-Output 1"),
        ReviewDocument.FromText("scripts/lib/helper.ps1", "function Help {}"),
    ], ["refs/missing.md (reference not read)"]);

    [Fact]
    public Task TreeGroupsFoldersAndShowsTheExactSelectedFile() => HeadlessSession.RunAsync(() =>
    {
        var view = new ReviewContentsView(Package());
        var window = new Window { Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var roots = view.Tree.Items.OfType<TreeViewItem>().ToArray();
            roots.Select(item => item.Header).Should().Equal("scripts/", "SKILL.md");
            var scripts = roots[0];
            scripts.Items.OfType<TreeViewItem>().Select(item => item.Header).Should().Equal("lib/", "run.ps1");
            view.Selected!.Path.Should().Be("SKILL.md");
            view.DocumentText.Text.Should().Be("# Skill");
            view.DocumentText.IsReadOnly.Should().BeTrue();

            view.Tree.SelectedItem = ((TreeViewItem)scripts.Items[0]!).Items[0];
            Dispatcher.UIThread.RunJobs();

            view.DocumentPath.Text.Should().Be("scripts/lib/helper.ps1");
            view.DocumentText.Text.Should().Be("function Help {}");
            view.DocumentDigest.Text.Should().Be("SHA-256 " + ReviewDocument.FromText("x", "function Help {}").Digest);
            view.UnavailableNotice!.Text.Should().Contain("refs/missing.md");

            view.Tree.SelectedItem = scripts;
            Dispatcher.UIThread.RunJobs();
            view.DocumentPath.Text.Should().Be("scripts/lib/helper.ps1", "selecting a folder keeps the last file shown");
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SingleFileContentsOmitTheTree() => HeadlessSession.RunAsync(() =>
    {
        var view = new ReviewContentsView(ReviewContents.FromText("Prompt", "request.md", "Hello"));
        var window = new Window { Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            view.IsSingleDocument.Should().BeTrue();
            view.GetVisualDescendants().OfType<TreeView>().Should().BeEmpty();
            view.DocumentText.Text.Should().Be("Hello");
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task LinkOpensOnlyCurrentContentsAndPhraseUsesTheLatestCurrentReview() => HeadlessSession.RunAsync(() =>
    {
        var older = new ReviewContentsLink();
        var newer = new ReviewContentsLink();
        var host = new StackPanel();
        host.Children.Add(older.Button);
        host.Children.Add(newer.Button);
        var window = new Window { Content = host };
        try
        {
            window.Show();
            older.Button.IsEnabled.Should().BeFalse();
            ReviewContentsPresentation.TryShowLatest().Should().BeFalse();

            var olderCurrent = true;
            var newerCurrent = true;
            older.Offer(Package(), () => olderCurrent);
            newer.Offer(ReviewContents.FromText("Prompt", "request.md", "Hello"), () => newerCurrent);
            older.Button.IsEnabled.Should().BeTrue();

            ReviewContentsPresentation.TryShowLatest().Should().BeTrue();
            ShownTitle(newer).Should().Be("Prompt");
            window.Content.Should().BeSameAs(host, "a standalone review keeps its own window; contents open separately");

            newerCurrent = false;
            ReviewContentsPresentation.TryShowLatest().Should().BeTrue("a stale newer review falls back to the older current one");
            ShownTitle(older).Should().Be("Example skill");
            newer.Button.IsEnabled.Should().BeFalse("stale reviews are retired, not shown");

            older.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ShownTitle(older).Should().Be("Example skill");

            olderCurrent = false;
            older.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            older.Button.IsEnabled.Should().BeFalse();
            ReviewContentsPresentation.TryShowLatest().Should().BeFalse();
        }
        finally
        {
            older.Dispose();
            newer.Dispose();
            window.Close();
        }
    });

    private static string? ShownTitle(ReviewContentsLink link) =>
        (link.Shown?.Content as ReviewContentsView)?.Contents.Title;
}

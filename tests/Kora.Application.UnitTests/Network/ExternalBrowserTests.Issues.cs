using AwesomeAssertions;

using Kora.Core.Network.ExternalBrowser;

namespace Kora.Application.UnitTests.Network;

public sealed partial class ExternalBrowserTests
{
    [Fact]
    public void Issue_review_replaces_and_retires_both_retrieved_and_final_source_reviews()
    {
        using var root = Root();
        var workflow = Create(new Launcher());
        var destination = ExternalBrowserDestination.Validate("https://github.com/roryprimrose/Kora/issues/new")!;
        var retrieved = workflow.Prepare(Snapshot(), static () => true);
        var issue = workflow.PrepareIssue(destination, static () => true);
        workflow.IsCurrent(retrieved).Should().BeFalse();
        workflow.IsCurrent(issue).Should().BeTrue();
        var (_, source) = FinalSource();
        var request = source.CaptureNavigation(0, FinalLink.Length, FinalAddress);
        var final = workflow.Prepare(request, static () => true);
        workflow.PrepareIssue(destination, static () => true);
        workflow.IsCurrent(final).Should().BeFalse();
        request.IsCurrent.Should().BeFalse();
        request.Destination.Should().BeEmpty();
        FluentActions.Invoking(() => workflow.PrepareIssue(null!, static () => true))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => workflow.PrepareIssue(destination, null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Active_browser_handoff_cannot_be_replaced_by_an_issue_review()
    {
        using var root = Root();
        var launcher = new Launcher { Release = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var workflow = Create(launcher);
        var review = workflow.Prepare(Snapshot(), static () => true);
        var pending = workflow.ConfirmAsync(review.Id, static () => true, Token);
        FluentActions.Invoking(() => workflow.PrepareIssue(
            ExternalBrowserDestination.Validate("https://github.com/roryprimrose/Kora/issues/12")!, static () => true))
            .Should().Throw<InvalidOperationException>();
        launcher.Release.SetResult();
        (await pending).Should().Be(ExternalBrowserLaunchOutcome.Unknown);
        launcher.Calls.Should().Be(1);
    }
}

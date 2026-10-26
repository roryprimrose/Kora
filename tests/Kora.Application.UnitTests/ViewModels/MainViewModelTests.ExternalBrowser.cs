using AwesomeAssertions;
using Kora.Application.Network.ExternalBrowser;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Network.ExternalBrowser;
using Kora.Core.Presentation;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public void ExternalBrowserCompositionRejectsNullReplacementAndDisposedHost()
    {
        var fixture = new Fixture();
        var workflow = BrowserWorkflow(new());
        FluentActions.Invoking(() => fixture.ViewModel.BindExternalBrowserWorkflow(null!)).Should().Throw<ArgumentNullException>();
        fixture.ViewModel.BindExternalBrowserWorkflow(workflow);
        FluentActions.Invoking(() => fixture.ViewModel.BindExternalBrowserWorkflow(workflow)).Should().Throw<InvalidOperationException>();
        fixture.ViewModel.Dispose();
        FluentActions.Invoking(() => fixture.ViewModel.BindExternalBrowserWorkflow(workflow)).Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task ExternalBrowserRequiresCurrentExactSnapshotComposedWorkflowAndExplicitNativeConfirmation()
    {
        var fixture = WebFixture();
        var launch = new BrowserLauncher();
        using var request = BrowserRoot();
        var missing = new DetailContentReference(new(Guid.NewGuid()), 1);
        FluentActions.Invoking(() => fixture.ViewModel.PrepareExternalBrowser(missing)).Should().Throw<InvalidOperationException>();
        await FluentActions.Awaiting(() => fixture.ViewModel.ConfirmExternalBrowserAsync(Guid.NewGuid())).Should().ThrowAsync<InvalidOperationException>();
        await fixture.RunAsync("get web page https://example.com/");
        var reference = fixture.ViewModel.WebResultDetailsReference!.Value;
        FluentActions.Invoking(() => fixture.ViewModel.PrepareExternalBrowser(reference)).Should().Throw<InvalidOperationException>();
        fixture.ViewModel.BindExternalBrowserWorkflow(BrowserWorkflow(launch));
        FluentActions.Invoking(() => fixture.ViewModel.PrepareExternalBrowser(missing)).Should().Throw<InvalidOperationException>();
        var review = fixture.ViewModel.PrepareExternalBrowser(reference);
        var notComposed = new Fixture();
        notComposed.ViewModel.IsExternalBrowserReviewCurrent(review).Should().BeFalse();
        notComposed.ViewModel.Dispose();
        fixture.ViewModel.IsExternalBrowserReviewCurrent(review).Should().BeTrue();
        launch.Calls.Should().Be(0);
        (await fixture.ViewModel.ConfirmExternalBrowserAsync(review.Id)).Should().Be(ExternalBrowserLaunchOutcome.Accepted);
        (await fixture.ViewModel.ConfirmExternalBrowserAsync(review.Id)).Should().Be(ExternalBrowserLaunchOutcome.Denied);
        launch.Calls.Should().Be(1);
        fixture.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("privacy")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("cancel")]
    [InlineData("dispose")]
    public async Task ExternalBrowserReviewCannotSurviveSourceOrAuthorityChanges(string closure)
    {
        var fixture = WebFixture();
        var launch = new BrowserLauncher();
        fixture.ViewModel.BindExternalBrowserWorkflow(BrowserWorkflow(launch));
        await fixture.RunAsync("get web page https://example.com/");
        using var request = BrowserRoot();
        var review = fixture.ViewModel.PrepareExternalBrowser(fixture.ViewModel.WebResultDetailsReference!.Value);
        switch (closure)
        {
            case "source": fixture.ViewModel.RetireWebResultDetails(); break;
            case "privacy": fixture.ViewModel.CloseForObservedPrivacyEvent("locked", true); await fixture.ViewModel.PrivacyClosureTask; break;
            case "owner": fixture.ViewModel.BindCallOwnershipGate(static () => false); break;
            case "call": fixture.CallState.SetState(Kora.Core.Communication.CallState.Active); await fixture.ViewModel.CallClosureTask; break;
            case "cancel": fixture.ViewModel.CancelExternalBrowserReview(); break;
            case "dispose": fixture.ViewModel.Dispose(); break;
        }
        fixture.ViewModel.IsExternalBrowserReviewCurrent(review).Should().BeFalse();
        (await fixture.ViewModel.ConfirmExternalBrowserAsync(review.Id)).Should().Be(ExternalBrowserLaunchOutcome.Denied);
        launch.Calls.Should().Be(0);
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task ExternalBrowserExitWaitsForNativeHandshakeWithoutClaimingBrowserQuiescence()
    {
        var fixture = WebFixture();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var launch = new BrowserLauncher { Release = release };
        fixture.ViewModel.BindExternalBrowserWorkflow(BrowserWorkflow(launch));
        await fixture.RunAsync("get web page https://example.com/");
        using var request = BrowserRoot();
        var review = fixture.ViewModel.PrepareExternalBrowser(fixture.ViewModel.WebResultDetailsReference!.Value);
        var confirmation = fixture.ViewModel.ConfirmExternalBrowserAsync(review.Id);
        await launch.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var exit = fixture.ViewModel.ExitAsync();
        exit.IsCompleted.Should().BeFalse();
        release.SetResult();
        (await confirmation).Should().Be(ExternalBrowserLaunchOutcome.Unknown);
        await exit;
        launch.Calls.Should().Be(1);
        fixture.ViewModel.Dispose();
    }

    private static HostActivity BrowserRoot() => HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
        HostActivityLayer.Application, HostOperation.Request);
    private static ExternalBrowserWorkflow BrowserWorkflow(BrowserLauncher launch) =>
        new(launch, new BrowserNetwork(), new ViewModelWebAudit(), TimeProvider.System);

    private sealed class BrowserNetwork : IExternalBrowserNetworkPolicy
    {
        public ExternalBrowserNetworkAdmission Capture() => new(true, static () => true);
    }

    private sealed class BrowserLauncher : IExternalBrowserLauncher
    {
        public int Calls { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? Release { get; init; }
        public async Task<ExternalBrowserLaunchOutcome> LaunchAsync(ExternalBrowserDestination destination, Func<bool> admitted, CancellationToken cancellationToken)
        {
            admitted().Should().BeTrue();
            Calls++;
            Entered.SetResult();
            if (Release is not null) { await Release.Task; }
            return ExternalBrowserLaunchOutcome.Accepted;
        }
    }
}

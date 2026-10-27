using AwesomeAssertions;
using Kora.Application.Network.ExternalBrowser;
using Kora.Application.Presentation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Network.ExternalBrowser;
using Kora.Core.Presentation;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinalDocumentBrowserUsesExistingHostPrivacyNetworkAndOriginalSource(bool close)
    {
        var f = CreateSummaryFixture(SpeechProviderIds.Windows);
        await f.ViewModel.InitializeAsync();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var original = HostRequest.Create(RequestOrigin.ActivatedVoice);
        const string text = "[site](https://example.com/final)";
        var content = new AdmittedDetailContent(new(new(Guid.NewGuid()), 1), DetailContentKind.Markdown,
            DetailContentOrigin.FinalizedResponse, DetailSensitivity.DisclosureConfirmationRequired,
            "Final answer", "Exact source", text, new(original.SessionId, original.RequestId, original.TaskId));
        var state = new DetailViewerState(content, () => true);
        state.CompleteRender(state.Generation, text, "Rendered", false);
        var source = state.CaptureFinalDocument();
        f.ViewModel.CancelExternalBrowserReview(Guid.NewGuid());
        var request = source.CaptureNavigation(0, text.Length, "https://example.com/final");
        FluentActions.Invoking(() => f.ViewModel.PrepareExternalBrowser(request, () => true)).Should().Throw<InvalidOperationException>();
        var launch = new FinalDocumentLauncher();
        f.ViewModel.BindExternalBrowserWorkflow(new(launch, new FinalDocumentNetwork(), f.Audit, TimeProvider.System));
        FluentActions.Invoking(() => f.ViewModel.PrepareExternalBrowser((FinalDocumentNavigationRequest)null!, () => true))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => f.ViewModel.PrepareExternalBrowser(request, null!)).Should().Throw<ArgumentNullException>();
        var current = true;
        var review = f.ViewModel.PrepareExternalBrowser(source.CaptureNavigation(0, text.Length, "https://example.com/final"), () => current);
        f.ViewModel.IsExternalBrowserReviewCurrent(review).Should().BeTrue();
        f.ViewModel.CancelExternalBrowserReview(Guid.NewGuid());
        f.ViewModel.IsExternalBrowserReviewCurrent(review).Should().BeTrue();
        if (close) { f.ViewModel.CancelExternalBrowserReview(review.Id); }
        else { current = false; }
        (await f.ViewModel.ConfirmExternalBrowserAsync(review.Id)).Should().Be(ExternalBrowserLaunchOutcome.Denied);
        launch.Calls.Should().Be(0);
        review = f.ViewModel.PrepareExternalBrowser(source.CaptureNavigation(0, text.Length, "https://example.com/final"), () => true);
        (await f.ViewModel.ConfirmExternalBrowserAsync(review.Id)).Should().Be(ExternalBrowserLaunchOutcome.Accepted);
        launch.Calls.Should().Be(1);
        f.ViewModel.CloseForObservedPrivacyEvent("Windows session locked", true);
        await f.ViewModel.PrivacyClosureTask;
        FluentActions.Invoking(() => f.ViewModel.PrepareExternalBrowser(source.CaptureNavigation(0, text.Length, "https://example.com/final"), () => true))
            .Should().Throw<InvalidOperationException>();
        await f.ViewModel.DisposeAsync();
    }

    private sealed class FinalDocumentNetwork : IExternalBrowserNetworkPolicy
    {
        public ExternalBrowserNetworkAdmission Capture() => new(true, () => true);
    }

    private sealed class FinalDocumentLauncher : IExternalBrowserLauncher
    {
        public int Calls { get; private set; }
        public Task<ExternalBrowserLaunchOutcome> LaunchAsync(ExternalBrowserDestination destination, Func<bool> admitted, CancellationToken cancellationToken)
        {
            admitted().Should().BeTrue();
            destination.ExactAddress.Should().Be("https://example.com/final");
            Calls++;
            return Task.FromResult(ExternalBrowserLaunchOutcome.Accepted);
        }
    }
}

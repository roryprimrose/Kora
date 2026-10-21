using AwesomeAssertions;

using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task NativeTextCountsStayOutOfResponsesTranscriptsHistorySpeechAndModelContext()
    {
        var fixture = new Fixture();
        fixture.ClipboardReader.Text = "hello world";
        await fixture.ViewModel.PreviewClipboardAsync();
        var snapshot = fixture.ViewModel.ClipboardPreview!;
        var title = fixture.ViewModel.ResponseTitle;
        var body = fixture.ViewModel.ResponseBody;
        var transcript = fixture.ViewModel.Transcript;
        var result = fixture.ViewModel.CountClipboardText(snapshot, () => true, TestContext.Current.CancellationToken);
        result.Source.Should().BeSameAs(snapshot);
        result.Statistics.Should().Be(new ClipboardTextStatistics(11, 11, 11, 2, 1));
        fixture.ViewModel.ClipboardPreview.Should().BeSameAs(snapshot);
        fixture.ViewModel.ResponseTitle.Should().Be(title);
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.ViewModel.Transcript.Should().Be(transcript);
        fixture.ClipboardReader.Calls.Should().Be(1);
        fixture.HostStore.Records.Should().BeEmpty();
        fixture.Audit.Events.Should().BeEmpty();
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.ViewModel.ClipboardExplanation.Should().BeNull();
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("dispose")]
    [InlineData("native")]
    public async Task TextCountsFailClosedOnHostOrNativeLoss(string change)
    {
        var fixture = new Fixture();
        await fixture.ViewModel.PreviewClipboardAsync();
        var snapshot = fixture.ViewModel.ClipboardPreview!;
        switch (change)
        {
            case "privacy": fixture.ViewModel.CloseForObservedPrivacyEvent("fixture", hidePresentation: true); break;
            case "owner": fixture.ViewModel.BindClipboardOwnershipGate(() => false); break;
            case "call":
                await fixture.ViewModel.SetManualCallAsync(true, RequestOrigin.LocalUi, fixture.ViewModel.CallPolicyRevision,
                    TestContext.Current.CancellationToken); break;
            case "dispose": fixture.ViewModel.Dispose(); break;
        }
        fixture.ViewModel.CountClipboardText(snapshot, () => change is not "native", CancellationToken.None)
            .Statistics.Should().BeNull();
    }

    [Theory]
    [InlineData(RequestOrigin.ActivatedVoice)]
    [InlineData(RequestOrigin.HostSystem)]
    public async Task AmbientNonNativeOriginCannotBeRelabelledForTextCounts(RequestOrigin origin)
    {
        var fixture = new Fixture();
        await fixture.ViewModel.PreviewClipboardAsync();
        var snapshot = fixture.ViewModel.ClipboardPreview!;
        using var host = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        fixture.ViewModel.CountClipboardText(snapshot, () => true, CancellationToken.None).Statistics.Should().BeNull();
    }

    [Fact]
    public async Task CancellationAndInternalFailureNeverBecomeZeroCountsOrExposeSourceBearingExceptions()
    {
        var fixture = new Fixture();
        await fixture.ViewModel.PreviewClipboardAsync();
        var snapshot = fixture.ViewModel.ClipboardPreview!;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = () => fixture.ViewModel.CountClipboardText(snapshot, () => true, cancellation.Token);
        cancelled.Should().Throw<OperationCanceledException>();
        var failed = () => fixture.ViewModel.CountClipboardText(snapshot,
            () => throw new InvalidOperationException("private marker"), CancellationToken.None);
        failed.Should().Throw<InvalidOperationException>()
            .WithMessage("Native clipboard text counting failed internally. No counts are claimed.")
            .Which.InnerException.Should().BeNull();
    }
}

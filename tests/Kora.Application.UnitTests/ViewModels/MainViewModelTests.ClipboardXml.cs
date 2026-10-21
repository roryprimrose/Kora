using AwesomeAssertions;
using Kora.Core.Context;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("<private/>", ClipboardXmlOutcome.WellFormedXml)]
    [InlineData("<private>", ClipboardXmlOutcome.Invalid)]
    public async Task NativeXmlOutcomeNeverBecomesTranscriptResponseSpeechOrModelSource(string text, ClipboardXmlOutcome expected)
    {
        var fixture = new Fixture();
        fixture.ClipboardReader.Text = text;
        await fixture.ViewModel.PreviewClipboardAsync();
        var snapshot = fixture.ViewModel.ClipboardPreview!;
        var title = fixture.ViewModel.ResponseTitle;
        var body = fixture.ViewModel.ResponseBody;
        var transcript = fixture.ViewModel.Transcript;
        var result = fixture.ViewModel.ValidateClipboardXml(snapshot, () => true, TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(expected);
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

    [Fact]
    public async Task NativeXmlCancellationAndInternalFailureAreNotInvalidXml()
    {
        var fixture = new Fixture();
        fixture.ClipboardReader.Text = "<private/>";
        await fixture.ViewModel.PreviewClipboardAsync();
        var snapshot = fixture.ViewModel.ClipboardPreview!;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = () => fixture.ViewModel.ValidateClipboardXml(snapshot, () => true, cancellation.Token);
        cancelled.Should().Throw<OperationCanceledException>();
        var failed = () => fixture.ViewModel.ValidateClipboardXml(snapshot,
            () => throw new InvalidOperationException("private XML name and secret excerpt"), CancellationToken.None);
        failed.Should().Throw<InvalidOperationException>().WithMessage("Native XML well-formedness checking failed internally. No XML result is claimed.")
            .Which.InnerException.Should().BeNull();
        var denied = fixture.ViewModel.ValidateClipboardXml(snapshot, () => false, CancellationToken.None);
        denied.Reason.Should().Be(ClipboardXmlReason.SourceUnavailable);
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("dispose")]
    public async Task NativeXmlUsesExistingHostEligibilityWithoutModelSourceAdmission(string change)
    {
        var fixture = new Fixture();
        fixture.ClipboardReader.Text = "<r/>";
        await fixture.ViewModel.PreviewClipboardAsync();
        var snapshot = fixture.ViewModel.ClipboardPreview!;
        switch (change)
        {
            case "privacy": fixture.ViewModel.CloseForObservedPrivacyEvent("fixture", hidePresentation: true); break;
            case "owner": fixture.ViewModel.BindClipboardOwnershipGate(() => false); break;
            case "call":
                await fixture.ViewModel.SetManualCallAsync(true, Kora.Core.Hosting.RequestOrigin.LocalUi,
                    fixture.ViewModel.CallPolicyRevision, TestContext.Current.CancellationToken);
                break;
            default: fixture.ViewModel.Dispose(); break;
        }
        fixture.ViewModel.ValidateClipboardXml(snapshot, () => true, CancellationToken.None).Reason
            .Should().Be(ClipboardXmlReason.SourceUnavailable);
        fixture.Reasoner.Requests.Should().BeEmpty();
    }
}

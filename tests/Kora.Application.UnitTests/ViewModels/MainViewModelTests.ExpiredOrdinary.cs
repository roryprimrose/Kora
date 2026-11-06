using AwesomeAssertions;

using Kora.Application.Voice;
using Kora.Core.Communication;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task FreshOrdinaryInputRejectsAnUnconsumedDictationReviewEvenAfterCapturePhysicallyCloses()
    {
        var f = CreateVoicePrivacyFixture();
        f.ViewModel.BindVoiceOwnershipGate(() => true);
        f.Voice.IsLiteralDictationAvailable = true;
        await f.ViewModel.InitializeAsync();
        using var composer = DictationComposer(f);
        composer.Body = "independent original input";
        await using var dictation = f.ViewModel.BindComposerDictation(NullLogger<ComposerDictationService>.Instance);
        f.ViewModel.CaptureFreshOrdinaryRequestEligibility()().Should().BeTrue();
        await dictation.BeginAsync(composer, () => true, TestContext.Current.CancellationToken);
        await f.Voice.RaiseTranscriptAsync("literal text", 1);
        await dictation.EndAsync();
        dictation.HasReview.Should().BeTrue();
        f.Voice.IsCaptureQuiescent.Should().BeTrue();
        f.ViewModel.CaptureFreshOrdinaryRequestEligibility()().Should().BeFalse();
        dictation.Retire("Explicit fixture cancellation.");
        await dictation.Quiescence;
        f.ViewModel.CaptureFreshOrdinaryRequestEligibility()().Should().BeTrue();
        composer.Body.Should().Be("independent original input");
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("input")]
    [InlineData("capture")]
    [InlineData("capture-not-joined")]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("call-revision")]
    [InlineData("owner")]
    [InlineData("name")]
    [InlineData("recovery")]
    public async Task FreshOrdinaryNativeInputReviewPinsCurrentHostInputCapturePrivacyAndCall(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        var current = f.ViewModel.CaptureFreshOrdinaryRequestEligibility();
        current().Should().BeTrue();
        switch (change)
        {
            case "input": await f.RunAsync("help"); break;
            case "capture": f.Voice.AdvanceGeneration(); break;
            case "capture-not-joined": f.Voice.PreventQuiescence = true; break;
            case "privacy": f.Session.IsUnlocked = false; break;
            case "call": f.CallState.SetState(CallState.Active); break;
            case "call-revision": f.CallState.SetState(CallState.Clear); break;
            case "owner": f.ViewModel.BindVoiceOwnershipGate(static () => false); break;
            case "name": await f.ViewModel.SetAssistantNameAsync("Nova"); break;
            case "recovery": await f.ViewModel.SetVoiceConsentAsync(false); break;
        }
        current().Should().BeFalse();
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.Dispose();
    }
}

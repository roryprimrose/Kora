using System.ComponentModel;

using AwesomeAssertions;

using Kora.Application.Infrastructure;
using Kora.Application.Voice;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Voice;

public sealed class SpeechCaptionPresentationContractTests
{
    [Fact]
    public void ExistingPassiveCaptionAdaptersDoNotClaimAnOwnedTypographyWrite()
    {
        ISpeechCaptionPresentation caption = new PassiveCaption();
        caption.IsReaderTypographyUpdatePending.Should().BeFalse();
    }

    private sealed class PassiveCaption : ISpeechCaptionPresentation
    {
        public int ReaderTextScalePercent => 100;
        public string? SpeechCaptionText => null;
        public string SpeechCaptionLabel => string.Empty;
        public string SpeechCaptionPinLabel => string.Empty;
        public AsyncCommand ToggleSpeechCaptionPinCommand { get; } = new(static () => Task.CompletedTask, static _ => { });
        public SpeechCaptionPlacement? SpeechCaptionPlacement => null;
        public bool IsSpeechCaptionVisible => false;
        public bool CanChooseSpeechCaptionDisplay => false;
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public event EventHandler? PrivacyClosureRequested { add { } remove { } }
        public void EnsureReaderTypographyAvailable() { }
        public void RefreshSpeechPlaybackFrame() { }
        public void ReportSpeechCaptionDisplayUnavailable(bool reportRecovery = true) { }
        public void ReportSpeechCaptionPresentationFailure(string exceptionType) { }
    }
}

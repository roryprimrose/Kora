using Kora.Core.Voice;

namespace Kora.NativeUxFixture;

internal sealed partial class NativeUxFixtureSession
{
    private Task? captionResponse;
    private long captionSnapshotRevision;

    internal async Task QueueSyntheticCaptionAsync()
    {
        RequireValidationAccess();
        var speech = SilentSpeech ?? throw new InvalidOperationException("Explicit silent-caption launch approval is required.");
        if (captionResponse is { IsCompleted: false }) { throw new InvalidOperationException("A synthetic caption response is already pending."); }
        var started = speech.Arm();
        Main.CommandText = "show your window";
        captionResponse = Main.RunTypedCommand.ExecuteAsync();
        var observed = await Task.WhenAny(started, captionResponse);
        if (observed == captionResponse)
        {
            await captionResponse;
            throw new InvalidOperationException("The ordinary response path did not admit synthetic caption playback. "
                + $"Output available: {Main.IsSpeechOutputAvailable}; response enabled: {Main.IsSpeechResponseEnabled}; "
                + $"mode: {Main.EffectiveResponseMode}; privacy held: {Main.IsPrivacyPresentationHeld}; "
                + $"speech status: {Main.VoiceAvailabilityMessage}; output status: {Main.OutputDeviceAvailabilityMessage}; "
                + $"response: {Main.ResponseTitle} - {Main.ResponseBody}");
        }
        await started;
    }

    internal void ObserveSyntheticCaption()
    {
        RequireValidationAccess();
        (SilentSpeech ?? throw new InvalidOperationException("Silent-caption mode is not enabled.")).ObservePlayback();
        Main.RefreshSpeechPlaybackFrame();
    }

    internal async Task CompleteSyntheticCaptionAsync()
    {
        RequireValidationAccess();
        (SilentSpeech ?? throw new InvalidOperationException("Silent-caption mode is not enabled.")).Complete();
        await (captionResponse ?? throw new InvalidOperationException("No synthetic caption response is pending."));
    }

    internal async Task StopSyntheticCaptionAsync()
    {
        if (SilentSpeech is null) { return; }
        if (!Access.Open) { SilentSpeech.InvalidateOutput(); }
        else if (Main.StopSpeechCommand.CanExecute(null)) { await Main.StopSpeechCommand.ExecuteAsync(); }
        else
        {
            Main.CommandText = "stop speaking";
            await Main.RunTypedCommand.ExecuteAsync();
        }
        if (captionResponse is not null) { await captionResponse; }
    }

    internal async Task SetSyntheticCaptionPlacementAsync(SpeechCaptionPlacement placement)
    {
        RequireValidationAccess();
        if (SilentSpeech is null) { throw new InvalidOperationException("Silent-caption mode is not enabled."); }
        await Main.RefreshSpeechTextCommand.ExecuteAsync();
        Main.SelectedSpeechCaptionOption = SpeechCaptionOption.Placement;
        Main.SelectedSpeechCaptionChoice = Main.SpeechCaptionOptionChoices.Single(choice =>
            choice.CaptionValue == new SpeechCaptionValue.Placement(placement));
        await Main.SaveSpeechCaptionOptionCommand.ExecuteAsync();
        if (Main.SpeechCaptionPlacement != placement) { throw new InvalidOperationException("The exact synthetic caption placement was not saved."); }
    }

    internal string SyntheticCaptionStatus(string completedOperation) => System.Text.Json.JsonSerializer.Serialize(new
    {
        synthetic = true,
        completedOperation,
        snapshotRevision = checked(++captionSnapshotRevision),
        captionVisible = Main.IsSpeechCaptionVisible,
        presentationAvailable = Main.CanRevealPrivatePresentation,
        speechResponseAvailable = Main.IsSpeechResponseEnabled,
        captionText = Main.SpeechCaptionText,
        exactText = SilentSpeech?.ExactText,
        playbackId = SilentSpeech?.PlaybackId,
        admittedRequests = SilentSpeech?.AdmittedRequests,
        pinned = Main.IsSpeechCaptionPinned,
        previous = Main.IsPreviousSpeechCaption,
        placement = Main.SpeechCaptionPlacement?.ToString(),
        realAudioOperations = 0,
    });
}

namespace Kora.Core.Voice;

public static class SpeechInputProfileCatalogue
{
    public const string WindowsProfileId = "windows-activated-v1";
    public const string NativeWakeProfileId = "pocketsphinx-5-1-1-en-us-cpu-v1";
    public static SpeechInputSelection NativeWakeSelection { get; } =
        new(NativeWakeProfileId, SpeechInputResource.Cpu, SpeechInputResidency.CaptureLifetime);
    public static SpeechInputProfile NativeWakeProfile { get; } =
        new(NativeWakeProfileId, "Local name wake and Windows activated commands", "PocketSphinx 5.1.1 / System.Speech", "English");
    public const string LocalProfilesUnavailable = "Additional exact local wake/activated-ASR/resource profiles are unavailable: "
        + "no complete compatible profile is admitted for selection. Optional hands-free asset preparation does not qualify capture, names or acoustic behavior.";
    public static IReadOnlyList<SpeechInputProfile> Profiles { get; } = Array.AsReadOnly<SpeechInputProfile>(
        [new(WindowsProfileId, "Windows activated commands", "System.Speech", "Installed English recognizer")]);

    public static string? UnavailableReason(SpeechInputSelection selection)
    {
        selection.Validate();
        if (selection.ProfileId is NativeWakeProfileId)
        {
            return "The exact local wake/name/dictionary/assets/backend/playback profile is unavailable. No default-name or Windows fallback was selected.";
        }
        return selection.Resource != SpeechInputResource.WindowsManaged || selection.Residency != SpeechInputResidency.CaptureLifetime
            ? "This resource/residency tuple is not qualified for the Windows activated adapter. No other engine is selected. Choose WindowsManaged / CaptureLifetime explicitly, or use typed/native input."
            : null;
    }
}

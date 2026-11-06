namespace Kora.Core.Voice;

public sealed record SpeechInputSelection(string ProfileId, SpeechInputResource Resource, SpeechInputResidency Residency)
{
    public static SpeechInputSelection Default { get; } =
        new(SpeechInputProfileCatalogue.WindowsProfileId, SpeechInputResource.WindowsManaged, SpeechInputResidency.CaptureLifetime);

    public void Validate()
    {
        if (ProfileId is not (SpeechInputProfileCatalogue.WindowsProfileId or SpeechInputProfileCatalogue.NativeWakeProfileId)
            || !Enum.IsDefined(Resource) || !Enum.IsDefined(Residency))
        {
            throw new ArgumentOutOfRangeException(null, "Choose an exact known speech-input profile, resource and residency tuple.");
        }
    }
}

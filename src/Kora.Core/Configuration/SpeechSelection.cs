using Kora.Core.Voice;

namespace Kora.Core.Configuration;

public sealed record SpeechSelection(string ProviderId, string? VoiceId)
{
    public static SpeechSelection Default { get; } = new(SpeechProviderIds.Windows, null);
    public const int MaximumVoiceIdLength = 256;

    public void Validate()
    {
        if (ProviderId is not (SpeechProviderIds.Windows or SpeechProviderIds.Kokoro))
        {
            throw new ArgumentOutOfRangeException(null, "Choose a delivered speech provider.");
        }
        if (VoiceId is not null && (string.IsNullOrWhiteSpace(VoiceId)
            || VoiceId.Length > MaximumVoiceIdLength || !string.Equals(VoiceId, VoiceId.Trim(), StringComparison.Ordinal)
            || VoiceId.Any(char.IsControl)))
        {
            throw new ArgumentOutOfRangeException(null, "Choose an exact installed voice ID of at most 256 characters.");
        }
    }
}

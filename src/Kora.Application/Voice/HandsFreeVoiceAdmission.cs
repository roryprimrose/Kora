using Kora.Core.Voice;

namespace Kora.Application.Voice;

/// <summary>Fresh host policy observation, not consent or identity supplied by an audio engine.</summary>
public sealed record HandsFreeVoiceAdmission(
    MicrophoneDevice Microphone, string AssistantName, SpeechInputGeneration Profile,
    long RecoveryRevision, long TopologyRevision, long CallRevision, long OwnerRevision,
    long OwnershipBindingRevision = 0)
{
    public long ComposerRevision { get; init; }
    public long ConversationRevision { get; init; }

    internal bool HasSameListenerAuthority(HandsFreeVoiceAdmission? other) =>
        other is not null && this == (other with { ComposerRevision = ComposerRevision });
}

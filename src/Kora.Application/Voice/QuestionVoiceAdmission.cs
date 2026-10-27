using Kora.Core.Configuration;
using Kora.Core.Voice;

namespace Kora.Application.Voice;

public sealed record QuestionVoiceAdmission(
    MicrophoneDevice Microphone, long RecoveryRevision, long TopologyRevision, long CallRevision)
{
    private string? activationName;

    public string? ActivationName
    {
        get => activationName;
        init => activationName = value is null ? null : AssistantNameRules.Normalize(value);
    }
}

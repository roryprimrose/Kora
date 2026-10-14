using Kora.Core.Voice;

namespace Kora.Application.Voice;

public sealed record QuestionVoiceAdmission(
    MicrophoneDevice Microphone, long RecoveryRevision, long TopologyRevision, long CallRevision);

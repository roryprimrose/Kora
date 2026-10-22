using Kora.Core.Voice;

namespace Kora.Application.Voice;

public sealed record ComposerDictationAdmission(
    MicrophoneDevice Microphone, long RecoveryRevision, long TopologyRevision,
    long CallRevision, long OwnershipRevision, long InputRevision);

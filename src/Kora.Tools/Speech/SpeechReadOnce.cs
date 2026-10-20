using Kora.Core.Voice;
using Kora.Core.Auditing;

namespace Kora.Tools.Speech;

public sealed class SpeechReadOnce(ISpeechReadOncePlayback playback)
{
    public const string AuditActionId = "speech.read-once";
    public const string ModelLaneUnavailableReason = "current-response-model-tool-loop-not-qualified";

    public Task ExecuteAsync(ResponseSpeechTarget target, SecurityAuditInitiator initiator, CancellationToken cancellationToken) =>
        playback.ReadOnceAsync(target, initiator, cancellationToken);
}

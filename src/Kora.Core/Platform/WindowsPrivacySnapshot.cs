using Kora.Core.Voice;

namespace Kora.Core.Platform;

public sealed record WindowsPrivacySnapshot(
    WindowsSessionState SessionState,
    MicrophoneAccessState MicrophoneAccess,
    long TopologyRevision,
    IReadOnlyList<string> ActiveMicrophoneIds,
    string? DefaultMicrophoneId,
    string? DefaultSpeakerId)
{
    public bool CanCapture =>
        SessionState == WindowsSessionState.Unlocked &&
        MicrophoneAccess == MicrophoneAccessState.Allowed;

    public bool CanCaptureFrom(MicrophoneDevice microphone)
    {
        ArgumentNullException.ThrowIfNull(microphone);
        var endpointId = microphone.IsSystemDefault ? DefaultMicrophoneId : microphone.Id;
        return CanCapture && endpointId is not null &&
            ActiveMicrophoneIds.Contains(endpointId, StringComparer.Ordinal);
    }
}

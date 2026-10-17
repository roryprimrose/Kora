using Kora.Core.Platform;

namespace Kora.Windows.Session;

internal sealed class WindowsPrivacySignalEventArgs(
    WindowsSessionState? sessionState = null,
    bool topologyChanged = false,
    WindowsPrivacyChangeReason reason = WindowsPrivacyChangeReason.Unknown,
    PrivacyObservation? observation = null,
    string? topologyIdentity = null) : EventArgs
{
    public WindowsSessionState? SessionState { get; } = sessionState;

    public bool TopologyChanged { get; } = topologyChanged;

    public WindowsPrivacyChangeReason Reason { get; } = reason;

    public PrivacyObservation Observation { get; } = observation ?? PrivacyObservation.Create();

    public string? TopologyIdentity { get; } = topologyIdentity;
}

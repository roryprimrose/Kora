namespace Kora.Core.Communication;

/// <summary>Explicit device-local selection of a supporting observation source.</summary>
public enum CommunicationActivitySelection
{
    /// <summary>No automatic observation promise.</summary>
    None,
    /// <summary>Windows communications-stream notifications, not actual-call identification.</summary>
    WindowsCommunicationsAudio,
}
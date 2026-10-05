namespace Kora.Core.Voice;

/// <summary>
/// Recording status only; closing input does not necessarily retire its final transcript generation.
/// Refresh the service's current status when applying queued presentation; this event never enables voice consent.
/// </summary>
public sealed class VoiceCaptureStateChangedEventArgs(long generation, bool isListening) : EventArgs
{
    public long Generation { get; } = generation;

    public bool IsListening { get; } = isListening;
}

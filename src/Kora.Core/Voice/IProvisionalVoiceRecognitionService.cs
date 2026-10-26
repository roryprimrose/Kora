namespace Kora.Core.Voice;

/// <summary>Optional observation of an already acknowledged activated capture; never input or execution authority.</summary>
public interface IProvisionalVoiceRecognitionService
{
    /// <summary>
    /// Observes snapshots for the exact host capture generation until disposed.
    /// Returns null when observation is unsupported or that capture is no longer eligible.
    /// Callbacks must not block recognition; disposal fences callbacks already in flight at the consumer.
    /// </summary>
    IDisposable? ObserveProvisionalTranscripts(long generation, Action<ProvisionalTranscriptSnapshot> observer);
}

using Kora.Core.Voice;

namespace Kora.Application.Voice;

public sealed partial class ComposerDictationService
{
    private bool provisionalUpdatePosted;

    public string ProvisionalTextFor(ComposerDictationTarget target)
    {
        lock (gate)
        {
            return turn is { } current && ReferenceEquals(current.Target, target)
                && Eligible(current) && !current.Releasing && current.Literal is null
                && current.Generation == capture.CaptureGeneration
                    ? current.ProvisionalText : string.Empty;
        }
    }

    private void ObserveProvisional(Turn current)
    {
        if (current.Target.Destination != ComposerDictationDestination.SessionComposer) { return; }
        var observation = (capture as IProvisionalVoiceRecognitionService)?
            .ObserveProvisionalTranscripts(current.Generation, snapshot => Provisional(current, snapshot));
        lock (gate)
        {
            if (Eligible(current) && current.Literal is null)
            {
                current.ProvisionalObservation = observation;
                if (observation is null)
                { status = "Capturing final-only literal text. Provisional feedback unavailable; release Dictate to finish."; }
                return;
            }
        }
        observation?.Dispose();
        Revalidate();
    }

    private void Provisional(Turn current, ProvisionalTranscriptSnapshot snapshot)
    {
        lock (gate)
        {
            if (!Eligible(current) || current.Releasing || current.Literal is not null || current.ProvisionalRetired
                || snapshot.Generation != current.Generation || snapshot.Generation != capture.CaptureGeneration
                || snapshot.Sequence <= current.ProvisionalSequence)
            { return; }
            current.ProvisionalSequence = snapshot.Sequence;
            if (!snapshot.IsValid())
            {
                ClearProvisional(current);
                status = "Provisional feedback retired: invalid or oversized recognition snapshot. Final-only dictation remains available.";
            }
            else { current.PendingProvisional = snapshot.Text; }
            QueueProvisionalUpdate();
        }
    }

    private void QueueProvisionalUpdate()
    {
        if (provisionalUpdatePosted) { return; }
        provisionalUpdatePosted = true;
        // One queued callback contains no text/target; newer snapshots replace one bounded pending value.
        dispatcher.Post(FlushProvisional);
    }

    private void FlushProvisional()
    {
        lock (gate)
        {
            provisionalUpdatePosted = false;
            if (turn is { } current && Eligible(current) && !current.Releasing && current.Literal is null
                && !current.ProvisionalRetired && current.PendingProvisional is { } text)
            {
                current.ProvisionalText = text;
                current.PendingProvisional = null;
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void ClearProvisional(Turn current)
    {
        current.ProvisionalRetired = true;
        current.ProvisionalText = string.Empty;
        current.PendingProvisional = null;
    }

    private void ReleaseProvisional(Turn current)
    {
        IDisposable? observation;
        lock (gate)
        {
            observation = current.ProvisionalObservation;
            current.ProvisionalObservation = null;
        }
        observation?.Dispose();
    }
}

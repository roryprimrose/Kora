using Kora.Core.Voice;

namespace Kora.Windows.Audio;

public sealed partial class WindowsVoiceRecognitionService : IProvisionalVoiceRecognitionService
{
    private ProvisionalObservation? provisionalObservation;

    public IDisposable? ObserveProvisionalTranscripts(long generation, Action<ProvisionalTranscriptSnapshot> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (gate)
        {
            if (disposed || replyCapture || !captureAdmitted || !acceptingAudio || transcriptDelivered
                || !transcriptPublicationReady || this.generation != generation
                || capture is not IActivatedHypothesisSource source)
            { return null; }
            if (provisionalObservation is not null)
            { throw new InvalidOperationException("This capture already has a provisional observer."); }
            provisionalObservation = new(this, source, generation, observer);
            return provisionalObservation;
        }
    }

    private void RetireProvisionalObservation() => provisionalObservation?.Dispose();

    private sealed class ProvisionalObservation : IDisposable
    {
        private readonly WindowsVoiceRecognitionService owner;
        private readonly IActivatedHypothesisSource source;
        private readonly long generation;
        private readonly Action<ProvisionalTranscriptSnapshot> observer;
        private int closed;

        public ProvisionalObservation(WindowsVoiceRecognitionService owner, IActivatedHypothesisSource source,
            long generation, Action<ProvisionalTranscriptSnapshot> observer)
        {
            this.owner = owner;
            this.source = source;
            this.generation = generation;
            this.observer = observer;
            source.Hypothesized += Observe;
        }

        private void Observe(object? sender, ActivatedHypothesisEventArgs args)
        {
            lock (owner.gate)
            {
                if (Volatile.Read(ref closed) != 0 || !ReferenceEquals(sender, source)
                    || !ReferenceEquals(owner.capture, source) || owner.generation != generation
                    || !owner.captureAdmitted || !owner.acceptingAudio || owner.transcriptDelivered
                    || owner.selectedMicrophone is not { } microphone
                    || !owner.privacy!.Current.CanCaptureFrom(microphone))
                { return; }
            }
            // The consumer revalidates its exact target and generation; never call it under the native gate.
            try { observer(new(generation, args.Sequence, args.Text)); }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                ProvisionalObserverFailed(owner.logger, exception.GetType().Name);
                Dispose();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref closed, 1) != 0) { return; }
            source.Hypothesized -= Observe;
            lock (owner.gate)
            {
                if (ReferenceEquals(owner.provisionalObservation, this)) { owner.provisionalObservation = null; }
            }
        }
    }
}

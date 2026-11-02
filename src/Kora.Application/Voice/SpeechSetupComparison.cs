using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.Voice;

/// <summary>Bounded memory-only setup operation. It has no command, source, preference or conversation writer.</summary>
public sealed class SpeechSetupComparison(ISpeechSetupComparisonRuntime runtime, ISpeechInputProfileRuntime profiles)
    : IAsyncDisposable
{
    private readonly Lock gate = new();
    private CancellationTokenSource? cancellation;
    private Task work = Task.CompletedTask;
    private Func<bool>? eligible;
    private MicrophoneDevice? microphone;
    private SpeechInputGeneration? generation;
    private SpeechComparisonProfile[] reviewed = [];
    private SpeechComparisonResult[] results = [];
    private string expectedPhrase = "The quiet garden has seven blue flowers";
    private string status = "Start a local setup comparison. No microphone is opened by review.";
    private bool ready;
    private bool running;
    private bool disposed;
    private long revision;

    public event EventHandler? Changed;
    public const string Disclosure = "Explicit local microphone use: two captures per reviewed profile, at most two profiles, "
        + "ten seconds per capture. Say the same inert phrase each time. Audio is discarded after each joined attempt. "
        + "Text is visible only in this memory-only comparison and cleared on privacy change or close. "
        + "Nothing is executed, saved, learned or sent to a model. Listening consent, listening state and profile/settings are unchanged. "
        + "First/repeat are not proof of cold/warm caches. Unsupported end-of-speech, cold/warm and resource measurements are unknown. "
        + "This is setup support, not release qualification.";

    public string ExpectedPhrase
    {
        get { lock (gate) { return expectedPhrase; } }
        set
        {
            lock (gate)
            {
                if (string.Equals(expectedPhrase, value, StringComparison.Ordinal)) { return; }
                expectedPhrase = value;
                Retire("Phrase changed. Review again before testing.");
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
    public string Status { get { lock (gate) { return status; } } }
    public string ReviewDescription
    {
        get
        {
            lock (gate)
            {
                return reviewed.Length == 0 ? "" : "Microphone: " + microphone!.Name + Environment.NewLine
                    + string.Join(Environment.NewLine, reviewed.Select(item => item.Name));
            }
        }
    }
    public IReadOnlyList<SpeechComparisonResult> Results { get { lock (gate) { return Array.AsReadOnly(results); } } }
    public bool IsRunning { get { lock (gate) { return running; } } }
    public bool CanTest { get { lock (gate) { return ready && !disposed && work.IsCompleted && IsCurrent(); } } }

    public void Review(MicrophoneDevice device, Func<bool> admission)
    {
        var origin = HostActivity.RequireCurrent().Request.Origin;
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        { throw new InvalidOperationException("Comparison requires original local user input."); }
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!work.IsCompleted) { throw new InvalidOperationException("Cancel and join the previous attempt before review."); }
            Retire("Review unavailable.");
            if (!admission() || !profiles.IsProfileAvailable)
            { throw new InvalidOperationException("Current microphone/owner/consent/privacy or profile admission is unavailable."); }
            if (string.IsNullOrWhiteSpace(expectedPhrase) || expectedPhrase.Length > SpeechComparisonObservation.MaximumFinalTextLength
                || expectedPhrase.Any(char.IsControl))
            { throw new InvalidOperationException("Use one visible representative phrase of 1-256 characters."); }
            var offered = runtime.GetComparisonProfiles().ToArray();
            if (offered.Length > SpeechComparisonObservation.MaximumProfiles
                || offered.DistinctBy(item => item.Selection).Count() != offered.Length
                || offered.Any(item => SpeechInputProfileCatalogue.UnavailableReason(item.Selection) is not null))
            { throw new InvalidOperationException("The installed comparison capability set is incompatible or exceeds the bounded limit."); }
            reviewed = offered;
            eligible = admission;
            microphone = device;
            generation = profiles.ProfileGeneration;
            ready = offered.Length != 0;
            status = ready ? "Review the phrase, exact profiles and microphone disclosure, then explicitly Test."
                : "Unavailable: no exact compatible installed profile is qualified for inert setup comparison. No download, fallback or microphone capture.";
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task TestAsync(bool microphoneUseConfirmed)
    {
        var origin = HostActivity.RequireCurrent().Request.Origin;
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        { throw new InvalidOperationException("Testing requires fresh original local user input."); }
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!microphoneUseConfirmed || !ready || !work.IsCompleted || !IsCurrent())
            { throw new InvalidOperationException("Fresh exact review and explicit microphone-use confirmation are required."); }
            ready = false;
            running = true;
            cancellation?.Dispose();
            cancellation = new();
            results = [];
            var attemptRevision = revision;
            var phrase = expectedPhrase;
            var selections = reviewed.ToArray();
            var device = microphone!;
            var profileGeneration = generation!;
            var token = cancellation.Token;
            var begin = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Runtime);
            status = "Opening inert setup capture. Wait for Speak now. Cancel retains ownership until native cleanup joins.";
            work = Task.Run(() => RunAsync(attemptRevision, phrase, selections, device, profileGeneration, begin, token), CancellationToken.None);
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return work;
    }

    private async Task RunAsync(long attemptRevision, string phrase, SpeechComparisonProfile[] selections,
        MicrophoneDevice device, SpeechInputGeneration profileGeneration, Func<HostActivity> begin, CancellationToken token)
    {
        using var activity = begin();
        try
        {
            bool Current() { lock (gate) { return revision == attemptRevision && IsCurrent(); } }
            await using (var lease = await runtime.AcquireComparisonAsync(profileGeneration, device, Current, token).ConfigureAwait(false))
            {
                foreach (var selection in selections)
                {
                    for (var trial = 1; trial <= 2; trial++)
                    {
                        token.ThrowIfCancellationRequested();
                        if (!Current()) { throw new OperationCanceledException(token); }
                        var label = $"{selection.Name}, {(trial == 1 ? "first" : "repeat")} capture";
                        var activeCapture = 1;
                        void RecordingStarted()
                        {
                            lock (gate)
                            {
                                if (Volatile.Read(ref activeCapture) == 0 || !Current()) { return; }
                                status = "Speak now - say the reviewed phrase: " + label + ". At most ten seconds.";
                            }
                            Changed?.Invoke(this, EventArgs.Empty);
                        }
                        SpeechComparisonObservation observation;
                        lock (gate) { status = "Opening " + label + ". Wait for Speak now."; }
                        Changed?.Invoke(this, EventArgs.Empty);
                        try { observation = await lease.CaptureAsync(selection, RecordingStarted, token).ConfigureAwait(false); }
                        finally { Interlocked.Exchange(ref activeCapture, 0); }
                        if (observation.FinalText.Length > SpeechComparisonObservation.MaximumFinalTextLength || observation.OpenToStart < TimeSpan.Zero)
                        { throw new InvalidOperationException("Comparison evidence exceeded the supported limits."); }
                        lock (gate)
                        {
                            if (!Current()) { throw new OperationCanceledException(token); }
                            results = [.. results, new(selection.Name, trial, phrase, observation)];
                            status = $"Completed {results.Length} of {selections.Length * 2} captures; native resources joined for this attempt.";
                        }
                        Changed?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
            lock (gate)
            {
                if (!Current()) { throw new OperationCanceledException(token); }
                status = "Comparison complete; all capture resources joined and raw audio discarded. Prior listening/settings/profile selection unchanged.";
            }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException)
        {
            lock (gate) { results = []; status = "Comparison cancelled or admission changed; native attempts joined. Review again to retry."; }
            activity.Complete(HostOperationOutcome.Cancelled);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            lock (gate) { results = []; status = "Comparison failed or cleanup is unconfirmed. No success is claimed. Failure type: " + exception.GetType().Name + ". Review current input before retry."; }
            activity.Complete(HostOperationOutcome.Failed);
        }
        finally
        {
            lock (gate) { running = false; }
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool IsCurrent() => eligible!() && generation == profiles.ProfileGeneration
        && profiles.IsProfileAvailable && reviewed.SequenceEqual(runtime.GetComparisonProfiles());

    public void Revalidate()
    {
        lock (gate)
        {
            if (eligible is null || IsCurrent()) { return; }
            Retire("Comparison admission changed. Visible text cleared; cancel/join before fresh review.");
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task CancelAsync()
    {
        Task joining;
        lock (gate)
        {
            Retire("Cancellation requested. Waiting for native capture and raw-buffer cleanup to join.");
            joining = work;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return joining;
    }

    public void Clear()
    {
        lock (gate)
        {
            Retire("Comparison closed or private input revoked. Visible text cleared.");
            expectedPhrase = "The quiet garden has seven blue flowers";
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Retire(string message)
    {
        revision++;
        ready = false;
        cancellation?.Cancel();
        results = [];
        reviewed = [];
        eligible = null;
        status = message;
    }

    public async ValueTask DisposeAsync()
    {
        Task joining;
        lock (gate)
        {
            disposed = true;
            Retire("Comparison disposed; cancellation requested and visible text cleared.");
            joining = work;
        }
        Changed?.Invoke(this, EventArgs.Empty);
#pragma warning disable VSTHRD003 // Joins only the exact owned bounded operation; native teardown is independent of UI delivery.
        try { await joining.ConfigureAwait(false); }
#pragma warning restore VSTHRD003
        finally { lock (gate) { cancellation?.Dispose(); cancellation = null; } }
    }
}

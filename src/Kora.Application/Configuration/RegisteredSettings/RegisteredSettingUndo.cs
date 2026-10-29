using System.Diagnostics;

using Kora.Application.Communication;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration.RegisteredSettings;

/// <summary>One run-only, last-confirmed registered preference. No history, redo or effect rollback.</summary>
public sealed class RegisteredSettingUndo : IDisposable
{
    private readonly AppearanceConfigurationService appearance;
    private readonly SpeechConfigurationService speech;
    private readonly PlaybackVolumeConfigurationService? volume;
    private readonly ResponseModeConfigurationService? response;
    private readonly CallCommunicationPolicy policy;
    private readonly TimeProvider time;
    private readonly AsyncLocal<bool> restoring = new();
    private RegisteredSettingChange? change;
    private Review? review;
    private int busy;
    private int generation;
    private bool disposed;

    public RegisteredSettingUndo(AppearanceConfigurationService appearance, SpeechConfigurationService speech,
        PlaybackVolumeConfigurationService? volume, ResponseModeConfigurationService? response,
        CallCommunicationPolicy policy, TimeProvider time)
    {
        this.appearance = appearance;
        this.speech = speech;
        this.volume = volume;
        this.response = response;
        this.policy = policy;
        this.time = time;
        appearance.UndoChanged += Record;
        speech.UndoChanged += Record;
        if (volume is not null) { volume.UndoChanged += Record; }
        if (response is not null) { response.UndoChanged += Record; }
    }

    public event EventHandler? Changed;
    public bool CanReview => !disposed && Volatile.Read(ref busy) == 0 && Volatile.Read(ref change) is not null;
    public string Description { get; private set; } =
        "No confirmed registered setting change is available in this run. No durable undo history is kept.";
    public bool CanConfirm => Volatile.Read(ref review) is { } current && Current(current);

    private sealed record Review(RegisteredSettingChange Change, HostRequest Request, RequestOrigin Origin,
        SecurityAuditInitiator Initiator, long CallRevision, DateTimeOffset Expires, int Generation,
        Func<bool> Eligible, ActivityContext Cause);

    private void Record(RegisteredSettingChange? value)
    {
        // A restore is a new write, but must never become its own redo receipt.
        if (disposed || restoring.Value) { return; }
        Volatile.Write(ref change, value);
        Interlocked.Increment(ref generation);
        Volatile.Write(ref review, null);
        Description = value is null
            ? "Undo held: the latest settings write is pending or unconfirmed. No retry or rollback is inferred."
            : $"One last confirmed change in this run: {value.Descriptor.Id}. Review revalidates the current saved preference.";
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        Volatile.Write(ref change, null);
        Volatile.Write(ref review, null);
        Interlocked.Increment(ref generation);
        Description = "No undo receipt is retained after privacy/lifecycle closure or restart.";
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Cancel()
    {
        if (Volatile.Read(ref busy) != 0) { throw new InvalidOperationException("Undo is committing; cancellation cannot promise rollback."); }
        Volatile.Write(ref review, null);
        Description = "Undo review cancelled. No write requested; a still-current last change can be reviewed again.";
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ReviewLast(SecurityAuditInitiator initiator, Func<bool> eligible)
    {
        Cancel();
        var host = RequireInput(initiator);
        var candidate = Volatile.Read(ref change);
        var revision = Volatile.Read(ref generation);
        if (disposed || candidate is null || !eligible() || !candidate.Matches())
        { throw new InvalidOperationException("No compatible, unchanged last confirmed setting is available in this run."); }
        if (candidate.Descriptor.RequiresNativeReview && initiator != SecurityAuditInitiator.LocalUser)
        { throw new InvalidOperationException("This appearance preference requires a new original native Settings undo review and confirmation."); }
        var call = policy.Current.Revision;
        if (policy.CheckMutation(host.Request.Origin, call, eligible) is not null)
        { throw new InvalidOperationException("The original undo channel or call policy denies this review. Start a new eligible request."); }
        if (!ReferenceEquals(candidate, Volatile.Read(ref change)))
        { throw new InvalidOperationException("The last settings change was replaced during review."); }
        var next = new Review(candidate, host.Request, host.Request.Origin, initiator, call,
            time.GetUtcNow().AddSeconds(60), revision, eligible, host.Activity!.Context);
        Volatile.Write(ref review, next);
        Description = $"Undo {candidate.Descriptor.Id}: current {candidate.After}; restore {candidate.Before}. "
            + $"Scope: {candidate.Descriptor.Scope}. One change, this run only; review expires in 60 seconds. "
            + "Confirm separately or Cancel. This is a new audited preference write, not deletion of audit/history. "
            + "Only future eligible presentation/ordinary responses change. No speech replay, model request, "
            + "effect reversal, grants, permissions, listening or microphone consent is restored. "
            + "Mandatory full visual warnings, questions, approvals, security, speech-unavailable fallback "
            + "and privacy/call restrictions still apply, including VoiceOnly.";
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> ConfirmAsync(SecurityAuditInitiator initiator, CancellationToken token)
    {
        var host = RequireInput(initiator);
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
        { throw new InvalidOperationException("An undo write is already pending."); }
        try { return await ConfirmCoreAsync(host, initiator, token).ConfigureAwait(false); }
        finally
        {
            Interlocked.Exchange(ref busy, 0);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<bool> ConfirmCoreAsync(HostActivity host, SecurityAuditInitiator initiator, CancellationToken token)
    {
        var candidate = Volatile.Read(ref review);
        if (candidate is null || host.Request.RequestId == candidate.Request.RequestId
            || candidate.Origin == RequestOrigin.ActivatedVoice && host.Request.Origin != RequestOrigin.ActivatedVoice
            || candidate.Change.Descriptor.RequiresNativeReview && initiator != SecurityAuditInitiator.LocalUser
            || !Current(candidate))
        { throw new InvalidOperationException("Undo needs a separate fresh confirmation of the exact current review and original channel."); }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Policy);
        activity.Activity!.AddLink(new(candidate.Cause));
        try
        {
            token.ThrowIfCancellationRequested();
            if (!Current(candidate) || !candidate.Change.Matches())
            { throw new InvalidOperationException("The reviewed settings state changed; nothing was applied."); }
            Volatile.Write(ref review, null);
            Volatile.Write(ref change, null);
            Description = "Undo write pending or unconfirmed. No automatic retry or rollback.";
            bool Eligible() => !disposed && candidate.Generation == Volatile.Read(ref generation)
                && candidate.Eligible() && time.GetUtcNow() < candidate.Expires
                && !token.IsCancellationRequested && policy.Current.Revision == candidate.CallRevision;
            restoring.Value = true;
            var origin = candidate.Origin == RequestOrigin.ActivatedVoice ? candidate.Origin : host.Request.Origin;
            var accepted = await candidate.Change.Restore(origin, initiator, policy,
                candidate.CallRevision, Eligible, token).ConfigureAwait(false);
            Description = accepted
                ? "Last setting undone by a new audited write. Undo consumed; no redo. Future eligible output only."
                : "Undo is unconfirmed; a write may have persisted. Inspect saved state and audit. No automatic retry or rollback.";
            activity.Complete(accepted ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            return accepted;
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
        finally
        {
            Volatile.Write(ref review, null);
            restoring.Value = false;
        }
    }

    private bool Current(Review value) => !disposed && value.Generation == Volatile.Read(ref generation)
        && ReferenceEquals(value.Change, Volatile.Read(ref change)) && value.Eligible()
        && time.GetUtcNow() < value.Expires && policy.Current.Revision == value.CallRevision;

    private static HostActivity RequireInput(SecurityAuditInitiator initiator)
    {
        var host = HostActivity.RequireCurrent();
        if (host.Activity!.IsStopped || host.Outcome != HostOperationOutcome.Unknown || host.Request.InvocationId is not null
            || host.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || (initiator == SecurityAuditInitiator.VoiceCommand) != (host.Request.Origin == RequestOrigin.ActivatedVoice))
        { throw new InvalidOperationException("Undo requires fresh original-user input, never model or tool authority."); }
        return host;
    }

    public void Dispose()
    {
        disposed = true;
        Clear();
        appearance.UndoChanged -= Record;
        speech.UndoChanged -= Record;
        if (volume is not null) { volume.UndoChanged -= Record; }
        if (response is not null) { response.UndoChanged -= Record; }
    }
}

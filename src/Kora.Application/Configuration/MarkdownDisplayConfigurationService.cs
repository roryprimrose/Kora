using Kora.Application.Communication;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed partial class MarkdownDisplayConfigurationService(
    IMarkdownDisplayPreferences preferences, AudioControlAdmission admission, ISecurityAuditLog auditLog,
    TimeProvider time, ILogger<MarkdownDisplayConfigurationService> logger) : IAsyncDisposable
{
    public sealed class CaptureUnavailableException(Exception innerException) : InvalidOperationException(
        "Markdown display preference unavailable; the saved default was not applied. Inspect preference state and audit before explicit recovery.",
        innerException);

    public const string ScopeDescription = "Device-local default for NEW eligible finalized-answer and retained-assistant Markdown Details only. "
        + "Existing viewers and their Source/Rendered, Find and continuous-reading choices are unchanged. Embedded help, USER/history/context readers, "
        + "raw file previews, plain text, HTML, Mermaid and browser renderers are unchanged.";
    private readonly Lock gate = new();
    private readonly SemaphoreSlim serial = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private MarkdownDisplayMode? saved;
    private MarkdownDisplayReview? review;
    private bool available;
    private bool busy;
    private volatile bool closed;
    private long revision;
    private Task? disposal;
    public event EventHandler? Changed;

    public string Description
    {
        get
        {
            lock (gate)
            {
                return available && !busy && !closed
                    ? $"Markdown display: {saved ?? MarkdownDisplayPreference.Default} ({(saved is null ? "unsaved default" : "saved")}). " + ScopeDescription
                    : "Markdown display unavailable or pending; the saved default is not applied. Inspect saved state and audit before explicit recovery. " + ScopeDescription;
            }
        }
    }

    public void Cancel() { lock (gate) { review = null; } }

    private MarkdownDisplayMode? Read(bool readBack = false)
    {
        var value = readBack ? preferences.ReadBack() : preferences.Load();
        if (value is { } mode) { _ = MarkdownDisplayPreference.Format(mode); }
        return value;
    }

    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken token)
    {
        Task entered;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (busy) { throw new InvalidOperationException("Markdown display control or preference capture is already active."); }
            busy = true;
            entered = serial.WaitAsync(CancellationToken.None);
        }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        await entered.ConfigureAwait(false);
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            var result = await Task.Run(() => operation(cancellation.Token), CancellationToken.None).ConfigureAwait(false);
            if (closed) { throw new OperationCanceledException(lifetime.Token); }
            cancellation.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception exception)
        {
            HoldUnavailable();
            PreferenceUnavailable(exception);
            throw;
        }
        finally
        {
            lock (gate) { busy = false; }
            serial.Release();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public Task ObserveAsync(CancellationToken token) => RunAsync(async cancellation =>
    {
        var value = Read();
        cancellation.ThrowIfCancellationRequested();
        lock (gate) { Publish(value); }
        await Task.CompletedTask.ConfigureAwait(false);
        return true;
    }, token);

    public Task InspectAsync(RequestOrigin origin, Func<bool> eligible, CancellationToken token) => RunAsync(async cancellation =>
    {
        var value = await admission.RunAsync(origin, eligible, (_, _) => Read(), cancellation).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        if (!eligible()) { throw new InvalidOperationException("Markdown preference inspection lost original admission."); }
        lock (gate) { Publish(value); }
        return true;
    }, token);

    public Task<MarkdownDisplayMode> CaptureAsync(Func<bool> eligible, CancellationToken token) => RunAsync(cancellation =>
    {
        MarkdownDisplayMode? expected;
        lock (gate)
        {
            if (!available) { throw new InvalidOperationException("Markdown display is unconfirmed; no saved default was applied."); }
            expected = saved;
        }
        if (!eligible()) { throw new InvalidOperationException("Markdown viewer source admission ended before preference capture."); }
        var value = Read();
        cancellation.ThrowIfCancellationRequested();
        if (value != expected || !eligible())
        { throw new InvalidOperationException("Markdown preference or viewer admission changed; inspect before opening a new viewer."); }
        return Task.FromResult(value ?? MarkdownDisplayPreference.Default);
    }, token);

    public Task<MarkdownDisplayReview> ReviewAsync(MarkdownDisplayMode value, RequestOrigin origin, long callRevision,
        CallCommunicationPolicy policy, Func<bool> eligible, CancellationToken token)
    {
        _ = MarkdownDisplayPreference.Format(value);
        return RunAsync(async cancellation =>
        {
            var candidate = await admission.RunAsync(origin, eligible, (request, session) =>
            {
                if (policy.CheckMutation(request.Origin, callRevision, eligible) is not null)
                { throw new InvalidOperationException("Markdown preference review denied by original call/channel policy."); }
                lock (gate)
                {
                    Publish(Read());
                    return new MarkdownDisplayReview(value, saved, revision, session, request.Origin, eligible,
                        callRevision, time.GetUtcNow().AddSeconds(60));
                }
            }, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            lock (gate)
            {
                if (!eligible() || revision != candidate.Revision)
                { throw new InvalidOperationException("Markdown preference review lost original admission or revision."); }
                review = candidate;
                return candidate;
            }
        }, token);
    }

    public Task<bool> ConfirmAsync(MarkdownDisplayReview candidate, RequestOrigin origin, SecurityAuditInitiator initiator,
        CallCommunicationPolicy policy, Func<bool> eligible, CancellationToken token)
    {
        if (initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || (origin == RequestOrigin.ActivatedVoice) != (initiator == SecurityAuditInitiator.VoiceCommand))
        { throw new InvalidOperationException("Markdown preference confirmation requires original user channel provenance."); }
        lock (gate)
        {
            if (busy || !available || !ReferenceEquals(review, candidate) || revision != candidate.Revision)
            { throw new InvalidOperationException("Review this exact Markdown preference change first."); }
            review = null;
        }
        return RunAsync(async cancellation =>
        {
            bool Current() => !closed && eligible() && candidate.Eligible() && time.GetUtcNow() < candidate.Expires
                && !cancellation.IsCancellationRequested;
            var accepted = await admission.RunAsync(origin, Current, (request, session) =>
            {
                lock (gate)
                {
                    if (session != candidate.Session || request.Origin != candidate.Origin || request.Origin != origin
                        || Read() != candidate.Saved || revision != candidate.Revision)
                    { throw new InvalidOperationException("Markdown preference source, session, channel or revision changed."); }
                    var audit = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                        "configuration.markdown-display", SecurityAuditOutcome.Requested, initiator, "preferences.device-local");
                    auditLog.Write(audit);
                    var denied = policy.CommitVoiceSetting(request.Origin, candidate.CallRevision,
                        () => Current() && ReferenceEquals(HostActivity.RequireCurrent().Request, request), () =>
                        {
                            if (Read() != candidate.Saved)
                            { throw new InvalidDataException("Markdown preference changed during requested audit."); }
                            HoldUnavailable();
                            try
                            {
                                preferences.BeginWrite();
                                preferences.Save(candidate.Value);
                                RequireReadback(candidate.Value);
                            }
                            catch
                            {
                                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed, "preference-write-unconfirmed"));
                                throw;
                            }
                            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
                        });
                    if (denied is not null)
                    { auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Denied, "call-channel-denied")); return false; }
                    return true;
                }
            }, cancellation).ConfigureAwait(false);
            if (accepted)
            {
                if (!Current()) { throw new InvalidOperationException("Markdown preference lost admission before its completed receipt."); }
                RequireReadback(candidate.Value);
                preferences.ConfirmWrite();
                try
                {
                    if (Read() != candidate.Value) { throw new InvalidDataException("Confirmed Markdown preference does not match."); }
                    if (!Current()) { throw new InvalidOperationException("Markdown preference lost admission during confirmation."); }
                }
                catch { preferences.BeginWrite(); throw; }
                lock (gate) { Publish(candidate.Value); }
            }
            return accepted;
        }, token);
    }

    private void RequireReadback(MarkdownDisplayMode value)
    {
        if (Read(readBack: true) != value) { throw new InvalidDataException("Markdown preference readback does not match."); }
    }

    public void HoldUnavailable()
    {
        lock (gate) { available = false; review = null; revision = checked(revision + 1); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Publish(MarkdownDisplayMode? value)
    {
        if (available && saved == value) { return; }
        saved = value; available = true; review = null; revision = checked(revision + 1);
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposal is not null) { return new(disposal); }
            closed = true;
            return new(disposal ??= JoinAsync(lifetime.CancelAsync()));
        }
    }

    private async Task JoinAsync(Task cancellation)
    {
#pragma warning disable VSTHRD003 // Join this owner's CancelAsync callbacks before disposing its lifetime.
        await cancellation.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        await serial.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        serial.Dispose();
        lifetime.Dispose();
    }
}

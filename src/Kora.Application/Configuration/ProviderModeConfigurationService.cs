using Kora.Application.Communication;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

public sealed class ProviderModeConfigurationService(
    IModelProviderModePreferences preferences, AudioControlAdmission admission, ISecurityAuditLog auditLog)
{
    private readonly Lock gate = new();
    private readonly Guid owner = Guid.NewGuid();
    private IReadOnlyList<ProviderModeChoice> choices = [];
    private ModelProviderMode? saved;
    private ModelProviderMode? desired;
    private long revision;
    private bool applying;
    private bool held = true;
    private string? recovery = "Inspect provider preferences in the current owning unlocked host.";

    public event EventHandler? Changed;
    public IReadOnlyList<ProviderModeChoice> Choices { get { lock (gate) { return choices; } } }

    public ProviderModeCommandResult Get(long callRevision = 0, string outcome = "observed")
    {
        lock (gate) { return new(outcome, recovery, revision, callRevision, saved, desired,
            held ? "unavailable" : saved is null ? "default" : "saved", !held); }
    }

    private ModelProviderMode? Read(bool readBack = false)
    {
        var value = readBack ? preferences.ReadBack() : preferences.Load();
        _ = ModelProviderModePreference.Serialize(value ?? ModelProviderModePreference.Default);
        return value;
    }

    public void Observe()
    {
        lock (gate)
        {
            if (applying) { throw new InvalidOperationException("Provider preference is committing."); }
            try
            {
                var value = Read();
                if (!held && saved == value) { return; }
                saved = value;
                desired = value ?? ModelProviderModePreference.Default;
                held = false;
                recovery = null;
                revision = checked(revision + 1);
                choices = [];
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch { HoldUnavailable(); throw; }
        }
    }

    // Holds only the short policy publication, not session I/O or provider dispatch.
    internal T WithInitialMode<T>(Func<ModelProviderMode, Func<bool>, T> publish)
    {
        lock (gate)
        {
            if (applying) { throw new InvalidOperationException("Provider preference is committing."); }
            try
            {
                Observe();
                var expected = revision;
                var value = saved;
                bool Current() => !held && !applying && revision == expected && Read() == value;
                return publish(desired!.Value, Current);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentOutOfRangeException)
            { HoldUnavailable(); throw; }
        }
    }

    public async Task RefreshAsync(RequestOrigin origin, Func<bool> eligible, CancellationToken cancellationToken)
    {
        var entered = false;
        var expectedRevision = 0L;
        try
        {
            await admission.RunAsync(origin, eligible, (request, authority) =>
            {
                lock (gate)
                {
                    if (applying) { throw new InvalidOperationException("Provider preference is committing."); }
                    applying = true;
                    entered = true;
                    saved = Read();
                    desired = saved ?? ModelProviderModePreference.Default;
                    held = true;
                    recovery = null;
                    revision = checked(revision + 1);
                    expectedRevision = revision;
                    choices = Array.AsReadOnly(ModelProviderModePreference.Choices.Select(mode =>
                        new ProviderModeChoice(mode, revision, owner, authority, request.Origin, eligible)).ToArray());
                    return true;
                }
            }, cancellationToken).ConfigureAwait(false);
            lock (gate)
            {
                if (revision != expectedRevision || !eligible() || Read() != saved)
                { throw new InvalidOperationException("Provider discovery admission or saved state changed before its receipt completed."); }
                held = false;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch { if (entered) { HoldUnavailable(); } throw; }
        finally { if (entered) { lock (gate) { applying = false; } } }
    }

    public async Task<bool> SelectAsync(ProviderModeChoice choice, long callRevision, RequestOrigin origin,
        SecurityAuditInitiator initiator, CallCommunicationPolicy policy, Func<bool> eligible, CancellationToken cancellationToken)
    {
        if (initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || initiator == SecurityAuditInitiator.VoiceCommand && origin != RequestOrigin.ActivatedVoice)
        { throw new InvalidOperationException("Provider-mode control requires original user initiation and channel."); }
        var entered = false;
        var expectedRevision = 0L;
        try
        {
            var accepted = await admission.RunAsync(origin, eligible, (request, authority) =>
            {
                lock (gate)
                {
                    if (held || applying || choice.Owner != owner || choice.Session != authority || choice.Revision != revision
                        || choice.Origin != request.Origin || !choice.RemainsAdmitted()
                        || !choices.Any(item => ReferenceEquals(item, choice)) || Read() != saved)
                    { throw new InvalidOperationException("Provider choice, saved state or original session/generation changed. Inspect and choose again."); }
                    applying = true;
                    entered = true;
                    var nextRevision = checked(revision + 1);
                    var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
                        "configuration.provider-mode", SecurityAuditOutcome.Requested,
                        request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : initiator,
                        "preferences.device-local");
                    auditLog.Write(audit);
                    var denied = policy.CommitVoiceSetting(request.Origin, callRevision,
                        () => ReferenceEquals(HostActivity.RequireCurrent().Request, request) && eligible()
                            && !cancellationToken.IsCancellationRequested && choice.Revision == revision && Read() == saved, () =>
                        {
                            held = true;
                            try
                            {
                                preferences.BeginWrite();
                                preferences.Save(choice.Mode);
                                if (Read(readBack: true) != choice.Mode)
                                { throw new InvalidDataException("Saved provider mode readback does not match the exact selection."); }
                            }
                            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentOutOfRangeException)
                            {
                                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed,
                                    exception is IOException ? "io-error" : exception is UnauthorizedAccessException ? "access-denied" : "invalid-data"));
                                throw;
                            }
                            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
                            if (revision != choice.Revision)
                            { throw new InvalidOperationException("Provider preference revision changed during terminal audit."); }
                            saved = desired = choice.Mode;
                            revision = nextRevision;
                            expectedRevision = nextRevision;
                            choices = [];
                        });
                    if (denied is { } reason)
                    {
                        auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Denied, reason.ToString().ToLowerInvariant()));
                        return false;
                    }
                    return true;
                }
            }, cancellationToken).ConfigureAwait(false);
            if (accepted)
            {
                lock (gate)
                {
                    if (revision != expectedRevision || !eligible() || cancellationToken.IsCancellationRequested)
                    { throw new InvalidOperationException("Provider apply admission changed before its receipt completed."); }
                    if (Read(readBack: true) != choice.Mode)
                    { throw new InvalidDataException("Saved provider mode changed before its completed receipt."); }
                    preferences.ConfirmWrite();
                    try
                    {
                        if (Read() != choice.Mode)
                        { throw new InvalidDataException("Confirmed provider mode readback does not match the exact selection."); }
                        if (revision != expectedRevision || !eligible() || cancellationToken.IsCancellationRequested)
                        { throw new InvalidOperationException("Provider confirmation admission changed before publication."); }
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
                        or ArgumentOutOfRangeException or InvalidOperationException)
                    { preferences.BeginWrite(); throw; }
                    held = false;
                    recovery = null;
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
            return accepted;
        }
        catch { if (entered) { HoldUnavailable(); } throw; }
        finally { if (entered) { lock (gate) { applying = false; } } }
    }

    public void HoldUnavailable()
    {
        lock (gate)
        {
            held = true;
            recovery = "Provider preference or admission/evidence is unconfirmed. Inspect saved state and receipts before explicit repair and refresh; no automatic retry.";
            choices = [];
            revision = checked(revision + 1);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}

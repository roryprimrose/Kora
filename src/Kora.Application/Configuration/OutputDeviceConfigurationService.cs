using System.Collections.Immutable;
using System.Collections.ObjectModel;

using Kora.Application.Communication;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed class OutputDeviceConfigurationService(
    IAudioDevicePreferences preferences, BoundedAudioOutputCatalog catalog,
    AudioControlAdmission admission, ISecurityAuditLog auditLog, ISpeechPlaybackService playback)
{
    private readonly Lock gate = new();
    private readonly Guid owner = Guid.NewGuid();
    private AudioOutputCatalogSnapshot? metadata;
    private WorkSessionAuthorization? session;
    private ReadOnlyCollection<OutputDeviceChoice> choices = Array.AsReadOnly<OutputDeviceChoice>([]);
    private string? desired;
    private string source = "unavailable";
    private string? recovery = "Refresh output metadata in the owning unlocked host.";
    private long revision;
    private bool applying;
    private bool held;

    public event EventHandler? Changed;

    public IReadOnlyList<OutputDeviceChoice> Choices { get { lock (gate) { return choices; } } }
    public AudioOutputCatalogSnapshot? Metadata { get { lock (gate) { return metadata; } } }

    public OutputDeviceCommandResult Get(long callRevision, string outcome = "observed")
    {
        lock (gate)
        {
            return Describe(metadata, desired, source, held, choices, recovery, revision, callRevision, outcome);
        }
    }

    private static OutputDeviceCommandResult Describe(AudioOutputCatalogSnapshot? metadata, string? desired,
        string source, bool held, IReadOnlyList<OutputDeviceChoice> choices, string? recovery,
        long revision, long callRevision, string outcome)
    {
        var effective = !held && metadata is { } current
            ? string.Equals(desired, SystemAudioDevices.Output.Id, StringComparison.Ordinal)
                ? current.Default : current.Devices.SingleOrDefault(device => string.Equals(device.Id, desired, StringComparison.Ordinal))
            : null;
        return new(outcome, recovery ?? (effective is null ? "Selected output is unavailable; no endpoint is substituted. Full visual output is retained."
            : effective.IsMuted ? "Windows software mute or zero volume: full visual output is required. Acoustic audibility is not claimed." : null),
            revision, callRevision, metadata is not null, desired, effective?.Id, metadata?.Default?.Id, source,
            effective is { IsMuted: false }, effective?.IsMuted == true)
        {
            Choices = choices.Select(choice => new OutputDeviceCommandChoice(choice.Id, choice.Device.Name,
                choice.Device.IsSystemDefault, choice.Device.IsSystemDefault
                    ? metadata?.Default is not null : true, choice.Device.IsSystemDefault
                    ? metadata?.Default?.IsMuted == true : choice.Device.IsMuted)).ToImmutableArray(),
        };
    }

    public async Task RefreshAsync(RequestOrigin origin, Func<bool> eligible, CancellationToken cancellationToken)
    {
        var snapshot = await catalog.RefreshAsync(cancellationToken).ConfigureAwait(false);
        ValidateMetadata(snapshot);
        snapshot = snapshot with { Devices = Array.AsReadOnly(snapshot.Devices.ToArray()) };
        var entered = false;
        var expectedRevision = 0L;
        try
        {
            var changed = await admission.RunAsync(origin, eligible, (request, authority) =>
            {
                lock (gate)
                {
                    if (applying) { throw new InvalidOperationException("Output preference is committing."); }
                    applying = true;
                    entered = true;
                    var id = preferences.LoadOutputDeviceId();
                    var nextDesired = id ?? SystemAudioDevices.Output.Id;
                    var nextSource = id is null ? "default" : "saved";
                    if (held || !Equivalent(metadata, snapshot) || !string.Equals(desired, nextDesired, StringComparison.Ordinal)
                        || !string.Equals(source, nextSource, StringComparison.Ordinal) || session != authority || choices.Count == 0
                        || choices[0].Origin != request.Origin || !choices[0].RemainsAdmitted())
                    {
                        var nextRevision = checked(revision + 1);
                        var nextChoices = Array.AsReadOnly(new[] { SystemAudioDevices.Output }.Concat(snapshot.Devices)
                            .Select(device => new OutputDeviceChoice(device, nextRevision, owner, authority, request.Origin, eligible)).ToArray());
                        _ = OutputDeviceCommandResult.Serialize(Describe(snapshot, nextDesired, nextSource, false,
                            nextChoices, null, nextRevision, 0, "observed"));
                        held = true;
                        playback.InvalidateOutput();
                        metadata = snapshot;
                        desired = nextDesired;
                        source = nextSource;
                        recovery = null;
                        session = authority;
                        revision = nextRevision;
                        expectedRevision = nextRevision;
                        choices = nextChoices;
                        return true;
                    }
                    return false;
                }
            }, cancellationToken).ConfigureAwait(false);
            if (changed)
            {
                lock (gate)
                {
                    if (revision != expectedRevision || !eligible()) { throw new InvalidOperationException("Output state or host admission changed before its discovery receipt completed."); }
                    held = false;
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        catch
        {
            if (entered)
            {
                lock (gate)
                {
                    held = true;
                    recovery = "Output discovery evidence was not confirmed. Inspect saved state and explicitly refresh.";
                    choices = Array.AsReadOnly<OutputDeviceChoice>([]);
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
            throw;
        }
        finally { if (entered) { lock (gate) { applying = false; } } }
    }

    public async Task<bool> SelectAsync(OutputDeviceChoice choice, long callRevision, RequestOrigin origin,
        SecurityAuditInitiator initiator, CallCommunicationPolicy policy, Func<bool> eligible,
        CancellationToken cancellationToken)
    {
        if (initiator == SecurityAuditInitiator.VoiceCommand && origin != RequestOrigin.ActivatedVoice)
        {
            throw new InvalidOperationException("Original activated voice provenance is required.");
        }
        var snapshot = await catalog.RefreshAsync(cancellationToken).ConfigureAwait(false);
        ValidateMetadata(snapshot);
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
                        || !choices.Any(item => ReferenceEquals(item, choice)) || !Equivalent(metadata, snapshot)
                        || !string.Equals(preferences.LoadOutputDeviceId() ?? SystemAudioDevices.Output.Id, desired, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("Output choice, session/generation or metadata changed. Refresh and choose again.");
                    }
                    applying = true;
                    entered = true;
                    {
                        var nextRevision = checked(revision + 1);
                        var nextChoices = Array.AsReadOnly(choices.Select(item =>
                            new OutputDeviceChoice(item.Device, nextRevision, owner, authority, item.Origin, item.RemainsAdmitted)).ToArray());
                        _ = OutputDeviceCommandResult.Serialize(Describe(metadata, choice.Id,
                            choice.Device.IsSystemDefault ? "default" : "saved", false, nextChoices, null,
                            nextRevision, callRevision, "saved"));
                        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
                            "configuration.audio-output", SecurityAuditOutcome.Requested,
                            request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : initiator,
                            "preferences.device-local");
                        auditLog.Write(audit);
                        var denied = policy.CommitVoiceSetting(request.Origin, callRevision, () =>
                            ReferenceEquals(HostActivity.RequireCurrent().Request, request) && eligible()
                            && !cancellationToken.IsCancellationRequested, () =>
                        {
                            held = true;
                            playback.InvalidateOutput();
                            try
                            {
                                if (choice.Device.IsSystemDefault) { preferences.ClearOutputDeviceId(); }
                                else { preferences.SaveOutputDeviceId(choice.Id); }
                            }
                            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                            {
                                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed,
                                    exception is IOException ? "io-error" : "access-denied"));
                                throw;
                            }
                            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
                            desired = choice.Id;
                            source = choice.Device.IsSystemDefault ? "default" : "saved";
                            recovery = null;
                            revision = nextRevision;
                            expectedRevision = nextRevision;
                            choices = nextChoices;
                        });
                        if (denied is { } reason)
                        {
                            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Denied, reason.ToString().ToLowerInvariant()));
                            return false;
                        }
                        return true;
                    }
                }
            }, cancellationToken).ConfigureAwait(false);
            if (accepted)
            {
                lock (gate)
                {
                    if (revision != expectedRevision || !eligible()) { throw new InvalidOperationException("Output state or host admission changed before its apply receipt completed."); }
                    held = false;
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
            return accepted;
        }
        catch
        {
            lock (gate)
            {
                if (entered)
                {
                    held = true;
                    recovery = "Output preference evidence was not confirmed. Inspect saved state and explicitly refresh.";
                    choices = Array.AsReadOnly<OutputDeviceChoice>([]);
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
            throw;
        }
        finally
        {
            if (entered) { lock (gate) { applying = false; } }
        }
    }

    public void Observe(AudioOutputCatalogSnapshot snapshot)
    {
        ValidateMetadata(snapshot);
        lock (gate)
        {
            if (applying) { throw new InvalidOperationException("Output metadata cannot replace a committing preference snapshot."); }
            var id = preferences.LoadOutputDeviceId();
            var nextDesired = id ?? SystemAudioDevices.Output.Id;
            var nextSource = id is null ? "default" : "saved";
            if (Equivalent(metadata, snapshot) && string.Equals(desired, nextDesired, StringComparison.Ordinal)
                && string.Equals(source, nextSource, StringComparison.Ordinal)) { return; }
            _ = OutputDeviceCommandResult.Serialize(Describe(snapshot, nextDesired, nextSource, held,
                [], recovery, revision, 0, "observed"));
            playback.InvalidateOutput();
            metadata = snapshot with { Devices = Array.AsReadOnly(snapshot.Devices.ToArray()) };
            desired = nextDesired;
            source = nextSource;
            if (!held) { recovery = null; }
            revision = checked(revision + 1);
            choices = Array.AsReadOnly<OutputDeviceChoice>([]);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void HoldUnavailable(string message)
    {
        lock (gate)
        {
            held = true;
            recovery = message;
            choices = Array.AsReadOnly<OutputDeviceChoice>([]);
            revision = checked(revision + 1);
            try { playback.InvalidateOutput(); }
            finally { Changed?.Invoke(this, EventArgs.Empty); }
        }
    }

    private static bool Equivalent(AudioOutputCatalogSnapshot? left, AudioOutputCatalogSnapshot right) =>
        left is not null && left.Default == right.Default && left.Devices.SequenceEqual(right.Devices);

    private static void ValidateMetadata(AudioOutputCatalogSnapshot snapshot)
    {
        if (snapshot.Devices.Any(device => string.IsNullOrWhiteSpace(device.Id) || device.Id.Any(char.IsControl)
            || device.IsSystemDefault || string.Equals(device.Id, SystemAudioDevices.Output.Id, StringComparison.Ordinal))
            || snapshot.Devices.Select(device => device.Id).Distinct(StringComparer.Ordinal).Count() != snapshot.Devices.Count
            || snapshot.Default is { } current && !snapshot.Devices.Contains(current))
        {
            throw new InvalidDataException("Output metadata is malformed or changed during enumeration. Refresh; no default or pin is substituted.");
        }
    }
}
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Skills;
using Kora.Core.Artifacts;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Skills;

public sealed partial class SharedSkillDiscoveryService(
    LocalSharedSkillPreferences preferences, ISharedSkillSourceReader reader, SharedSkillAdmission admission,
    ISecurityAuditLog auditLog, ILogger<SharedSkillDiscoveryService> logger, IArtifactUsePolicy? usePolicy = null)
{
    private volatile bool observationRequired;
    private readonly Lock consentGate = new();
    private readonly HashSet<Guid> withdrawnIdentities = [];
    private long consentRevision;
    internal long ConsentRevision => Interlocked.Read(ref consentRevision);
    internal long UseRevision => usePolicy?.Revision ?? 0;

    internal bool IsUseAllowed(SharedSkillSource source, SharedSkillSnapshot snapshot) =>
        usePolicy?.IsDisabled(ArtifactUseIdentity.File(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            source.ProfileRelativeRoot, snapshot.RelativeFile))) != true;

    public Task<IReadOnlyList<SharedSkillSource>> LoadSourcesAsync(Func<bool> eligible, CancellationToken token) =>
        admission.RunAsync(eligible, async (request, session, currentToken) =>
            await admission.CommitAsync(request, session, eligible, () =>
            {
                var sources = preferences.Load();
                observationRequired = false;
                return sources;
            }, currentToken).ConfigureAwait(false), token);

    public Task<SharedSkillSource> RegisterAsync(string selectedRoot, Func<bool> eligible, CancellationToken token) =>
        admission.RunAsync(eligible, async (request, session, currentToken) =>
        {
            // Invalid persisted state blocks selection and registration, rather than resetting consent.
            var before = await admission.CommitAsync(request, session, eligible, () =>
            {
                RequireObservation();
                return preferences.Load();
            }, currentToken).ConfigureAwait(false);
            var selected = await reader.SelectAsync(selectedRoot, currentToken).ConfigureAwait(false);
            return await admission.CommitAsync(request, session, eligible, () => preferences.WithUnchangedSources(before, () =>
            {
                RequireObservation();
                if (IsWithdrawn(selected.Id))
                { throw new InvalidOperationException("Fresh native selection must issue a new source identity."); }
                if (before.Any(source => string.Equals(source.ProfileRelativeRoot, selected.ProfileRelativeRoot,
                        StringComparison.OrdinalIgnoreCase) || string.Equals(source.DirectoryIdentity, selected.DirectoryIdentity, StringComparison.Ordinal)))
                { throw new InvalidDataException("That source, or a path alias for it, is already registered."); }
                var audit = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                    "skills.source.register", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser,
                    $"shared-source.{selected.Id:N}");
                auditLog.Write(audit);
                Interlocked.Increment(ref consentRevision);
                try
                {
                    preferences.WithUnchangedSources(before, () =>
                    {
                        preferences.Save(before.Append(selected).ToArray());
                        return true;
                    });
                    if (!preferences.Load().SequenceEqual(before.Append(selected)))
                    { throw new InvalidDataException("Source registration read-back did not match."); }
                    auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
                    RegistrationSaved(logger, before.Count + 1);
                    return selected;
                }
                catch
                {
                    observationRequired = true;
                    Interlocked.Increment(ref consentRevision);
                    auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed, "registration-failed"));
                    throw;
                }
            }), currentToken).ConfigureAwait(false);
        }, token);

    public Task<IReadOnlyList<SharedSkillSource>> UnregisterAsync(SharedSkillSource source,
        IReadOnlyList<SharedSkillSource> confirmedSources, Func<bool> eligible, CancellationToken token)
    {
        // Bind the confirmation to immutable metadata, not a mutable path/name or caller-owned list.
        var expected = confirmedSources.ToArray();
        return admission.RunAsync(eligible, async (request, session, currentToken) =>
            await admission.CommitAsync(request, session, eligible, () => preferences.WithUnchangedSources(expected, () =>
            {
                RequireObservation();
                if (!expected.Contains(source))
                { throw new InvalidOperationException("The exact confirmed source registration is unavailable."); }
                var remaining = expected.Where(item => item != source).ToArray();
                var audit = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                    "skills.source.unregister", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser,
                    $"shared-source.{source.Id:N}");
                auditLog.Write(audit);
                observationRequired = true;
                // An attempted publication can be durable even when read-back/audit fails.
                // Retain only the identity fence, never source content or a cached grant.
                lock (consentGate) { withdrawnIdentities.Add(source.Id); }
                try
                {
                    preferences.WithUnchangedSources(expected, () =>
                    {
                        preferences.Save(remaining);
                        return true;
                    });
                    if (!preferences.Load().SequenceEqual(remaining))
                    { throw new InvalidDataException("Source withdrawal read-back did not match."); }
                    currentToken.ThrowIfCancellationRequested();
                    if (!eligible())
                    { throw new InvalidOperationException("Source withdrawal admission changed after publication."); }
                    auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
                    observationRequired = false;
                    RegistrationWithdrawn(logger, remaining.Length);
                    return (IReadOnlyList<SharedSkillSource>)Array.AsReadOnly(remaining);
                }
                catch
                {
                    // A durable write may already have happened. Never imply rollback or reuse cached consent.
                    auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed, "withdrawal-unconfirmed"));
                    throw;
                }
            }), currentToken).ConfigureAwait(false), token);
    }

    public Task<SharedSkillCatalogue> DiscoverAsync(SharedSkillSource source, Func<bool> eligible, CancellationToken token) =>
        admission.RunAsync(eligible, async (request, session, currentToken) =>
        {
            await admission.CommitAsync(request, session, eligible, () => { RequireRegistration(source); return true; },
                currentToken).ConfigureAwait(false);
            var result = await reader.DiscoverAsync(source, currentToken).ConfigureAwait(false);
            await admission.CommitAsync(request, session, eligible, () => { RequireRegistration(source); return true; },
                currentToken).ConfigureAwait(false);
            DiscoveryCompleted(logger, result.Packages.Count);
            return result;
        }, token);

    public async Task<bool> IsCurrentAsync(SharedSkillSnapshot snapshot, SharedSkillSource source,
        Func<bool> eligible, CancellationToken token)
    {
        var current = await DiscoverAsync(source, eligible, token).ConfigureAwait(false);
        return Matches(current, snapshot);
    }

    internal Task ValidateForUseAsync(SharedSkillSnapshot snapshot, SharedSkillSource source,
        SharedSkillUseAdmission useAdmission, Func<bool> eligible, Kora.Core.Hosting.RequestOrigin origin, CancellationToken token) =>
        useAdmission.RunAsync(origin, eligible, async (request, session, live, currentToken) =>
        {
            await useAdmission.CommitAsync(request, session, live, () => { RequireRegistration(source); return true; },
                currentToken).ConfigureAwait(false);
            var current = await reader.DiscoverAsync(source, currentToken).ConfigureAwait(false);
            SharedSkillSnapshot? bundle = null;
            if (snapshot.ReferenceBundle is not null)
            {
                if (!Matches(current, snapshot)) { throw new InvalidOperationException("The declared skill revision changed."); }
                bundle = await reader.ReadReferencesAsync(source, snapshot, currentToken).ConfigureAwait(false);
            }
            await useAdmission.CommitAsync(request, session, live, () =>
            {
                RequireRegistration(source);
                if (current.Source != source || !Matches(current, snapshot)
                    || bundle is not null && (!bundle.IsInstructionCompatible || !string.Equals(bundle.RevisionDigest, snapshot.RevisionDigest, StringComparison.Ordinal)))
                { throw new InvalidOperationException("The exact reviewed shared skill source changed; review and enable again."); }
                return true;
            }, currentToken).ConfigureAwait(false);
            return true;
        }, token);

    internal async Task ValidateCapturedRevisionAsync(SharedSkillSnapshot snapshot, SharedSkillSource source,
        Func<bool> eligible, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!eligible()) { throw new InvalidOperationException("The original skill result source was retired."); }
        RequireRegistration(source);
        var current = await reader.DiscoverAsync(source, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        RequireRegistration(source);
        if (!eligible() || current.Source != source || !Matches(current, snapshot))
        { throw new InvalidOperationException("The original skill result source changed; no source refresh or authority renewal is admitted."); }
    }

    internal Task<SharedSkillSnapshot> ReadReferencesAsync(SharedSkillSnapshot snapshot, SharedSkillSource source,
        SharedSkillUseAdmission useAdmission, Func<bool> eligible, Kora.Core.Hosting.RequestOrigin origin, CancellationToken token) =>
        useAdmission.RunAsync(origin, eligible, async (request, session, live, currentToken) =>
        {
            var entry = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.SecurityApproval,
                "skills.references.read", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser,
                $"shared-revision.{snapshot.SourceId:N}.{snapshot.RevisionDigest}");
            await useAdmission.CommitAsync(request, session, live, () =>
            { RequireRegistration(source); auditLog.Write(entry); return true; }, currentToken).ConfigureAwait(false);
            try
            {
                var result = await reader.ReadReferencesAsync(source, snapshot, currentToken).ConfigureAwait(false);
                return await useAdmission.CommitAsync(request, session, live, () =>
                {
                    RequireRegistration(source);
                    if (!result.IsInstructionCompatible || result.ReferenceBundle is null
                        || result.SourceId != snapshot.SourceId || !string.Equals(result.RelativeFile, snapshot.RelativeFile, StringComparison.Ordinal)
                        || !string.Equals(result.PackageDirectoryIdentity, snapshot.PackageDirectoryIdentity, StringComparison.Ordinal)
                        || !result.Bytes.SequenceEqual(snapshot.Bytes))
                    { throw new InvalidDataException("Reference capture did not return the exact complete reviewed package."); }
                    auditLog.Write(entry.WithOutcome(SecurityAuditOutcome.Succeeded));
                    return result;
                }, currentToken).ConfigureAwait(false);
            }
            catch
            {
                auditLog.Write(entry.WithOutcome(SecurityAuditOutcome.Failed, "reference-read-unconfirmed"));
                throw;
            }
        }, token);

    private static bool Matches(SharedSkillCatalogue current, SharedSkillSnapshot snapshot) =>
        current.Packages.Any(package => string.Equals(package.SourceQualifiedIdentity, snapshot.SourceQualifiedIdentity, StringComparison.Ordinal)
            && string.Equals(package.RelativeFile, snapshot.RelativeFile, StringComparison.Ordinal)
            && package.Bytes.SequenceEqual(snapshot.Bytes)
            && string.Equals(package.PackageDirectoryIdentity, snapshot.PackageDirectoryIdentity, StringComparison.Ordinal)
            && package.UninspectedFiles.SequenceEqual(snapshot.UninspectedFiles, StringComparer.Ordinal)
            && (snapshot.ReferenceBundle is null
                ? package.UnavailableReasons.SequenceEqual(snapshot.UnavailableReasons, StringComparer.Ordinal)
                : package.IsReferenceCandidate));

    private void RequireRegistration(SharedSkillSource source)
    {
        RequireObservation();
        if (IsWithdrawn(source.Id) || !preferences.Load().Contains(source))
        { throw new InvalidOperationException("The exact source registration is unavailable."); }
    }

    private bool IsWithdrawn(Guid id)
    {
        lock (consentGate) { return withdrawnIdentities.Contains(id); }
    }

    private void RequireObservation()
    {
        if (observationRequired)
        { throw new InvalidOperationException("Source mutation is unconfirmed; refresh registrations before any further read."); }
    }
}

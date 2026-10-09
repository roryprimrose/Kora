using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Skills;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Skills;

public sealed partial class SharedSkillDiscoveryService(
    LocalSharedSkillPreferences preferences, ISharedSkillSourceReader reader, SharedSkillAdmission admission,
    ISecurityAuditLog auditLog, ILogger<SharedSkillDiscoveryService> logger)
{
    private bool observationRequired;
    private readonly HashSet<Guid> withdrawnIdentities = [];

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
                if (withdrawnIdentities.Contains(selected.Id))
                { throw new InvalidOperationException("Fresh native selection must issue a new source identity."); }
                if (before.Any(source => string.Equals(source.ProfileRelativeRoot, selected.ProfileRelativeRoot,
                        StringComparison.OrdinalIgnoreCase) || string.Equals(source.DirectoryIdentity, selected.DirectoryIdentity, StringComparison.Ordinal)))
                { throw new InvalidDataException("That source, or a path alias for it, is already registered."); }
                var audit = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                    "skills.source.register", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser,
                    $"shared-source.{selected.Id:N}");
                auditLog.Write(audit);
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
                withdrawnIdentities.Add(source.Id);
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
            void RequireRegistration()
            {
                RequireObservation();
                if (withdrawnIdentities.Contains(source.Id) || !preferences.Load().Contains(source))
                { throw new InvalidOperationException("The exact source registration is unavailable."); }
            }
            await admission.CommitAsync(request, session, eligible, () => { RequireRegistration(); return true; },
                currentToken).ConfigureAwait(false);
            var result = await reader.DiscoverAsync(source, currentToken).ConfigureAwait(false);
            await admission.CommitAsync(request, session, eligible, () => { RequireRegistration(); return true; },
                currentToken).ConfigureAwait(false);
            DiscoveryCompleted(logger, result.Packages.Count);
            return result;
        }, token);

    public async Task<bool> IsCurrentAsync(SharedSkillSnapshot snapshot, SharedSkillSource source,
        Func<bool> eligible, CancellationToken token)
    {
        var current = await DiscoverAsync(source, eligible, token).ConfigureAwait(false);
        return current.Packages.Any(package => string.Equals(package.SourceQualifiedIdentity, snapshot.SourceQualifiedIdentity, StringComparison.Ordinal)
            && string.Equals(package.RelativeFile, snapshot.RelativeFile, StringComparison.Ordinal)
            && string.Equals(package.RevisionDigest, snapshot.RevisionDigest, StringComparison.Ordinal)
            && package.UninspectedFiles.SequenceEqual(snapshot.UninspectedFiles, StringComparer.Ordinal)
            && package.UnavailableReasons.SequenceEqual(snapshot.UnavailableReasons, StringComparer.Ordinal));
    }

    private void RequireObservation()
    {
        if (observationRequired)
        { throw new InvalidOperationException("Source mutation is unconfirmed; refresh registrations before any further read."); }
    }
}

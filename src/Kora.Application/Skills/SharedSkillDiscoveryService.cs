using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Skills;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Skills;

public sealed partial class SharedSkillDiscoveryService(
    LocalSharedSkillPreferences preferences, ISharedSkillSourceReader reader, SharedSkillAdmission admission,
    ISecurityAuditLog auditLog, ILogger<SharedSkillDiscoveryService> logger)
{
    public Task<IReadOnlyList<SharedSkillSource>> LoadSourcesAsync(Func<bool> eligible, CancellationToken token) =>
        admission.RunAsync(eligible, async (request, session, currentToken) =>
            await admission.CommitAsync(request, session, eligible, preferences.Load, currentToken).ConfigureAwait(false), token);

    public Task<SharedSkillSource> RegisterAsync(string selectedRoot, Func<bool> eligible, CancellationToken token) =>
        admission.RunAsync(eligible, async (request, session, currentToken) =>
        {
            // Invalid persisted state blocks selection and registration, rather than resetting consent.
            var before = await admission.CommitAsync(request, session, eligible, preferences.Load, currentToken).ConfigureAwait(false);
            var selected = await reader.SelectAsync(selectedRoot, currentToken).ConfigureAwait(false);
            return await admission.CommitAsync(request, session, eligible, () =>
            {
                if (!preferences.Load().SequenceEqual(before))
                { throw new InvalidOperationException("Source preferences changed; refresh and select again."); }
                if (before.Any(source => string.Equals(source.ProfileRelativeRoot, selected.ProfileRelativeRoot,
                        StringComparison.OrdinalIgnoreCase) || string.Equals(source.DirectoryIdentity, selected.DirectoryIdentity, StringComparison.Ordinal)))
                { throw new InvalidDataException("That source, or a path alias for it, is already registered."); }
                var audit = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                    "skills.source.register", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser,
                    $"shared-source.{selected.Id:N}");
                auditLog.Write(audit);
                try
                {
                    preferences.Save(before.Append(selected).ToArray());
                    if (!preferences.Load().SequenceEqual(before.Append(selected)))
                    { throw new InvalidDataException("Source registration read-back did not match."); }
                    auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
                    RegistrationSaved(logger, before.Count + 1);
                    return selected;
                }
                catch
                {
                    auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed, "registration-failed"));
                    throw;
                }
            }, currentToken).ConfigureAwait(false);
        }, token);

    public Task<SharedSkillCatalogue> DiscoverAsync(SharedSkillSource source, Func<bool> eligible, CancellationToken token) =>
        admission.RunAsync(eligible, async (request, session, currentToken) =>
        {
            void RequireRegistration()
            {
                if (!preferences.Load().Contains(source))
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
}

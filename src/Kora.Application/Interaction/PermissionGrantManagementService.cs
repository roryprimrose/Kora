using System.Collections.Immutable;

using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Network;
using Kora.Core.Storage;

namespace Kora.Application.Interaction;

/// <summary>Shared management inventory; each grant kind retains its own authoritative enforcement and persistence.</summary>
public sealed class PermissionGrantManagementService(IExactGrantStore exact, IPreapprovedUriConfiguration? uris = null)
{
    public bool CanManageUriRules => uris is not null;

    public async Task<PermissionGrantInventory> ReadAsync(ExactGrantQuery query, ExactGrantCursor? cursor, CancellationToken token)
    {
        var page = await exact.ReadExactGrantPageAsync(query, cursor, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var rules = uris is null ? [] : PreapprovedUriSettings.Create(uris.GetSettings().Patterns).Patterns;
        var records = page.Records.Select(item => (PermissionGrantRecord)new ExactOperationPermissionGrant(item))
            .Concat(rules.Select(pattern => new UriPermissionGrant(pattern))).ToImmutableArray();
        return new(page, records);
    }

    public void AddUriRule(string pattern, Func<bool> admitted) =>
        ChangeUriRules(source => source.Add(pattern, SecurityAuditInitiator.LocalUser), admitted);

    public void RemoveUriRule(UriPermissionGrant grant, Func<bool> admitted) =>
        ChangeUriRules(source => source.Remove(grant.Pattern, SecurityAuditInitiator.LocalUser), admitted);

    public void ClearUriRules(Func<bool> admitted) =>
        ChangeUriRules(source => source.Clear(SecurityAuditInitiator.LocalUser), admitted);

    private void ChangeUriRules(Action<IPreapprovedUriConfiguration> change, Func<bool> admitted)
    {
        if (uris is null || HostActivity.HasScope || !admitted())
        {
            throw new InvalidOperationException("URI permission changes require fresh original native input and current owning unlocked host control.");
        }
        HostRequestRunner.Run(RequestOrigin.LocalUi, () =>
        {
            if (!admitted()) { throw new InvalidOperationException("URI permission control changed before commit."); }
            change(uris);
        });
    }
}

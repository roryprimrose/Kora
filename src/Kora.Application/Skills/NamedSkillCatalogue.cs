using Kora.Core.Artifacts;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.Core.Storage;

namespace Kora.Application.Skills;

/// <summary>Explicit native inspection using the existing bounded owned and registered-source readers.</summary>
public sealed class NamedSkillCatalogue(
    IOwnedDeclarativeSkillStore store, OwnedSkillSaveWorkflow owned, SharedSkillDiscoveryService shared,
    ISessionWorkspaceAccess access, IArtifactUsePolicy usePolicy, TimeProvider time)
{
    public async Task<NamedSkillCandidates> InspectAsync(string name, OwnedSkillAuthoringAuthority original,
        Func<bool> eligible, CancellationToken token)
    {
        NamedSkillCommand.ValidateName(name);
        if (HostActivity.RequireCurrent().Request.Origin != RequestOrigin.LocalUi)
        { throw new InvalidOperationException("Inspect all saved and registered skill files with the separate native read-consent control."); }
        var control = access.ControlRevision;
        var policy = usePolicy.Revision;
        bool Current() => original.IsCurrent() && eligible() && access.CanControl && access.CanInspect
            && access.ControlRevision == control && usePolicy.Revision == policy;
        Require(Current, token);
        var native = new OwnedSkillAuthoringAuthority(RequestOrigin.LocalUi, Current);
        var sources = await shared.LoadSourcesAsync(Current, token).ConfigureAwait(false);
        var consent = shared.ConsentRevision;
        bool Registered() => Current() && shared.ConsentRevision == consent;
        var saved = await owned.List(native, token).ConfigureAwait(false);
        Require(Registered, token);
        var catalogues = new List<SharedSkillCatalogue>();
        foreach (var source in sources)
        {
            var catalogue = await shared.DiscoverAsync(source, Registered, token).ConfigureAwait(false);
            Require(Registered, token);
            if (catalogue.Source != source) { throw new InvalidDataException("Inspection returned a different source identity."); }
            catalogues.Add(catalogue);
        }
        Require(Registered, token);
        return new(name, saved, catalogues, original, Registered, time,
            snapshot => !usePolicy.IsDisabled(ArtifactUseIdentity.File(Path.Combine(store.Destination(snapshot.Document), "instructions.md")))
                && !usePolicy.IsDisabled(ArtifactUseIdentity.File(Path.Combine(store.Destination(snapshot.Document), "manifest.yaml"))),
            shared.IsUseAllowed);
    }

    private static void Require(Func<bool> current, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!current()) { throw new InvalidOperationException("Original private skill inspection authority expired. No lookup result is current."); }
    }
}

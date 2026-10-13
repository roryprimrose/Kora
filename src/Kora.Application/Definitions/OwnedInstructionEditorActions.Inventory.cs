using Kora.Core.Artifacts;

namespace Kora.Application.Definitions;

public sealed partial class OwnedInstructionEditorActions
{
    public event EventHandler? InventoryChanged
    {
        add => catalogue.Changed += value;
        remove => catalogue.Changed -= value;
    }

    public event EventHandler<ArtifactCatalogueReloadFailedEventArgs>? InventoryReloadFailed
    {
        add => catalogue.ReloadFailed += value;
        remove => catalogue.ReloadFailed -= value;
    }

    public Task<OwnedDefinitionInventory> ObserveInventory(IReadOnlyList<string> names, Func<bool> eligible, CancellationToken token) =>
        admission.Run(eligible, (_, _, _, _) =>
        {
            var failure = catalogue.ReloadFailureRevision;
            var candidates = catalogue.Candidates;
            var snapshot = new OwnedDefinitionInventory(Kind, names, candidates, failure, DateTimeOffset.UtcNow);
            RequireInventory(snapshot);
            return Task.FromResult(snapshot);
        }, token);

    public bool IsInventoryCurrent(OwnedDefinitionInventory snapshot) =>
        !catalogue.IsReloadUnavailable && snapshot.Kind == Kind && snapshot.FailureRevision == catalogue.ReloadFailureRevision
        && ReferenceEquals(snapshot.CatalogueIdentity, catalogue.Candidates);

    private void RequireInventory(OwnedDefinitionInventory snapshot)
    {
        if (!IsInventoryCurrent(snapshot))
        { throw new InvalidOperationException("The observed catalogue changed or became unavailable. Refresh and reselect the exact row; the draft is retained."); }
    }

    public Task<OwnedInstructionSnapshot> InspectInventory(OwnedDefinitionInventory snapshot, OwnedDefinitionInventoryRow row,
        Func<bool> eligible, CancellationToken token) =>
        admission.Run(eligible, async (_, _, current, admitted) =>
        {
            RequireInventory(snapshot);
            if (!snapshot.Rows.Any(item => ReferenceEquals(item, row)) || row.NativeRevision is null)
            { throw new InvalidOperationException("This row has no exact admitted native revision. Refresh or explicitly inspect its current filename ID for compatibility; no match is assumed."); }
            var live = await store.Read(row.Name, admitted).ConfigureAwait(false);
            RequireInventory(snapshot);
            if (!current() || live is null || live.Document.Definition.Kind != row.Kind
                || !string.Equals(live.CanonicalFileName, row.CanonicalFileName, StringComparison.Ordinal)
                || !string.Equals(live.Revision, row.NativeRevision, StringComparison.Ordinal)
                || !string.Equals(live.Document.Definition.Digest, row.Digest, StringComparison.Ordinal)
                || !string.Equals(live.Document.Definition.Id, row.ArtifactId, StringComparison.Ordinal))
            { throw new InvalidOperationException("The selected original changed, was replaced or disappeared. Refresh and reselect; no newer source was loaded and the draft is retained."); }
            return live;
        }, token);
}

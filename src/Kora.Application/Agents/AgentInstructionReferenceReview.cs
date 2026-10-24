using System.Collections.Immutable;

using Kora.Core.Agents;
using Kora.Core.Artifacts;

namespace Kora.Application.Agents;

/// <summary>A pending candidate review or resolved bundle, owned only by its exact agent review.</summary>
public sealed class AgentInstructionReferenceReview : IDisposable
{
    private readonly AgentInstructionReferenceResolver owner;
    private readonly BoundedAgentProfile profile;
    private readonly Action revoke;
    private readonly long reloadRevision;
    private ImmutableArray<Guid> epochs = [];
    private int resolved;
    private int retired;

    internal AgentInstructionReferenceReview(AgentInstructionReferenceResolver owner, BoundedAgentProfile profile,
        IEnumerable<ArtifactDefinition> candidates, Action revoke)
    {
        this.owner = owner;
        this.profile = profile;
        this.revoke = revoke;
        Candidates = candidates.ToImmutableArray();
        reloadRevision = owner.Catalogue.ReloadFailureRevision;
        owner.Catalogue.Changed += Changed;
        owner.Catalogue.ReloadFailed += ReloadFailed;
    }

    public ImmutableArray<ArtifactDefinition> Candidates { get; }
    public AgentInstructionBundle? Bundle { get; private set; }
    internal AgentInstructionBundle RequireBundle() =>
        Bundle ?? throw new InvalidOperationException("Separately consent to resolve before invocation review.");

    internal bool IsCurrent()
    {
        if (Volatile.Read(ref retired) != 0 || owner.Catalogue.IsReloadUnavailable
            || owner.Catalogue.ReloadFailureRevision != reloadRevision
            || Candidates.Any(candidate => !owner.Catalogue.Candidates.Any(item =>
                ReferenceEquals(item, candidate))))
        { return false; }
        var bundle = Bundle;
        return bundle is null || !bundle.Instructions.Where((snapshot, index) =>
            !owner.Revisions.IsEnabled(snapshot) || !owner.Revisions.IsCapturedUseAllowed(snapshot, epochs[index])).Any();
    }

    internal async Task ResolveAsync(Func<bool> current, CancellationToken token)
    {
        if (Interlocked.Exchange(ref resolved, 1) != 0)
        { throw new InvalidOperationException("Resolution consent is single-use; begin a new review."); }
        try
        {
            var snapshots = ImmutableArray.CreateBuilder<OwnedInstructionSnapshot>();
            var captured = ImmutableArray.CreateBuilder<Guid>();
            foreach (var candidate in Candidates)
            {
                Require(current, token);
                var snapshot = await owner.ReadAsync(candidate, token).ConfigureAwait(false);
                Require(current, token);
                snapshots.Add(snapshot);
                captured.Add(owner.Revisions.CaptureReviewEpoch(snapshot));
            }
            epochs = captured.ToImmutable();
            Bundle = new(profile, snapshots);
            await ValidateAsync(current, token).ConfigureAwait(false);
        }
        catch { Dispose(); throw; }
    }

    internal async Task ValidateAsync(Func<bool> current, CancellationToken token)
    {
        _ = RequireBundle();
        for (var index = 0; index < Candidates.Length; index++)
        {
            Require(current, token);
            _ = await owner.ReadAsync(Candidates[index], token).ConfigureAwait(false);
            Require(current, token);
        }
    }

    private void Require(Func<bool> current, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!current() || !IsCurrent())
        { throw new InvalidOperationException("The instruction bundle was retired; begin a fresh review."); }
    }

    private void Changed(object? sender, EventArgs args)
    {
        if (!IsCurrent()) { Dispose(); }
    }

    private void ReloadFailed(object? sender, ArtifactCatalogueReloadFailedEventArgs args) => Dispose();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref retired, 1) != 0) { return; }
        owner.Catalogue.Changed -= Changed;
        owner.Catalogue.ReloadFailed -= ReloadFailed;
        revoke();
    }
}

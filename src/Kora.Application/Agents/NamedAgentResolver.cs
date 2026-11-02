using Kora.Core.Artifacts;

namespace Kora.Application.Agents;

public sealed class NamedAgentResolver(ArtifactCatalogue catalogue,
    Func<ArtifactDefinition, CancellationToken, Task<string>> resolvePath)
{
    public IReadOnlyList<ArtifactDefinition> ReadCandidates(string name, Func<bool> eligible, CancellationToken token)
    {
        Require(() => eligible() && !catalogue.IsReloadUnavailable, token);
        return catalogue.Candidates.Where(item => item.Kind == ArtifactKind.Agent
            && string.Equals(item.Name, name, StringComparison.Ordinal)).ToArray();
    }

    public Task<NamedAgentSelection> ResolveAsync(string name, Func<bool> eligible, CancellationToken token) =>
        ResolveAsync(name, null, eligible, token);

    public Task<NamedAgentSelection> ResolveAsync(ArtifactDefinition selected, Func<bool> eligible, CancellationToken token) =>
        ResolveAsync(selected.Name, selected, eligible, token);

    private async Task<NamedAgentSelection> ResolveAsync(string name, ArtifactDefinition? choice,
        Func<bool> eligible, CancellationToken token)
    {
        var candidates = catalogue.Candidates;
        var failureRevision = catalogue.ReloadFailureRevision;
        var useRevision = catalogue.UseControlRevision;
        bool Current() => eligible() && !catalogue.IsReloadUnavailable
            && catalogue.ReloadFailureRevision == failureRevision && catalogue.UseControlRevision == useRevision
            && ReferenceEquals(catalogue.Candidates, candidates);
        Require(Current, token);
        var matches = candidates.Where(item => item.Kind == ArtifactKind.Agent
            && string.Equals(item.Name, name, StringComparison.Ordinal)
            && (choice is null || item == choice)).ToArray();
        if (matches.Length != 1)
        { throw new InvalidOperationException("The exact readable agent name is missing or ambiguous. Review registered sources; no agent was selected."); }
        var selected = matches[0];
        if (!selected.IsUseEnabled || selected.IsUseDisabled || selected.UseControlId is null)
        { throw new InvalidOperationException("This registered agent source is disabled or has no host-owned file binding."); }
        var path = await resolvePath(selected, token).ConfigureAwait(false);
        Require(Current, token);
        if (!string.Equals(ArtifactUseIdentity.File(path), selected.UseControlId, StringComparison.Ordinal))
        { throw new InvalidOperationException("The registered agent's exact source binding changed."); }
        return new(selected, path, Current);
    }

    private static void Require(Func<bool> current, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!current()) { throw new InvalidOperationException("The original agent selection or registered source inventory expired."); }
    }
}

using System.Collections.ObjectModel;

using Kora.Core.Commands;

namespace Kora.Core.Artifacts;

public sealed class ArtifactCatalogue
{
    private readonly Lock gate = new();
    private ReadOnlyCollection<ArtifactDefinition> artifacts;
    private long reloadFailureRevision;
    private int reloadUnavailable;

    public ArtifactCatalogue(IReadOnlyList<ArtifactDefinition> artifacts)
    {
        this.artifacts = Validate(artifacts);
    }

    public event EventHandler? Changed;
    public event EventHandler<ArtifactCatalogueReloadFailedEventArgs>? ReloadFailed;

    public ReadOnlyCollection<ArtifactDefinition> Candidates => Volatile.Read(ref artifacts);
    public long ReloadFailureRevision => Interlocked.Read(ref reloadFailureRevision);
    public bool IsReloadUnavailable => Volatile.Read(ref reloadUnavailable) != 0;
    public ReadOnlyCollection<ArtifactDefinition> Artifacts =>
        new(Candidates.Where(artifact => artifact.IsUseEnabled).ToArray());

    public bool IsCurrent(string id, string digest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(digest);
        return Artifacts.Any(artifact =>
            string.Equals(artifact.Id, id, StringComparison.Ordinal)
            && string.Equals(artifact.Digest, digest, StringComparison.Ordinal));
    }

    public bool IsCapturedRevisionCurrent(string id, string digest) =>
        Candidates.Any(artifact => (artifact.IsUseEnabled || artifact.PreservesCapturedUse)
            && string.Equals(artifact.Id, id, StringComparison.Ordinal)
            && string.Equals(artifact.Digest, digest, StringComparison.Ordinal));

    public void Publish(IReadOnlyList<ArtifactDefinition> updatedArtifacts)
    {
        Replace(_ => updatedArtifacts, recovered: true);
    }

    public void Update(Func<IReadOnlyList<ArtifactDefinition>, IReadOnlyList<ArtifactDefinition>> update) =>
        Replace(update);

    private void Replace(Func<IReadOnlyList<ArtifactDefinition>, IReadOnlyList<ArtifactDefinition>> update, bool recovered = false)
    {
        ReadOnlyCollection<ArtifactDefinition> previous;
        ReadOnlyCollection<ArtifactDefinition> validated;
        lock (gate)
        {
            previous = artifacts;
            validated = Validate(update(previous));
            Volatile.Write(ref artifacts, validated);
            if (recovered) { Volatile.Write(ref reloadUnavailable, 0); }
        }
        if (!Equivalent(previous, validated))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ReportReloadFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (gate)
        {
            Volatile.Write(ref reloadUnavailable, 1);
            Interlocked.Increment(ref reloadFailureRevision);
        }
        ReloadFailed?.Invoke(this, new(exception));
    }

    private static ReadOnlyCollection<ArtifactDefinition> Validate(
        IReadOnlyList<ArtifactDefinition> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        if (artifacts.Count is 0 || artifacts.Any(artifact => artifact is null))
        {
            throw new ArgumentException("The artifact catalogue must be nonempty and bounded.", nameof(artifacts));
        }
        var ownedInstructions = artifacts.Count(artifact => IsOwned(artifact, ArtifactKind.Instruction));
        var ownedPrompts = artifacts.Count(artifact => IsOwned(artifact, ArtifactKind.Prompt));
        if (ownedInstructions > OwnedDefinitionKind.MaximumFiles(ArtifactKind.Instruction)
            || ownedPrompts > OwnedDefinitionKind.MaximumFiles(ArtifactKind.Prompt)
            || artifacts.Count - ownedInstructions - ownedPrompts > 128)
        { throw new ArgumentException("The artifact catalogue exceeds its separately bounded owned-kind or existing other-artifact inventory.", nameof(artifacts)); }
        if (artifacts.Select(artifact => artifact.Id).Distinct(StringComparer.Ordinal).Count() != artifacts.Count
            || artifacts.Select(artifact => artifact.CommandName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != artifacts.Count)
        {
            throw new ArgumentException("Artifact IDs and slash commands must be unique.", nameof(artifacts));
        }
        var duplicateSpokenName = artifacts
            .SelectMany(artifact => artifact.SpokenNames.Select(name => (
                Name: BuiltInCommandRouter.Normalize(name),
                Artifact: artifact)))
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Select(item => item.Artifact.Id).Distinct(StringComparer.Ordinal).Skip(1).Any());
        if (duplicateSpokenName is not null)
        {
            throw new ArgumentException(
                $"The normalized spoken artifact name “{duplicateSpokenName.Key}” is ambiguous.",
                nameof(artifacts));
        }
        return new ReadOnlyCollection<ArtifactDefinition>(artifacts.ToArray());
    }

    private static bool IsOwned(ArtifactDefinition artifact, ArtifactKind kind) =>
        artifact.Kind == kind && artifact.Source is "kora-profile" && artifact.OwnedInstructionName is not null;

    private static bool Equivalent(
        ReadOnlyCollection<ArtifactDefinition> left,
        ReadOnlyCollection<ArtifactDefinition> right) =>
        left.Count == right.Count
        && left.Zip(right).All(pair =>
            string.Equals(pair.First.Id, pair.Second.Id, StringComparison.Ordinal)
            && string.Equals(pair.First.Digest, pair.Second.Digest, StringComparison.Ordinal)
            && pair.First.PreservesCapturedUse == pair.Second.PreservesCapturedUse
            && pair.First.IsUseEnabled == pair.Second.IsUseEnabled);
}

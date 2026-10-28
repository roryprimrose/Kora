using System.Collections.ObjectModel;

using Kora.Core.Commands;

namespace Kora.Core.Artifacts;

public sealed class ArtifactCatalogue : IDisposable
{
    private readonly Lock gate = new();
    private ReadOnlyCollection<ArtifactDefinition> artifacts;
    private long reloadFailureRevision;
    private int reloadUnavailable;
    private readonly IArtifactUsePolicy? usePolicy;
    private ReadOnlyCollection<ArtifactDefinition>? maskedCandidates;
    private long maskedUseRevision;

    public ArtifactCatalogue(IReadOnlyList<ArtifactDefinition> artifacts, IArtifactUsePolicy? usePolicy = null)
    {
        this.artifacts = Validate(artifacts);
        this.usePolicy = usePolicy;
        if (usePolicy is not null) { usePolicy.Changed += OnUsePolicyChanged; }
    }

    public event EventHandler? Changed;
    public event EventHandler<ArtifactCatalogueReloadFailedEventArgs>? ReloadFailed;

    /// <summary>Source observations before local use masks; these are not invocation admission.</summary>
    public ReadOnlyCollection<ArtifactDefinition> SourceCandidates => Volatile.Read(ref artifacts);

    public ReadOnlyCollection<ArtifactDefinition> Candidates
    {
        get
        {
            lock (gate)
            {
                var revision = usePolicy?.Revision ?? 0;
                if (maskedCandidates is not null && maskedUseRevision == revision) { return maskedCandidates; }
                maskedCandidates = new(artifacts.Select(artifact =>
                    usePolicy is not null && (usePolicy.IsDisabled(artifact.UseControlId ?? artifact.Id) || usePolicy.IsDisabled(artifact.Id))
                        ? artifact with { IsUseEnabled = false, IsUseDisabled = true, PreservesCapturedUse = false }
                        : artifact.UseControlId is { } source && (IsReloadUnavailable
                            || artifact.OwnedInstructionName is null && usePolicy is not null
                                && !usePolicy.IsRevisionAllowed(source, artifact.Digest))
                            ? artifact with { IsUseEnabled = false, PreservesCapturedUse = false }
                        : artifact).ToArray());
                maskedUseRevision = revision;
                return maskedCandidates;
            }
        }
    }
    public long ReloadFailureRevision => Interlocked.Read(ref reloadFailureRevision);
    public long UseControlRevision => usePolicy?.Revision ?? 0;
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

    private void OnUsePolicyChanged(object? sender, EventArgs args)
    {
        lock (gate) { maskedCandidates = null; }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (usePolicy is not null) { usePolicy.Changed -= OnUsePolicyChanged; }
    }

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
        bool recoveredFromFailure;
        lock (gate)
        {
            recoveredFromFailure = recovered && IsReloadUnavailable;
            previous = artifacts;
            validated = Validate(update(previous));
            Volatile.Write(ref artifacts, validated);
            maskedCandidates = null;
            if (recovered) { Volatile.Write(ref reloadUnavailable, 0); }
        }
        if (recoveredFromFailure || !Equivalent(previous, validated))
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
            maskedCandidates = null;
            Interlocked.Increment(ref reloadFailureRevision);
        }
        // Invalidation can retire consumers and unsubscribe their terminal failure handlers.
        var reloadFailed = ReloadFailed;
        Changed?.Invoke(this, EventArgs.Empty);
        reloadFailed?.Invoke(this, new(exception));
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

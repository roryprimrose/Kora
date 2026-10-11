namespace Kora.Core.Artifacts;

/// <summary>Dedicated bounded owned-definition storage, never arbitrary filesystem authority.</summary>
public interface IOwnedInstructionStore
{
    /// <summary>Lists bounded native owned instruction file names.</summary>
    Task<IReadOnlyList<string>> List(CancellationToken token);
    /// <summary>Reads an exact host-derived owned file, or observes its absence.</summary>
    Task<OwnedInstructionSnapshot?> Read(string name, CancellationToken token);
    /// <summary>Optimistically compares the reviewed base and atomically publishes complete bytes or deletes it.</summary>
    Task<InstructionWriteResult> Write(string name, OwnedInstructionSnapshot? expected,
        ArtifactFileDocument? replacement, Func<bool> eligible,
        Func<Func<InstructionWriteResult>, Task<InstructionWriteResult>> commit, CancellationToken token);
}

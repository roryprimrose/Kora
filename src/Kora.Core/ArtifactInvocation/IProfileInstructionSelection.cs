using Kora.Core.Context;

namespace Kora.Core.Artifacts;

/// <summary>A disposable, exact-file read lease; neither registration nor activation authority.</summary>
public interface IProfileInstructionSelection : IDisposable
{
    /// <summary>Gets the host-generated identity of this one-operation lease.</summary>
    Guid Id { get; }
    /// <summary>Gets the complete source metadata disclosed before content-read consent.</summary>
    LocalFileMetadata Metadata { get; }
    /// <summary>Captures the complete source only after the caller obtains exact read consent.</summary>
    Task<byte[]> Read(CancellationToken token);
    /// <summary>Revalidates the same source and bytes immediately around a guarded publication.</summary>
    Task<InstructionWriteResult> WithCurrent(ProfileInstructionSnapshot expected, Func<bool> consentCurrent,
        Func<Func<bool>, Task<InstructionWriteResult>> publish, CancellationToken token);
}

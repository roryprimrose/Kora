using Kora.Core.Artifacts;
using Kora.Core.Dependencies;

namespace Kora.Windows.Artifacts;

public sealed class WindowsOwnedInstructionStore : IOwnedInstructionStore
{
    private readonly WindowsOwnedDefinitionFileAccess files;

    public WindowsOwnedInstructionStore(IApplicationDataPaths paths, ArtifactKind kind = ArtifactKind.Instruction)
    {
        _ = OwnedDefinitionKind.DirectoryName(kind);
        files = new(paths, kind);
    }

    internal Action? AfterFinalComparison { get => files.AfterFinalComparison; set => files.AfterFinalComparison = value; }
    internal Action? AfterPublication { get => files.AfterPublication; set => files.AfterPublication = value; }
    public Task<IReadOnlyList<string>> List(CancellationToken token) => files.List(token);
    internal IReadOnlyList<string> ListVerified(CancellationToken token) => files.ListVerified(token);
    public async Task<OwnedInstructionSnapshot?> Read(string name, CancellationToken token) =>
        Convert(await files.Read(name, token).ConfigureAwait(false));
    internal OwnedInstructionSnapshot? ReadVerified(string name, CancellationToken token) => Convert(files.ReadVerified(name, token));
    public async Task<InstructionWriteResult> Write(string name, OwnedInstructionSnapshot? expected,
        ArtifactFileDocument? replacement, Func<bool> eligible,
        Func<Func<InstructionWriteResult>, Task<InstructionWriteResult>> commit, CancellationToken token) =>
        Convert(await files.Write(name, Convert(expected), replacement, eligible,
            async operation => Convert(await commit(() => Convert(operation())).ConfigureAwait(false)), token).ConfigureAwait(false));
    internal Task<InstructionWriteResult> Write(string name, OwnedInstructionSnapshot? expected,
        ArtifactFileDocument? replacement, Func<bool> eligible, CancellationToken token) =>
        Write(name, expected, replacement, eligible, operation => Task.FromResult(operation()), token);

    private static OwnedInstructionSnapshot? Convert(WindowsOwnedDefinitionFileAccess.Snapshot? value) =>
        value is null ? null : new(value.Name, value.DirectoryIdentity, value.FileIdentity, value.Document)
        { CanonicalFileName = value.CanonicalFileName };
    private static WindowsOwnedDefinitionFileAccess.Snapshot? Convert(OwnedInstructionSnapshot? value) =>
        value is null ? null : new(value.Name, value.DirectoryIdentity, value.FileIdentity, value.Document, value.CanonicalFileName);
    private static InstructionWriteResult Convert(WindowsOwnedDefinitionFileAccess.WriteResult value) =>
        new(value.Published, value.Deleted, value.Conflict, Convert(value.Current), value.Unchanged);
    private static WindowsOwnedDefinitionFileAccess.WriteResult Convert(InstructionWriteResult value) =>
        new(value.Published, value.Deleted, value.Conflict, Convert(value.Current), value.Unchanged);
}

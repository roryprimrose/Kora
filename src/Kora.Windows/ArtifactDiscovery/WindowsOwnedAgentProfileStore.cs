using Kora.Core.Agents;
using Kora.Core.Artifacts;
using Kora.Core.Dependencies;

namespace Kora.Windows.Artifacts;

public sealed class WindowsOwnedAgentProfileStore(IApplicationDataPaths paths) : IOwnedAgentProfileStore
{
    private readonly WindowsOwnedDefinitionFileAccess files = new(paths, ArtifactKind.Agent);
    internal Action? AfterFinalComparison { get => files.AfterFinalComparison; set => files.AfterFinalComparison = value; }
    internal Action? AfterPublication { get => files.AfterPublication; set => files.AfterPublication = value; }

    public Task<IReadOnlyList<string>> List(CancellationToken token) => files.List(token);
    public Task<OwnedAgentProfileDestination> PrepareNew(string name, CancellationToken token) =>
        files.PrepareAgentDestination(name, token);

    public async Task<OwnedAgentProfileSnapshot?> Read(string name, CancellationToken token)
    {
        OwnedInstructionNames.WriteName(name);
        var value = await files.Read(name, token).ConfigureAwait(false);
        return value is null ? null : Snapshot(value);
    }

    public async Task<AgentProfileWriteResult> Create(OwnedAgentProfileDestination destination,
        OwnedAgentProfileDocument document, Func<bool> eligible,
        Func<Func<AgentProfileWriteResult>, Task<AgentProfileWriteResult>> commit, CancellationToken token)
    {
        if (!string.Equals(document.Name, destination.Name, StringComparison.Ordinal))
        { throw new InvalidDataException("The proposal and destination differ."); }
        var current = await PrepareNew(destination.Name, token).ConfigureAwait(false);
        if (current != destination) { throw new InvalidOperationException("The reviewed owned root changed."); }
        return await Write(destination.Name, null, document, eligible, commit, token).ConfigureAwait(false);
    }

    public Task<AgentProfileWriteResult> Update(OwnedAgentProfileSnapshot expected,
        OwnedAgentProfileDocument document, Func<bool> eligible,
        Func<Func<AgentProfileWriteResult>, Task<AgentProfileWriteResult>> commit, CancellationToken token)
    {
        var destination = expected.Destination;
        OwnedInstructionNames.WriteName(destination.Name);
        if (!string.Equals(document.Name, destination.Name, StringComparison.Ordinal)
            || !string.Equals(expected.Document.Name, destination.Name, StringComparison.Ordinal)
            || !string.Equals(destination.DisplayPath,
                Path.Combine(paths.RoamingRoot, "OwnedAgents-v1", destination.Name + ".agent.md"), StringComparison.Ordinal))
        { throw new InvalidDataException("The reviewed original is not this exact canonical owned destination."); }
        var original = new WindowsOwnedDefinitionFileAccess.Snapshot(destination.Name, destination.DirectoryIdentity,
            expected.FileIdentity, expected.Document.Document, destination.Name + ".agent.md")
        { RootBindingIdentity = destination.RootBindingIdentity };
        return Write(destination.Name, original, document, eligible, commit, token);
    }

    private async Task<AgentProfileWriteResult> Write(string name, WindowsOwnedDefinitionFileAccess.Snapshot? expected,
        OwnedAgentProfileDocument document, Func<bool> eligible,
        Func<Func<AgentProfileWriteResult>, Task<AgentProfileWriteResult>> commit, CancellationToken token)
    {
        AgentProfileWriteResult? completed = null;
        try
        {
            _ = await files.Write(name, expected, document.Document, eligible,
                async operation =>
                {
                    var admitted = await commit(() => Convert(operation())).ConfigureAwait(false);
                    completed = admitted;
                    return new WindowsOwnedDefinitionFileAccess.WriteResult(admitted.State == AgentProfileWriteState.Saved, false,
                        admitted.State == AgentProfileWriteState.Conflict, null);
                }, token).ConfigureAwait(false);
            return completed ?? throw new IOException("The native publication result is unavailable.");
        }
        catch (InstructionPublicationUnconfirmedException exception)
        { throw new AgentProfilePublicationUnconfirmedException(exception); }
    }

    private AgentProfileWriteResult Convert(WindowsOwnedDefinitionFileAccess.WriteResult result) =>
        new(result.Conflict ? AgentProfileWriteState.Conflict
            : result.Published ? AgentProfileWriteState.Saved
            : result.Unchanged ? AgentProfileWriteState.Unchanged : AgentProfileWriteState.Cancelled,
            result.Current is null ? null : Snapshot(result.Current));

    private OwnedAgentProfileSnapshot Snapshot(WindowsOwnedDefinitionFileAccess.Snapshot value) =>
        new(new(value.Name, Path.Combine(paths.RoamingRoot, "OwnedAgents-v1", value.Name + ".agent.md"), value.DirectoryIdentity)
            { RootBindingIdentity = value.RootBindingIdentity },
            value.FileIdentity, OwnedAgentProfileDocument.Parse(value.Name, value.Document.CopyBytes()));
}

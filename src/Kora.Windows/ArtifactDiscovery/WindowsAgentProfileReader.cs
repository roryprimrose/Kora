using System.Security.AccessControl;
using System.Security.Principal;

using Kora.Core.Agents;
using Kora.Core.Context;
using Kora.Windows.Context;

namespace Kora.Windows.Artifacts;

public sealed class WindowsAgentProfileReader(WindowsLocalFileInspector inspector) : IAgentProfileReader
{
    public async Task<IAgentProfileSelection> OpenAsync(string path, CancellationToken token)
    {
        if (!path.EndsWith(".agent.md", StringComparison.Ordinal))
        { throw new InvalidDataException("Select an existing canonical .agent.md file on a local fixed drive."); }
        var selected = await inspector.InspectAsync(path, token).ConfigureAwait(false);
        try
        {
            RequireOwner(selected.Metadata);
            return new Selection(selected);
        }
        catch { selected.Dispose(); throw; }
    }

    private static void RequireOwner(LocalFileMetadata metadata)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var owner = new FileInfo(metadata.CanonicalPath).GetAccessControl(AccessControlSections.Owner)
            .GetOwner(typeof(SecurityIdentifier));
        if (identity.User is null || !identity.User.Equals(owner))
        { throw new InvalidDataException("The selected agent source must belong to the current Windows user."); }
    }

    private sealed class Selection(ILocalFileSelection selected) : IAgentProfileSelection
    {
        public LocalFileMetadata Metadata => selected.Metadata;
        public async Task<byte[]> ReadAsync(CancellationToken token)
        {
            RequireOwner(Metadata);
            var bytes = await selected.ReadAsync(token).ConfigureAwait(false);
            RequireOwner(Metadata);
            return bytes;
        }
        public void Dispose() => selected.Dispose();
    }
}

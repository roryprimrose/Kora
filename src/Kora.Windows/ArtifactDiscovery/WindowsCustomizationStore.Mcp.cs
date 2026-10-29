using System.Security.Cryptography;
using System.Text;

using Kora.Core.Artifacts;
using Kora.Core.Customizations;
using Kora.Windows.Storage;

namespace Kora.Windows.Artifacts;

public sealed partial class WindowsCustomizationStore
{
    private sealed record McpCapture(string Anchor, string Binding, DiskState State, string FileRevision, string MetadataRevision)
    {
        internal string Revision => Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(Anchor + "\0" + Binding + "\0" + State.Revision + "\0" + FileRevision)));
    }

    internal Action? BeforeMcpFinalComparison { get; set; }
    internal Action? AfterMcpFinalComparison { get; set; }

    private void RequireOwnedMcp(Target target)
    {
        var expected = Path.GetFullPath(Path.Combine(paths.RoamingRoot, "Mcp", "mcp.json"));
        if (target.Source.Scope != CustomizationScope.Kora
            || !string.Equals(target.Source.Location, expected, StringComparison.Ordinal)
            || !string.Equals(target.Entry, target.Source.Name, StringComparison.Ordinal))
        { throw new InvalidDataException("Only one exact entry in Kora's host-selected canonical MCP file may be edited."); }
        OwnedInstructionNames.WriteName(target.Source.Name);
        if (File.Exists(JournalPath(target)))
        { throw new InvalidOperationException("This entry has a legacy unfinished transaction. Keep it excluded and reconcile it explicitly; no raw shared-file recovery is exposed."); }
    }

    private McpCapture CaptureMcp(Target target, CancellationToken token)
    {
        RequireOwnedMcp(target);
        var anchor = target.Directory;
        while (!Directory.Exists(anchor))
        {
            if (File.Exists(anchor)) { throw new InvalidDataException("An MCP parent is occupied by a file."); }
            anchor = Path.GetDirectoryName(anchor) ?? throw new InvalidDataException("The MCP source has no existing local parent.");
        }
        using var parent = WindowsOwnedDefinitionFileAccess.PinDefinitionDirectory(anchor, false, token);
        WindowsOwnedDefinitionFileAccess.RequireExactDefinitionDirectory(parent, anchor);
        if (!string.Equals(anchor, target.Directory, StringComparison.Ordinal))
        { return new(anchor, parent.BindingIdentity, new(parent.Identity, []), string.Empty, string.Empty); }
        var first = WindowsOwnedDefinitionFileAccess.ReadManagedDefinitionFile(target.Source.Location, 256 * 1024, token);
        var second = WindowsOwnedDefinitionFileAccess.ReadManagedDefinitionFile(target.Source.Location, 256 * 1024, token);
        if (!string.Equals(first?.Revision, second?.Revision, StringComparison.Ordinal))
        { throw new IOException("The MCP source was unstable during capture. No source was disclosed or saved."); }
        return new(anchor, parent.BindingIdentity,
            new(parent.Identity, second is null ? [] : [new(target.FileName, second.File.Bytes, second.File.Identity)]),
            second?.Revision ?? string.Empty, second?.MetadataRevision ?? string.Empty);
    }

    private static CustomizationSnapshot ExposeMcp(Target target, McpCapture captured)
    {
        if (captured.State.Files.Count == 0)
        { throw new InvalidOperationException("The selected MCP file is missing. Start an explicit new-entry operation instead."); }
        var bytes = new McpDefinitionDocument(captured.State.Files.Single().Bytes).ReadHttps(target.Entry!);
        return new(target.Source, captured.Revision, [new(target.Source.PrimaryFile, bytes)]);
    }

    private WindowsOwnedDefinitionFileAccess.DefinitionDirectory CreateMcpParents(Target target, McpCapture expected,
        Func<bool> eligible, CancellationToken token)
    {
        using var anchor = WindowsOwnedDefinitionFileAccess.PinDefinitionDirectory(expected.Anchor, false, token);
        if (!string.Equals(anchor.BindingIdentity, expected.Binding, StringComparison.Ordinal))
        { throw new InvalidOperationException("The reviewed MCP parent binding changed."); }
        var held = new List<WindowsOwnedDefinitionFileAccess.DefinitionDirectory>();
        try
        {
            var current = expected.Anchor;
            foreach (var component in Path.GetRelativePath(expected.Anchor, target.Directory).Split('\\'))
            {
                RequireEligible(eligible, token);
                current = Path.Combine(current, component);
                using var owner = System.Security.Principal.WindowsIdentity.GetCurrent();
                RestrictedStorageDirectory.CreateRestrictedDirectory(current,
                    owner.User ?? throw new InvalidOperationException("MCP storage ownership is unknown."));
                held.Add(WindowsOwnedDefinitionFileAccess.PinDefinitionDirectory(current, false, token));
            }
            return WindowsOwnedDefinitionFileAccess.PinDefinitionDirectory(target.Directory, false, token);
        }
        catch (System.ComponentModel.Win32Exception exception)
        { throw new IOException("An MCP parent could not be created exclusively. Inspect occupancy and review again; no existing directory was adopted or repaired.", exception); }
        finally { foreach (var parent in held) { parent.Dispose(); } }
    }

    private CustomizationSnapshot PublishMcp(Target target, CustomizationSnapshot? expected,
        CustomizationFile[] files, Func<bool> eligible, CancellationToken token)
    {
        RequireOwnedMcp(target);
        var current = CaptureMcp(target, token);
        var expectedRevision = expected is null ? target.McpCreationRevision : expected.Revision;
        if (expected is not null && (expected.Source != target.Source || expected.RecoveryDetails is not null)
            || expectedRevision is null || !string.Equals(expectedRevision, current.Revision, StringComparison.Ordinal))
        { throw new InvalidOperationException("The complete reviewed MCP source or parent changed. Reload and review; nothing was overwritten."); }
        var original = current.State.Files.Count == 0 ? "{\n  \"servers\": {}\n}\n"u8.ToArray() : current.State.Files.Single().Bytes;
        var document = new McpDefinitionDocument(original);
        if ((expected is null) == document.Names.Contains(target.Entry!, StringComparer.Ordinal))
        { throw new InvalidOperationException("The reviewed MCP entry appeared or disappeared. Select it again; nothing was overwritten."); }
        var proposed = document.ReplaceHttps(target.Entry!, files.Single().CopyBytes());
        RequireEligible(eligible, token);
        using var parent = string.Equals(current.Anchor, target.Directory, StringComparison.Ordinal)
            ? WindowsOwnedDefinitionFileAccess.PinDefinitionDirectory(target.Directory, false, token)
            : CreateMcpParents(target, current, eligible, token);
        if (!string.Equals(current.Anchor, target.Directory, StringComparison.Ordinal))
        {
            current = CaptureMcp(target, token);
            if (current.State.Files.Count != 0)
            { throw new InvalidOperationException("The newly created MCP destination became occupied. No file was replaced."); }
        }
        var pinnedRevision = current.Revision;
        string? stagedMetadata = null;
        var published = false;
        try
        {
            var identity = WindowsOwnedDefinitionFileAccess.WriteDefinitionFile(target.Directory, parent, target.FileName,
                proposed, current.State.Files.Count != 0, eligible, token, () =>
                {
                    BeforeMcpFinalComparison?.Invoke();
                    var final = CaptureMcp(target, token);
                    if (!string.Equals(final.Revision, pinnedRevision, StringComparison.Ordinal))
                    { throw new InvalidOperationException("The MCP base changed at final native comparison. Reload and review; no replacement was made."); }
                    AfterMcpFinalComparison?.Invoke();
                }, current.State.Files.Count == 0 ? null : target.Source.Location, () => published = true,
                metadata => stagedMetadata = metadata);
            AfterComponentPublished?.Invoke(target.FileName);
            RequireEligible(eligible, token);
            var readback = CaptureMcp(target, token);
            var actual = readback.State.Files.Single();
            if (!string.Equals(readback.Binding, parent.BindingIdentity, StringComparison.Ordinal)
                || !string.Equals(actual.Identity, identity, StringComparison.Ordinal)
                || !string.Equals(readback.MetadataRevision, stagedMetadata, StringComparison.Ordinal)
                || !actual.Bytes.AsSpan().SequenceEqual(proposed))
            { throw new IOException("The exact MCP publication readback is unconfirmed."); }
            return ExposeMcp(target, readback);
        }
        catch (Exception exception) when (published && exception is not OutOfMemoryException)
        { throw new IOException("MCP publication may have occurred but is unconfirmed. Keep this server disabled and excluded; inspect the current entry and start a fresh review. No rollback or automatic retry is claimed.", exception); }
    }
}

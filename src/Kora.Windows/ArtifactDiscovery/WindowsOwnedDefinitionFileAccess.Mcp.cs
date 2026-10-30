using System.Runtime.InteropServices;
using System.Security.Cryptography;

using Kora.Windows.Coordination;
using Kora.Windows.Storage;

using Microsoft.Win32.SafeHandles;

namespace Kora.Windows.Artifacts;

internal sealed partial class WindowsOwnedDefinitionFileAccess
{
    internal sealed record ManagedDefinitionFile(DefinitionFile File, string Revision, string MetadataRevision);

    internal static void RequireExactDefinitionDirectory(DefinitionDirectory directory, string path)
    {
        if (!string.Equals(CoordinationNative.CanonicalPath(directory.Handle).TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.Ordinal))
        { throw new InvalidDataException("The canonical MCP source parent is a case or path alias."); }
    }

    internal static ManagedDefinitionFile? ReadManagedDefinitionFile(string path, int maximumBytes, CancellationToken token)
    {
        var file = ReadDefinitionFile(path, maximumBytes, token);
        if (file is null) { return null; }
        using var handle = OpenInstructionOriginalForReplacement(path);
        if (!string.Equals(file.Identity, WindowsFileIdentity.Read(handle), StringComparison.Ordinal))
        { throw new IOException("The MCP file identity changed during capture."); }
        var metadata = DefinitionMetadataRevision(handle);
        var revision = file.Identity + ":" + file.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ":" + Convert.ToHexStringLower(SHA256.HashData(file.Bytes)) + ":" + metadata;
        return new(file, revision, metadata);
    }

    private static string DefinitionMetadataRevision(SafeFileHandle handle)
    {
        var status = GetSecurityInfo(handle, 1, 7, out _, out _, out _, out _, out var descriptor);
        if (status != 0) { throw new IOException("MCP source security metadata is unavailable."); }
        try
        {
            var security = new byte[GetSecurityDescriptorLength(descriptor)];
            Marshal.Copy(descriptor, security, 0, security.Length);
            var info = Info(handle);
            byte[] metadata = [.. security, .. BitConverter.GetBytes(info.Attributes),
                .. BitConverter.GetBytes(info.CreatedHigh), .. BitConverter.GetBytes(info.CreatedLow)];
            return Convert.ToHexStringLower(SHA256.HashData(metadata));
        }
        finally { LocalFree(descriptor); }
    }

    [LibraryImport("advapi32.dll")]
    private static partial uint GetSecurityDescriptorLength(nint descriptor);
}

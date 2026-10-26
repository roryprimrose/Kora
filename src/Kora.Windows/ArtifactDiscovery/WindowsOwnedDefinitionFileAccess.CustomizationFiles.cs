using System.Runtime.InteropServices;
using System.Security.Principal;

using Kora.Core.Customizations;
using Kora.Windows.Storage;

using Microsoft.Win32.SafeHandles;

namespace Kora.Windows.Artifacts;

internal sealed partial class WindowsOwnedDefinitionFileAccess
{
    internal sealed class DefinitionDirectory : IDisposable
    {
        private readonly Pinned pinned;
        internal DefinitionDirectory(Pinned pinned) => this.pinned = pinned;
        internal SafeFileHandle Handle => pinned.Last;
        internal string Identity => WindowsFileIdentity.Read(Handle);
        internal string BindingIdentity => pinned.BindingIdentity;
        public void Dispose() => pinned.Dispose();
    }

    internal static DefinitionDirectory PinDefinitionDirectory(string path, bool create, CancellationToken token)
    {
        path = Path.GetFullPath(path).TrimEnd('\\');
        if (path.Length < 3 || path[1] != ':' || path[2] != '\\'
            || GetDriveType(path[..3]) != 3 || Protected(path))
        { throw new InvalidDataException("Definition editing requires an unprotected fixed local source."); }
        var pinned = new Pinned();
        try
        {
            var current = path[..3];
            foreach (var component in new[] { string.Empty }.Concat(path[3..].Split('\\')))
            {
                token.ThrowIfCancellationRequested();
                if (component.Length > 0) { current = Path.Combine(current, component); }
                var handle = CreateFile(current, 0x200A0, 3, nint.Zero, 3, 0x02200000, nint.Zero);
                var error = Marshal.GetLastWin32Error();
                if (handle.IsInvalid && error is 2 or 3 && create)
                {
                    handle.Dispose();
                    using var owner = WindowsIdentity.GetCurrent();
                    RestrictedStorageDirectory.CreateRestrictedDirectory(current,
                        owner.User ?? throw new InvalidOperationException("Definition storage ownership is unknown."));
                    handle = CreateFile(current, 0x200A0, 3, nint.Zero, 3, 0x02200000, nint.Zero);
                }
                pinned.Add(handle);
                VerifyDirectory(handle, current);
            }
            VerifyOwner(pinned.Last);
            return new(pinned);
        }
        catch { pinned.Dispose(); throw; }
    }

    internal sealed record DefinitionFile(byte[] Bytes, string Identity);

    internal static DefinitionFile? ReadDefinitionFile(string path, int maximumBytes, CancellationToken token)
    {
        using var handle = CreateFile(path, 0x80000000, 5, nint.Zero, 3, 0x02200000, nint.Zero);
        if (handle.IsInvalid)
        {
            if (Marshal.GetLastWin32Error() == 2) { return null; }
            throw NativeFailure("The definition component is inaccessible.");
        }
        VerifyFile(handle, path);
        var length = RandomAccess.GetLength(handle);
        if (length < 0 || length > maximumBytes) { throw new InvalidDataException("The definition component exceeds its byte bound."); }
        var bytes = new byte[(int)length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            token.ThrowIfCancellationRequested();
            var count = RandomAccess.Read(handle, bytes.AsSpan(offset), offset);
            if (count == 0) { throw new IOException("The definition component changed during capture."); }
            offset += count;
        }
        return new(bytes, WindowsFileIdentity.Read(handle));
    }

    internal static string WriteDefinitionFile(string directory, DefinitionDirectory pinned, string name,
        byte[] bytes, bool replace, Func<bool> eligible, CancellationToken token,
        Action? finalComparison = null, string? preserveSecurityFrom = null, Action? publicationCommitted = null)
    {
        CustomizationFile.ValidateName(name);
        if (name.Contains('\\', StringComparison.Ordinal))
        { throw new InvalidDataException("Publication must be relative to the pinned immediate parent."); }
        var temporary = Path.Combine(directory, $".customization-{Guid.NewGuid():N}.tmp");
        var published = false;
        try
        {
            using var original = preserveSecurityFrom is null ? null : OpenInstructionOriginalForReplacement(preserveSecurityFrom);
            using var staged = CreatePrivateStaging(temporary);
            VerifyFile(staged, temporary);
            RandomAccess.Write(staged, bytes, 0);
            RandomAccess.FlushToDisk(staged);
            if (original is not null) { PreserveInstructionSecurity(original, staged); }
            finalComparison?.Invoke();
            RequireEligible(eligible, token);
            Rename(staged, pinned.Handle, name, replace, token);
            published = true;
            publicationCommitted?.Invoke();
            return WindowsFileIdentity.Read(staged);
        }
        finally { if (!published && File.Exists(temporary)) { File.Delete(temporary); } }
    }

    internal static void DeleteDefinitionFile(string path, Func<bool> eligible, CancellationToken token)
    {
        using var target = OpenFile(path, 0x80010000, 1);
        RequireEligible(eligible, token);
        if (!SetFileInformationByHandle(target, 4, [1], 1))
        { throw NativeFailure("The definition component deletion is unconfirmed."); }
    }
}
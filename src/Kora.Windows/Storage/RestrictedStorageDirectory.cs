using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

using Kora.Core.Dependencies;

namespace Kora.Windows.Storage;

internal sealed partial class RestrictedStorageDirectory
{
    internal const string PartitionName = "DurableStorageV1";
    private readonly SecurityIdentifier user;
    private readonly string localRoot;
    private readonly bool includeKeys;

    internal RestrictedStorageDirectory(IApplicationDataPaths paths, bool includeKeys = true, string? partitionName = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Durable storage requires Windows.");
        }

        using var identity = WindowsIdentity.GetCurrent();
        user = identity.User ?? throw new InvalidOperationException("The current Windows profile is unavailable.");
        if (string.IsNullOrWhiteSpace(paths.LocalRoot))
        {
            throw new InvalidDataException("The supplied local storage root is missing.");
        }
        if (!Path.IsPathFullyQualified(paths.LocalRoot) || paths.LocalRoot.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Durable storage requires a supplied absolute local path.");
        }

        localRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(paths.LocalRoot));
        var volume = Path.GetPathRoot(localRoot) ?? throw new InvalidDataException("The storage volume is unknown.");
        if (new DriveInfo(volume).DriveType != DriveType.Fixed
            || string.Equals(localRoot, volume, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Durable storage requires a local fixed-volume application directory.");
        }

        this.includeKeys = includeKeys;
        var partition = partitionName ?? (includeKeys ? PartitionName : "HostStorageV1");
        if (string.IsNullOrWhiteSpace(partition) || partition is "." or ".."
            || partition.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("A partition must be a single local directory name.", nameof(partitionName));
        }
        Root = Path.Combine(localRoot, partition);
        Keys = Path.Combine(Root, "Keys");
        Artifacts = Path.Combine(Root, "Artifacts");
    }

    internal string Root { get; }
    internal string Keys { get; }
    internal string Artifacts { get; }

    internal void CreateNew()
    {
        VerifyLocalRoot();
        CreateRestrictedDirectory(Root, user);
        VerifyDirectory(Root);
        if (includeKeys)
        {
            CreateRestrictedDirectory(Keys, user);
        }
        CreateRestrictedDirectory(Artifacts, user);
        Verify();
    }

    internal void Verify()
    {
        VerifyLocalRoot();
        VerifyDirectory(Root);
        if (includeKeys)
        {
            VerifyDirectory(Keys);
        }
        VerifyDirectory(Artifacts);
    }

    internal FileStream AcquireLease() => AcquireBoundedLease(requireExisting: false, CancellationToken.None);

    internal FileStream AcquireBoundedLease(bool requireExisting, CancellationToken cancellationToken)
    {
        Verify();
        var path = Path.Combine(Root, "operation.lock");
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return OpenLease(path, requireExisting);
            }
            catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33)
            {
                if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(5))
                {
                    throw new IOException("The private storage admission lease timed out.", exception);
                }
                // This is bounded blocking admission, not a queued background writer.
                if (cancellationToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(20)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
        }
    }

    private FileStream OpenLease(string path, bool requireExisting)
    {
        var exists = EntryExists(path);
        if (!exists && requireExisting)
        {
            throw new InvalidDataException("An existing storage partition is missing its admission lease.");
        }
        if (exists)
        {
            VerifyFile(path);
        }
        var lease = exists
            ? new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
            : CreateNewFile(path);
        var admitted = false;
        try
        {
            VerifyFile(path);
            admitted = true;
            return lease;
        }
        finally
        {
            if (!admitted)
            {
                lease.Dispose();
            }
        }
    }

    internal void VerifyFile(string path)
    {
        VerifyOwnedPath(path);
        RejectReparseAncestors(path);
        var info = new FileInfo(path);
        if (info.Attributes.HasFlag(FileAttributes.Directory))
        {
            throw new InvalidDataException("A managed storage file is a directory.");
        }
        VerifyPermissions(info.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner),
            requireProtected: true, allowSystemAdministrators: false);
    }

    internal FileStream CreateNewFile(string path, FileOptions options = FileOptions.None)
    {
        Verify();
        VerifyOwnedPath(path);
        RejectReparseAncestors(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var security = new FileSecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        // Elevated tokens may default to Administrators ownership; specify the user before any bytes are written.
        var stream = new FileInfo(path).Create(FileMode.CreateNew,
            FileSystemRights.Read | FileSystemRights.Write, FileShare.None, 4096, options, security);
        var admitted = false;
        try
        {
            VerifyFile(path);
            admitted = true;
            return stream;
        }
        finally
        {
            if (!admitted)
            {
                stream.Dispose();
            }
        }
    }

    private void VerifyOwnedPath(string path)
    {
        if (!Path.GetFullPath(path).StartsWith(string.Concat(Root, Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("A managed storage file is outside its owned partition.");
        }
    }

    internal static void CreateRestrictedDirectory(string path, SecurityIdentifier owner)
    {
        var security = new DirectorySecurity();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        var descriptor = security.GetSecurityDescriptorBinaryForm();
        var memory = Marshal.AllocHGlobal(descriptor.Length);
        try
        {
            Marshal.Copy(descriptor, 0, memory, descriptor.Length);
            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = memory,
            };
            // CREATE_NEW semantics: never repair or change an existing user's directory.
            if (!CreateDirectory(path, ref attributes))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "A new restricted storage directory could not be created.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    private void VerifyLocalRoot()
    {
        RejectReparseAncestors(localRoot);
        VerifyPermissions(new DirectoryInfo(localRoot).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner),
            requireProtected: false, allowSystemAdministrators: true);
    }

    private void VerifyDirectory(string path)
    {
        RejectReparseAncestors(path);
        VerifyPermissions(new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner),
            requireProtected: true, allowSystemAdministrators: false);
    }

    private void VerifyPermissions(FileSystemSecurity security, bool requireProtected, bool allowSystemAdministrators)
    {
        if (!user.Equals(security.GetOwner(typeof(SecurityIdentifier)))
            || requireProtected && !security.AreAccessRulesProtected)
        {
            throw new UnauthorizedAccessException("The storage owner or ACL protection is invalid.");
        }

        var hasUserFullControl = false;
        foreach (var rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
        {
            if (rule is not FileSystemAccessRule access || access.IdentityReference is not SecurityIdentifier sid
                || access.AccessControlType != AccessControlType.Allow)
            {
                throw new UnauthorizedAccessException("The effective storage ACL is not recognized.");
            }
            var isUser = user.Equals(sid);
            var isTrustedSystem = allowSystemAdministrators
                && (sid.IsWellKnown(WellKnownSidType.LocalSystemSid)
                    || sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid));
            if (!isUser && !isTrustedSystem)
            {
                throw new UnauthorizedAccessException("The storage ACL grants access outside the current profile.");
            }
            hasUserFullControl |= isUser
                && (access.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl
                && (access.PropagationFlags & PropagationFlags.InheritOnly) == PropagationFlags.None;
        }

        // Inspect all actual ACEs, including inheritance, rather than trusting a path or a named rule.
        if (!hasUserFullControl)
        {
            throw new UnauthorizedAccessException("The current profile lacks effective storage control.");
        }
    }

    private static void RejectReparseAncestors(string path)
    {
        string? current = path;
        while (current is not null)
        {
            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new UnauthorizedAccessException("Reparse points are not admitted in managed storage paths.");
            }
            current = Path.GetDirectoryName(current);
        }
    }

    private static bool EntryExists(string path)
    {
        try
        {
            // File.Exists hides access errors and may report dangling links as absent.
            _ = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        internal int Length;
        internal IntPtr SecurityDescriptor;
        internal int InheritHandle;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateDirectoryW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateDirectory(string path, ref SecurityAttributes attributes);
}

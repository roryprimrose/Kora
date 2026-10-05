using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

using Kora.Core.Coordination;

using Microsoft.Win32.SafeHandles;

namespace Kora.Windows.Coordination;

internal sealed class VerifiedInstanceProcess : IDisposable
{
    private readonly SafeProcessHandle process;
    private readonly List<FileStream> images;

    private VerifiedInstanceProcess(SafeProcessHandle process, List<FileStream> images, InstanceProcessIdentity identity)
    {
        this.process = process;
        this.images = images;
        Identity = identity;
    }

    internal InstanceProcessIdentity Identity { get; }

    internal bool IsAlive => CoordinationNative.WaitForSingleObject(process, 0) == 258;

    internal static VerifiedInstanceProcess Open(int processId, string expectedSid, int expectedSession)
    {
        var handle = CoordinationNative.OpenProcess(
            CoordinationNative.QueryLimitedInformation | CoordinationNative.Synchronize, false, processId);
        var files = new List<FileStream>();
        try
        {
            if (handle.IsInvalid)
            {
                throw new Win32Exception();
            }

            var (sid, session) = CoordinationNative.ReadProcessToken(handle);
            if (!string.Equals(sid, expectedSid, StringComparison.Ordinal) || session != expectedSession)
            {
                throw new InvalidOperationException("The peer is not in this user's authoritative interactive session.");
            }

            if (!CoordinationNative.GetProcessTimes(handle, out var creation, out _, out _, out _))
            {
                throw new Win32Exception();
            }

            var path = CoordinationNative.ReadImagePath(handle);
            var build = ReadBuild(path, files);
            var peer = new VerifiedInstanceProcess(handle, files,
                new InstanceProcessIdentity(sid, session, processId, creation, build));
            if (!peer.IsAlive)
            {
                throw new InvalidOperationException("The peer has exited.");
            }

            return peer;
        }
        catch
        {
            handle.Dispose();
            foreach (var file in files)
            {
                file.Dispose();
            }

            throw;
        }
    }

    internal static InstanceBuildIdentity ReadBuild(string imagePath, List<FileStream> files)
    {
        var elapsed = Stopwatch.StartNew();
        var image = OpenImage(imagePath, files);
        var executable = CoordinationNative.CanonicalPath(image.SafeFileHandle);
        if (!Path.GetFileName(executable).Equals("Kora.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only the Kora application host has a supported launch identity.");
        }

        var directory = Path.GetDirectoryName(executable)!;
        var assembly = OpenImage(Path.Combine(directory, "Kora.dll"), files, directory);
        using var pe = new PEReader(assembly, PEStreamOptions.LeaveOpen);
        if (!pe.HasMetadata)
        {
            throw new InvalidOperationException("The executable's build identity cannot be proven.");
        }

        var metadata = pe.GetMetadataReader();
        var definition = metadata.GetAssemblyDefinition();
        if (!metadata.GetString(definition.Name).Equals("Kora", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The executable is not a supported Kora assembly.");
        }

        var debug = false;
        var debugKnown = false;
        var buildId = definition.Version.ToString();
        foreach (var attributeHandle in definition.GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(attributeHandle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference)
            {
                continue;
            }

            var member = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (member.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            var type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
            var name = metadata.GetString(type.Name);
            var blob = metadata.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1)
            {
                throw new InvalidOperationException("Unsupported assembly identity metadata.");
            }

            if (string.Equals(name, "DebuggableAttribute", StringComparison.Ordinal) &&
                string.Equals(metadata.GetString(type.Namespace), "System.Diagnostics", StringComparison.Ordinal))
            {
                var flags = blob.ReadInt32();
                debug = (flags & 0x100) != 0;
                debugKnown = true;
            }
            else if (string.Equals(name, "AssemblyInformationalVersionAttribute", StringComparison.Ordinal) &&
                string.Equals(metadata.GetString(type.Namespace), "System.Reflection", StringComparison.Ordinal))
            {
                buildId = blob.ReadSerializedString() ?? buildId;
            }
        }

        if (!debugKnown || buildId.Length > 512)
        {
            throw new InvalidOperationException("Release/debug identity cannot be proven.");
        }

        var names = EnumerateLibraries(directory)
            .Concat([executable, Path.Combine(directory, "Kora.deps.json"), Path.Combine(directory, "Kora.runtimeconfig.json")])
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (names.Length > 512)
        {
            throw new InvalidOperationException("The application closure exceeds identity limits.");
        }

        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        var totalBytes = 0L;
        foreach (var filePath in names)
        {
            var file = files.FirstOrDefault(item => item.Name.Equals(filePath, StringComparison.OrdinalIgnoreCase))
                ?? OpenImage(filePath, files, directory);
            if (file.Length > 512L * 1024 * 1024)
            {
                throw new InvalidOperationException("An application image exceeds identity limits.");
            }

            digest.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(directory, filePath).ToUpperInvariant()));
            digest.AppendData(BitConverter.GetBytes(file.Length));
            file.Position = 0;
            int count;
            while ((count = file.Read(buffer)) != 0)
            {
                totalBytes += count;
                if (totalBytes > 1024L * 1024 * 1024 || elapsed.Elapsed > TimeSpan.FromSeconds(10))
                {
                    throw new InvalidOperationException("The application identity could not be proven within bounded limits.");
                }

                digest.AppendData(buffer.AsSpan(0, count));
            }
        }

        return new InstanceBuildIdentity(definition.Version.ToString(), buildId,
            Convert.ToHexString(digest.GetHashAndReset()), debug, executable, directory);
    }

    private static IEnumerable<string> EnumerateLibraries(string root)
    {
        var directories = new Queue<(string Path, int Depth)>();
        directories.Enqueue((root, 0));
        var count = 0;
        while (directories.TryDequeue(out var directory))
        {
            if (++count > 256 || directory.Depth > 8)
            {
                throw new InvalidOperationException("The application directory closure exceeds identity limits.");
            }

            foreach (var path in Directory.EnumerateFileSystemEntries(directory.Path))
            {
                var attributes = File.GetAttributes(path);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidOperationException("Redirected application images cannot be proven.");
                }

                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    directories.Enqueue((path, directory.Depth + 1));
                }
                else if (Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    yield return path;
                }
            }
        }
    }

    private static FileStream OpenImage(string path, List<FileStream> files, string? expectedDirectory = null)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        files.Add(stream);
        if (expectedDirectory is not null &&
            !CoordinationNative.CanonicalPath(stream.SafeFileHandle)
                .StartsWith(expectedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The application closure escapes the verified deployment directory.");
        }

        return stream;
    }

    internal void WaitForExit()
    {
        if (CoordinationNative.WaitForSingleObject(process, uint.MaxValue) != 0)
        {
            throw new InvalidOperationException("Replacement process lifetime monitoring failed.");
        }
    }

    public void Dispose()
    {
        process.Dispose();
        foreach (var image in images)
        {
            image.Dispose();
        }
    }
}

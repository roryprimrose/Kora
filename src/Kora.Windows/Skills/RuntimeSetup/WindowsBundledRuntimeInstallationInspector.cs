using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

using Kora.Windows.Storage;

namespace Kora.Windows.Skills.RuntimeSetup;

internal sealed class WindowsBundledRuntimeInstallationInspector : IBundledRuntimeInstallationInspector
{
    private const long MaximumBytes = 2L * 1024 * 1024 * 1024;
    private const int MaximumFiles = 10000;
    private static readonly string[] RequiredFiles = ["pwsh.exe", "pwsh.dll", "pwsh.deps.json", "pwsh.runtimeconfig.json",
        "System.Management.Automation.dll", "hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "System.Private.CoreLib.dll",
        "LICENSE.txt", "ThirdPartyNotices.txt"];
    private static readonly string[] AssetKinds = ["runtime", "native", "resources"];
    private readonly string root;
    private readonly IBundledRuntimeNativeMetadata metadata;

    internal WindowsBundledRuntimeInstallationInspector() : this(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7"),
        new WindowsBundledRuntimeNativeMetadata()) { }

    internal WindowsBundledRuntimeInstallationInspector(string root, IBundledRuntimeNativeMetadata metadata)
    { this.root = root; this.metadata = metadata; }

    public Task<BundledRuntimeInstallation?> InspectAsync(CancellationToken cancellationToken) =>
        Task.Run(() => InspectCoreAsync(cancellationToken), cancellationToken);

    private async Task<BundledRuntimeInstallation?> InspectCoreAsync(CancellationToken cancellationToken)
    {
        metadata.RequireSupportedPlatform();
        try { _ = File.GetAttributes(root); }
        catch (DirectoryNotFoundException) { return null; }
        catch (FileNotFoundException) { return null; }
        RestrictedStorageDirectory.RejectReparseAncestors(root);
        string? ancestor = root;
        while (ancestor is not null)
        {
            _ = metadata.InspectProtection(ancestor, directory: true, ancestorOnly: true);
            ancestor = Path.GetDirectoryName(ancestor);
        }
        var files = new List<BundledRuntimeFile>();
        var held = new List<FileStream>();
        var protections = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var directories = new Stack<string>();
        directories.Push(root);
        long bytes = 0;
        try
        {
            while (directories.TryPop(out var directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                RestrictedStorageDirectory.RejectReparseAncestors(directory);
                protections.Add(directory, metadata.InspectProtection(directory, directory: true, ancestorOnly: false));
                if (protections.Count > MaximumFiles * 2)
                { throw new InvalidDataException("The runtime directory inventory exceeds its inspection bound."); }
                foreach (var child in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var attributes = File.GetAttributes(child);
                    if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    { throw new InvalidDataException("A runtime installation contains a reparse point. No inventory or executable is admitted."); }
                    if (attributes.HasFlag(FileAttributes.Directory))
                    {
                        if (Path.GetRelativePath(root, child).Count(character => character == Path.DirectorySeparatorChar) >= 16)
                        { throw new InvalidDataException("The installed runtime directory depth exceeds its inspection bound."); }
                        directories.Push(child);
                        continue;
                    }
                    if (files.Count >= MaximumFiles)
                    { throw new InvalidDataException("The installed runtime exceeds its bounded complete inventory."); }
                    protections.Add(child, metadata.InspectProtection(child, directory: false, ancestorOnly: false));
                    var input = new FileStream(child, new FileStreamOptions
                    { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, Options = FileOptions.Asynchronous | FileOptions.SequentialScan });
                    held.Add(input);
                    bytes = checked(bytes + input.Length);
                    if (bytes > MaximumBytes)
                    { throw new InvalidDataException("The installed runtime exceeds its complete byte inspection bound."); }
                    var digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false));
                    files.Add(new(Path.GetRelativePath(root, child), input.Length, digest));
                }
            }
            var executable = Path.Combine(root, "pwsh.exe");
            foreach (var required in RequiredFiles)
            {
                if (!files.Any(file => string.Equals(file.Name, required, StringComparison.OrdinalIgnoreCase)))
                { throw new InvalidDataException("The complete required PowerShell payload, dependency declarations or licence notices are missing."); }
            }
            var executableStream = held.Single(file => string.Equals(file.Name, executable, StringComparison.OrdinalIgnoreCase));
            var version = metadata.InspectExecutable(executable, executableStream);
            var licence = await ReadNoticeAsync("LICENSE.txt", 64 * 1024, cancellationToken).ConfigureAwait(false);
            var notices = await ReadNoticeAsync("ThirdPartyNotices.txt", 2 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
            if (new FileInfo(Path.Combine(root, "pwsh.deps.json")).Length > 16 * 1024 * 1024)
            { throw new InvalidDataException("The runtime dependency declaration exceeds its inspection bound."); }
            using (var dependencies = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "pwsh.deps.json"), cancellationToken).ConfigureAwait(false)))
            {
                if (!dependencies.RootElement.TryGetProperty("runtimeTarget", out var target)
                    || !target.TryGetProperty("name", out var targetName)
                    || targetName.GetString() is not { } name || !name.EndsWith("/win-x64", StringComparison.Ordinal))
                { throw new InvalidDataException("The installed runtime dependency target is not an exact win-x64 closure."); }
                if (!dependencies.RootElement.TryGetProperty("targets", out var targets)
                    || !targets.TryGetProperty(name, out var packages) || packages.ValueKind != JsonValueKind.Object
                    || !packages.EnumerateObject().Any())
                { throw new InvalidDataException("The exact installed runtime target has no complete dependency declarations."); }
                foreach (var package in packages.EnumerateObject())
                {
                    if (package.Value.TryGetProperty("runtimeTargets", out var unresolved) && unresolved.EnumerateObject().Any())
                    { throw new InvalidDataException("Unresolved runtime-specific assets are not an admitted installed closure."); }
                    foreach (var kind in AssetKinds)
                    {
                        if (!package.Value.TryGetProperty(kind, out var assets)) { continue; }
                        foreach (var asset in assets.EnumerateObject())
                        {
                            if (asset.Name.EndsWith("/_._", StringComparison.Ordinal)) { continue; }
                            var relative = asset.Name.Replace('/', '\\');
                            if (Path.IsPathFullyQualified(relative) || relative.Split('\\').Any(segment => segment is "" or "." or "..")
                                || !files.Any(file => string.Equals(file.Name, relative, StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(file.Name, Path.GetFileName(relative), StringComparison.OrdinalIgnoreCase)))
                            { throw new InvalidDataException("A declared runtime/native/resource dependency is absent from the exact installed inventory. No external path is searched."); }
                        }
                    }
                }
            }
            if (new FileInfo(Path.Combine(root, "pwsh.runtimeconfig.json")).Length > 64 * 1024)
            { throw new InvalidDataException("The installed runtime configuration exceeds its inspection bound."); }
            using (var configuration = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "pwsh.runtimeconfig.json"), cancellationToken).ConfigureAwait(false)))
            {
                if (!configuration.RootElement.TryGetProperty("runtimeOptions", out var options)
                    || !options.TryGetProperty("tfm", out var framework)
                    || framework.GetString() is not { Length: > 0 })
                { throw new InvalidDataException("The installed PowerShell runtime framework declaration is missing."); }
            }
            var device = metadata.DeviceDigest();
            var ordered = files.OrderBy(file => file.Name, StringComparer.Ordinal).ToArray();
            var inventory = Hash(JsonSerializer.SerializeToUtf8Bytes(new
            {
                Executable = executable,
                Version = version,
                Files = ordered,
                Protections = protections,
                HostImplementation = typeof(WindowsBundledScriptWorker).Module.ModuleVersionId,
                Bootstrap = Hash(Encoding.UTF8.GetBytes(BundledPowerShellBootstrap.Source)),
            }));
            return new(executable, version, inventory, device, Array.AsReadOnly(ordered), licence, notices);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or DecoderFallbackException)
        { throw new InvalidDataException("The installed PowerShell dependency, framework or licence declaration is malformed.", exception); }
        finally
        {
            foreach (var input in held) { await input.DisposeAsync().ConfigureAwait(false); }
        }
    }

    private async Task<string> ReadNoticeAsync(string name, int maximumBytes, CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, name);
        if (new FileInfo(path).Length > maximumBytes)
        { throw new InvalidDataException("An installed runtime licence notice exceeds its review bound."); }
        return await File.ReadAllTextAsync(path, new UTF8Encoding(false, true), cancellationToken).ConfigureAwait(false);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal static void VerifyProtected(FileSystemSecurity security, bool ancestorOnly = false)
    {
        static bool Privileged(SecurityIdentifier sid) =>
            sid.IsWellKnown(WellKnownSidType.LocalSystemSid) || sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)
            || string.Equals(sid.Value, "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464", StringComparison.Ordinal);
        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !Privileged(owner))
        { throw new UnauthorizedAccessException("Runtime files must be independently system/administrator/TrustedInstaller owned, not user-owned."); }
        var mutation = FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles
            | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        if (!ancestorOnly) { mutation |= FileSystemRights.Write; }
        foreach (var rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)).OfType<FileSystemAccessRule>())
        {
            if (rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly)) { continue; }
            if (rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & mutation) != (FileSystemRights)0
                && rule.IdentityReference is SecurityIdentifier sid && !Privileged(sid))
            { throw new UnauthorizedAccessException("The installed runtime or an ancestor is mutable by an unprivileged identity."); }
        }
    }
}

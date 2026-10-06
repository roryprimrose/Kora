using System.Diagnostics;

namespace Kora.Windows.IntegrationTests.Storage;

internal static class OwnedStorageChildProcess
{
    private const string RootPrefix = ".kora-storage-test-";

    internal static ProcessStartInfo CreateStart(Type fixtureType, string methodName)
    {
        var assembly = typeof(OwnedStorageChildProcess).Assembly;
        if (fixtureType.Assembly != assembly
            || !string.Equals(fixtureType.Namespace, typeof(OwnedStorageChildProcess).Namespace, StringComparison.Ordinal)
            || fixtureType.GetMethod(methodName, Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException("Only an exact storage fixture method in this test assembly may run in a child.");
        }
        var executable = Path.GetFullPath(Path.ChangeExtension(assembly.Location, ".exe"));
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("The integration-test executable must be built before starting its owned child.", executable);
        }
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Directory.GetCurrentDirectory(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-method");
        start.ArgumentList.Add(fixtureType.FullName + "." + methodName);
        return start;
    }

    internal static void RequireOwnedRoot(string root)
    {
        var name = Path.GetFileName(root);
        if (!Path.IsPathFullyQualified(root)
            || !string.Equals(Path.GetDirectoryName(root),
                Path.TrimEndingDirectorySeparator(Directory.GetCurrentDirectory()), StringComparison.OrdinalIgnoreCase)
            || !name.StartsWith(RootPrefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(name.AsSpan(RootPrefix.Length), "N", out _))
        {
            throw new InvalidOperationException("The child must use its exact GUID-named scratch fixture in the test working directory.");
        }
        var attributes = File.GetAttributes(root);
        if (!attributes.HasFlag(FileAttributes.Directory) || attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException("The owned child fixture must be an existing non-reparse directory.");
        }
    }
}

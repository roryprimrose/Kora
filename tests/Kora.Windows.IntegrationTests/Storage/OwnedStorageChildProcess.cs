using System.Diagnostics;

using AwesomeAssertions;

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

    internal static void AssertHotJournal(string path)
    {
        using var journal = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        journal.Length.Should().BeGreaterThan(512);
        var header = new byte[8];
        journal.ReadExactly(header);
        header.Should().Equal([0xd9, 0xd5, 0x05, 0xf9, 0x20, 0xa1, 0x63, 0xd7],
            "a persistent journal's retained size alone does not prove a hot rollback header");
    }

    internal static void SignalAndBlock(string root, string marker)
    {
        File.WriteAllText(Path.Combine(root, marker), "owned pre-commit checkpoint");
        using var wait = new ManualResetEventSlim();
        if (!wait.Wait(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken))
        {
            throw new TimeoutException("The parent failed to terminate its owned child.");
        }
    }

    internal static async Task WaitForReleasedDatabaseAsync(string root, string databasePath, CancellationToken cancellationToken)
    {
        RequireOwnedRoot(root);
        if (!Path.GetFullPath(databasePath).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A release probe may only open its exact owned fixture files.");
        }
        var leasePath = Path.Combine(Path.GetDirectoryName(databasePath)!, "operation.lock");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Process exit alone is not evidence that all terminated-process file handles are quiescent.
                using var lease = new FileStream(leasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                using var database = new FileStream(databasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                using var journal = new FileStream(databasePath + "-journal", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return;
            }
            catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
            }
        }
    }
}

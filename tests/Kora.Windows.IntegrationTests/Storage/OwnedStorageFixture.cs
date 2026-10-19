using System.Security.Principal;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

internal sealed class OwnedStorageFixture : IApplicationDataPaths, IDisposable
{
    internal OwnedStorageFixture()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User ?? throw new InvalidOperationException("The test needs a loaded Windows profile.");
        // Nested native partitions must not inherit the testhost binary directory's path depth.
        LocalRoot = Path.Combine(RepositoryRoot(), $".kora-storage-test-{Guid.NewGuid():N}");
        RestrictedStorageDirectory.CreateRestrictedDirectory(LocalRoot, sid);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Kora.slnx"))) { return directory.FullName; }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("The storage fixture requires its source repository root.");
    }

    public string LocalRoot { get; }
    public string RoamingRoot => Path.Combine(LocalRoot, "UnusedRoaming");
    internal string Partition => Path.Combine(LocalRoot, RestrictedStorageDirectory.PartitionName);
    internal string Keys => Path.Combine(Partition, "Keys");
    internal string Artifacts => Path.Combine(Partition, "Artifacts");
    internal string KeyFile => Path.Combine(Keys, WindowsStorageKeyStore.PublishedFileName);
    internal string StagedKeyFile => Path.Combine(Keys, WindowsStorageKeyStore.StagingFileName);

    internal string ArtifactPath(Guid id, bool staged = false)
    {
        return Path.Combine(Artifacts, $"{id:N}{(staged ? ".pending" : ".gcm")}");
    }

    internal static ArtifactIdentity NewIdentity()
    {
        var session = new HostId<SessionIdentity>(Guid.NewGuid());
        return new ArtifactIdentity(session, Guid.NewGuid(), session, ArtifactRole.SessionAttachment);
    }

    public void Dispose()
    {
        Directory.Delete(LocalRoot, recursive: true);
    }
}

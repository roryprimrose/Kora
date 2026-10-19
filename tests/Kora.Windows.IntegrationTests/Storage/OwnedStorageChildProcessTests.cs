using AwesomeAssertions;

using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed class OwnedStorageChildProcessTests
{
    [WindowsFact]
    public void Scratch_parent_is_shared_and_wrong_parent_or_malformed_identity_is_rejected()
    {
        using var fixture = new OwnedStorageFixture();
        OwnedStorageChildProcess.RequireOwnedRoot(fixture.LocalRoot);
        var wrongParent = Path.Combine(AppContext.BaseDirectory, Path.GetFileName(fixture.LocalRoot));
        var foreign = () => OwnedStorageChildProcess.RequireOwnedRoot(wrongParent);
        foreign.Should().Throw<InvalidOperationException>();
        var malformedRoot = Path.Combine(OwnedStorageFixture.ScratchParent(), ".kora-storage-test-not-a-guid");
        var malformed = () => OwnedStorageChildProcess.RequireOwnedRoot(malformedRoot);
        malformed.Should().Throw<InvalidOperationException>();
        Directory.Exists(wrongParent).Should().BeFalse();
        Directory.Exists(malformedRoot).Should().BeFalse();
    }

    [Theory]
    [InlineData("evidence.db")]
    [InlineData("evidence.db-journal")]
    [InlineData("operation.lock")]
    public async Task Release_barrier_requires_all_owned_handles_to_close_and_preserves_bytes_on_cancellation(string heldName)
    {
        using var fixture = new OwnedStorageFixture();
        new WindowsSqliteEvidenceSink(fixture).Initialize();
        var partition = Path.Combine(fixture.LocalRoot, WindowsSqliteEvidenceSink.PartitionName);
        var database = Path.Combine(partition, "evidence.db");
        var databaseBytes = File.ReadAllBytes(database);
        var journalBytes = File.ReadAllBytes(database + "-journal");
        using (var held = new FileStream(Path.Combine(partition, heldName), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var waiting = OwnedStorageChildProcess.WaitForReleasedDatabaseAsync(fixture.LocalRoot, database, cancellation.Token);
            waiting.IsCompleted.Should().BeFalse("the fixture still holds one of the required exclusive handles");
            cancellation.Cancel();
            var wait = () => waiting;
            await wait.Should().ThrowAsync<OperationCanceledException>();
        }
        await OwnedStorageChildProcess.WaitForReleasedDatabaseAsync(fixture.LocalRoot, database, TestContext.Current.CancellationToken);
        File.ReadAllBytes(database).Should().Equal(databaseBytes);
        File.ReadAllBytes(database + "-journal").Should().Equal(journalBytes);
    }

    [WindowsFact]
    public async Task Release_barrier_rejects_out_of_fixture_paths_and_missing_files_without_recreating_them()
    {
        using var fixture = new OwnedStorageFixture();
        var outside = Path.Combine(Directory.GetCurrentDirectory(), "not-owned.db");
        var probe = () => OwnedStorageChildProcess.WaitForReleasedDatabaseAsync(fixture.LocalRoot, outside,
            TestContext.Current.CancellationToken);
        await probe.Should().ThrowAsync<InvalidOperationException>();
        new WindowsSqliteEvidenceSink(fixture).Initialize();
        var database = Path.Combine(fixture.LocalRoot, WindowsSqliteEvidenceSink.PartitionName, "evidence.db");
        File.Delete(database + "-journal");
        var missing = () => OwnedStorageChildProcess.WaitForReleasedDatabaseAsync(fixture.LocalRoot, database,
            TestContext.Current.CancellationToken);
        await missing.Should().ThrowAsync<FileNotFoundException>();
        File.Exists(database + "-journal").Should().BeFalse();
    }
}

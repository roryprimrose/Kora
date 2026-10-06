using System.ComponentModel;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed class WindowsStorageKeyStoreTests
{
    [WindowsFact]
    public async Task Current_user_key_round_trips_with_actual_restricted_directory_and_file_permissions()
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsStorageKeyStore(fixture);
        using var created = await store.CreateNewAsync(TestContext.Current.CancellationToken);
        using var reopened = await store.OpenAsync(TestContext.Current.CancellationToken);
        reopened.Id.Should().Be(created.Id);
        CryptographicOperations.FixedTimeEquals(reopened.Bytes, created.Bytes).Should().BeTrue();
        new RestrictedStorageDirectory(fixture).Verify();
        new RestrictedStorageDirectory(fixture).VerifyFile(fixture.KeyFile);
        var wrapper = await File.ReadAllBytesAsync(fixture.KeyFile, TestContext.Current.CancellationToken);
        wrapper.AsSpan().IndexOf(created.Bytes).Should().Be(-1);
        File.Exists(Path.Combine(fixture.LocalRoot, "kora.db")).Should().BeFalse();
    }

    [WindowsFact]
    public async Task Interrupted_flushed_key_publication_recovers_the_same_generation_without_replacement()
    {
        using var fixture = new OwnedStorageFixture();
        var interrupted = new WindowsStorageKeyStore(fixture,
            new InterruptPublicationCheckpoint(StoragePublicationKind.Key));
        var create = () => interrupted.CreateNewAsync(TestContext.Current.CancellationToken);
        await create.Should().ThrowAsync<IOException>();
        var candidate = await File.ReadAllBytesAsync(fixture.StagedKeyFile, TestContext.Current.CancellationToken);
        var candidateId = new Guid(candidate.AsSpan(8, 16));
        File.Exists(fixture.KeyFile).Should().BeFalse();

        using var recovered = await new WindowsStorageKeyStore(fixture).OpenAsync(TestContext.Current.CancellationToken);
        recovered.Id.Should().Be(candidateId);
        (await File.ReadAllBytesAsync(fixture.KeyFile, TestContext.Current.CancellationToken)).Should().Equal(candidate);
        File.Exists(fixture.StagedKeyFile).Should().BeFalse();
    }

    [WindowsFact]
    public async Task Key_cancellation_after_flush_preserves_the_only_key_candidate_for_explicit_open_recovery()
    {
        using var fixture = new OwnedStorageFixture();
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var store = new WindowsStorageKeyStore(fixture, new CancelPublicationCheckpoint(source));
        var create = () => store.CreateNewAsync(source.Token);
        await create.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(fixture.StagedKeyFile).Should().BeTrue();
        File.Exists(fixture.KeyFile).Should().BeFalse();
        var cancelledOpen = () => new WindowsStorageKeyStore(fixture).OpenAsync(source.Token);
        await cancelledOpen.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(fixture.StagedKeyFile).Should().BeTrue();
        using var recovered = await new WindowsStorageKeyStore(fixture).OpenAsync(TestContext.Current.CancellationToken);
        recovered.Bytes.Length.Should().Be(32);
    }

    [WindowsFact]
    public async Task Precancelled_creation_does_not_create_a_partition()
    {
        using var fixture = new OwnedStorageFixture();
        using var source = new CancellationTokenSource();
        source.Cancel();
        var create = () => new WindowsStorageKeyStore(fixture).CreateNewAsync(source.Token);
        await create.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(fixture.Partition).Should().BeFalse();
    }

    [WindowsFact]
    public async Task Deleted_key_cannot_be_regenerated_over_existing_artifacts()
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsStorageKeyStore(fixture);
        using var key = await store.CreateNewAsync(TestContext.Current.CancellationToken);
        var artifact = await new WindowsEncryptedArtifactStore(fixture, key).PublishAsync(
            OwnedStorageFixture.NewIdentity(), "retained content"u8.ToArray(), TestContext.Current.CancellationToken);
        var artifactBytes = await File.ReadAllBytesAsync(fixture.ArtifactPath(artifact.Identity.ArtifactId),
            TestContext.Current.CancellationToken);
        File.Delete(fixture.KeyFile);

        var open = () => store.OpenAsync(TestContext.Current.CancellationToken);
        var create = () => store.CreateNewAsync(TestContext.Current.CancellationToken);
        await open.Should().ThrowAsync<InvalidDataException>();
        await create.Should().ThrowAsync<Win32Exception>();
        File.Exists(fixture.KeyFile).Should().BeFalse();
        (await File.ReadAllBytesAsync(fixture.ArtifactPath(artifact.Identity.ArtifactId),
            TestContext.Current.CancellationToken)).Should().Equal(artifactBytes);
    }

    [WindowsFact]
    public async Task Corrupt_protected_key_fails_without_changing_the_file_or_creating_a_default()
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsStorageKeyStore(fixture);
        using var key = await store.CreateNewAsync(TestContext.Current.CancellationToken);
        var bytes = await File.ReadAllBytesAsync(fixture.KeyFile, TestContext.Current.CancellationToken);
        bytes[^1] ^= 0x80;
        await File.WriteAllBytesAsync(fixture.KeyFile, bytes, TestContext.Current.CancellationToken);
        var open = () => store.OpenAsync(TestContext.Current.CancellationToken);
        await open.Should().ThrowAsync<CryptographicException>();
        (await File.ReadAllBytesAsync(fixture.KeyFile, TestContext.Current.CancellationToken)).Should().Equal(bytes);
        File.Exists(fixture.StagedKeyFile).Should().BeFalse();
    }

    [WindowsFact]
    public async Task Key_wrapper_identity_is_bound_inside_the_DPAPI_payload()
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsStorageKeyStore(fixture);
        using var key = await store.CreateNewAsync(TestContext.Current.CancellationToken);
        var bytes = await File.ReadAllBytesAsync(fixture.KeyFile, TestContext.Current.CancellationToken);
        bytes[8] ^= 1;
        await File.WriteAllBytesAsync(fixture.KeyFile, bytes, TestContext.Current.CancellationToken);
        var open = () => store.OpenAsync(TestContext.Current.CancellationToken);
        await open.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task Ambiguous_published_and_staged_keys_are_not_silently_selected_or_removed()
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsStorageKeyStore(fixture);
        using var key = await store.CreateNewAsync(TestContext.Current.CancellationToken);
        File.Copy(fixture.KeyFile, fixture.StagedKeyFile);
        var open = () => store.OpenAsync(TestContext.Current.CancellationToken);
        await open.Should().ThrowAsync<InvalidDataException>();
        File.Exists(fixture.KeyFile).Should().BeTrue();
        File.Exists(fixture.StagedKeyFile).Should().BeTrue();
    }

    [WindowsFact]
    public async Task Permissive_supplied_root_is_rejected_without_permission_repair()
    {
        using var fixture = new OwnedStorageFixture();
        var info = new DirectoryInfo(fixture.LocalRoot);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Read, AccessControlType.Allow));
        info.SetAccessControl(acl);
        var before = info.GetAccessControl().GetSecurityDescriptorBinaryForm();
        var create = () => new WindowsStorageKeyStore(fixture).CreateNewAsync(TestContext.Current.CancellationToken);
        await create.Should().ThrowAsync<UnauthorizedAccessException>();
        Directory.Exists(fixture.Partition).Should().BeFalse();
        info.GetAccessControl().GetSecurityDescriptorBinaryForm().Should().Equal(before);
    }

    [WindowsFact]
    public async Task Permissive_actual_key_file_is_rejected_without_mutating_existing_permissions()
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsStorageKeyStore(fixture);
        using var key = await store.CreateNewAsync(TestContext.Current.CancellationToken);
        var info = new FileInfo(fixture.KeyFile);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Read, AccessControlType.Allow));
        info.SetAccessControl(acl);
        var before = info.GetAccessControl().GetSecurityDescriptorBinaryForm();
        var open = () => store.OpenAsync(TestContext.Current.CancellationToken);
        await open.Should().ThrowAsync<UnauthorizedAccessException>();
        info.GetAccessControl().GetSecurityDescriptorBinaryForm().Should().Equal(before);
    }

    [WindowsFact]
    public async Task Permissive_existing_partition_is_rejected_and_never_repaired()
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsStorageKeyStore(fixture);
        using var key = await store.CreateNewAsync(TestContext.Current.CancellationToken);
        var info = new DirectoryInfo(fixture.Partition);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Read, AccessControlType.Allow));
        info.SetAccessControl(acl);
        var before = info.GetAccessControl().GetSecurityDescriptorBinaryForm();
        var open = () => store.OpenAsync(TestContext.Current.CancellationToken);
        await open.Should().ThrowAsync<UnauthorizedAccessException>();
        info.GetAccessControl().GetSecurityDescriptorBinaryForm().Should().Equal(before);
    }

    [WindowsFact]
    public async Task A_reparse_partition_is_rejected_without_following_or_mutating_its_owned_target()
    {
        using var fixture = new OwnedStorageFixture();
        var target = Path.Combine(fixture.LocalRoot, "OwnedJunctionTarget");
        using var identity = WindowsIdentity.GetCurrent();
        RestrictedStorageDirectory.CreateRestrictedDirectory(target,
            identity.User ?? throw new InvalidOperationException("The test profile is unavailable."));
        var targetPermissions = new DirectoryInfo(target).GetAccessControl().GetSecurityDescriptorBinaryForm();
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Arguments = $"/d /c mklink /J \"{fixture.Partition}\" \"{target}\"",
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The owned junction could not be created.");
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        process.ExitCode.Should().Be(0, $"owned junction creation must succeed: {await output} {await error}");
        try
        {
            (File.GetAttributes(fixture.Partition) & FileAttributes.ReparsePoint).Should().Be(FileAttributes.ReparsePoint);
            var open = () => new WindowsStorageKeyStore(fixture).OpenAsync(TestContext.Current.CancellationToken);
            await open.Should().ThrowAsync<UnauthorizedAccessException>();
            Directory.EnumerateFileSystemEntries(target).Should().BeEmpty();
            new DirectoryInfo(target).GetAccessControl().GetSecurityDescriptorBinaryForm().Should().Equal(targetPermissions);
        }
        finally
        {
            start.Arguments = $"/d /c rmdir \"{fixture.Partition}\"";
            await RemoveOwnedJunctionAsync(start, TestContext.Current.CancellationToken);
        }
    }

    private static async Task RemoveOwnedJunctionAsync(ProcessStartInfo start, CancellationToken cancellationToken)
    {
        using var cleanup = Process.Start(start) ?? throw new InvalidOperationException("Owned junction cleanup could not start.");
        var output = cleanup.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = cleanup.StandardError.ReadToEndAsync(cancellationToken);
        await cleanup.WaitForExitAsync(cancellationToken);
        cleanup.ExitCode.Should().Be(0, $"detach the owned junction before recursive fixture cleanup: {await output} {await error}");
    }

    [WindowsFact]
    public async Task A_truncated_staged_key_is_preserved_as_an_explicit_failure_not_a_fresh_key()
    {
        using var fixture = new OwnedStorageFixture();
        var interrupted = new WindowsStorageKeyStore(fixture,
            new InterruptPublicationCheckpoint(StoragePublicationKind.Key));
        var create = () => interrupted.CreateNewAsync(TestContext.Current.CancellationToken);
        await create.Should().ThrowAsync<IOException>();
        await File.WriteAllBytesAsync(fixture.StagedKeyFile, [1, 2, 3], TestContext.Current.CancellationToken);
        var open = () => new WindowsStorageKeyStore(fixture).OpenAsync(TestContext.Current.CancellationToken);
        await open.Should().ThrowAsync<InvalidDataException>();
        (await File.ReadAllBytesAsync(fixture.StagedKeyFile, TestContext.Current.CancellationToken)).Should().Equal([1, 2, 3]);
        File.Exists(fixture.KeyFile).Should().BeFalse();
    }

    [WindowsFact]
    public async Task Oversized_key_wrapper_is_rejected_without_reading_an_unbounded_payload()
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsStorageKeyStore(fixture);
        using var key = await store.CreateNewAsync(TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(fixture.KeyFile, new byte[4097], TestContext.Current.CancellationToken);
        var open = () => store.OpenAsync(TestContext.Current.CancellationToken);
        await open.Should().ThrowAsync<InvalidDataException>();
        new FileInfo(fixture.KeyFile).Length.Should().Be(4097);
    }

    [WindowsFact]
    public async Task An_active_operation_lease_excludes_another_store_without_retry_or_key_replacement()
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsStorageKeyStore(fixture);
        using var key = await store.CreateNewAsync(TestContext.Current.CancellationToken);
        using var lease = new RestrictedStorageDirectory(fixture).AcquireLease();
        var open = () => new WindowsStorageKeyStore(fixture).OpenAsync(TestContext.Current.CancellationToken);
        await open.Should().ThrowAsync<IOException>();
        File.Exists(fixture.KeyFile).Should().BeTrue();
    }

    [Fact]
    public void Disposing_a_key_zeros_its_owned_memory_and_rejects_further_use()
    {
        var bytes = Enumerable.Repeat((byte)1, 32).ToArray();
        var key = new WindowsStorageKey(Guid.NewGuid(), bytes);
        key.Dispose();
        bytes.Should().OnlyContain(static value => value == 0);
        var read = () => key.Bytes.ToArray();
        read.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Network_roots_are_never_used_as_a_profile_storage_fallback()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var construct = () => new WindowsStorageKeyStore(new SuppliedPaths(@"\\server\share\Kora"));
        construct.Should().Throw<InvalidDataException>();
    }

    private sealed class SuppliedPaths(string localRoot) : IApplicationDataPaths
    {
        public string LocalRoot { get; } = localRoot;
        public string RoamingRoot => @"\\server\share\Roaming";
    }
}

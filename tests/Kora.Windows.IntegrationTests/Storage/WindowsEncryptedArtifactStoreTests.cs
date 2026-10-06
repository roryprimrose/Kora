using System.Buffers.Binary;
using System.Security.Cryptography;

using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed class WindowsEncryptedArtifactStoreTests
{
    [WindowsFact]
    public async Task Artifact_round_trip_uses_opaque_names_and_no_plaintext_managed_copy()
    {
        using var fixture = new OwnedStorageFixture();
        using var key = await new WindowsStorageKeyStore(fixture).CreateNewAsync(TestContext.Current.CancellationToken);
        var store = new WindowsEncryptedArtifactStore(fixture, key);
        var content = "unique artifact plaintext sentinel"u8.ToArray();
        var reference = await store.PublishAsync(OwnedStorageFixture.NewIdentity(), content,
            TestContext.Current.CancellationToken);
        (await store.ReadAsync(reference, TestContext.Current.CancellationToken)).Should().Equal(content);
        var persisted = await File.ReadAllBytesAsync(fixture.ArtifactPath(reference.Identity.ArtifactId),
            TestContext.Current.CancellationToken);
        persisted.AsSpan().IndexOf(content).Should().Be(-1);
        Directory.EnumerateFileSystemEntries(fixture.Artifacts).Should().ContainSingle();
        new RestrictedStorageDirectory(fixture).VerifyFile(fixture.ArtifactPath(reference.Identity.ArtifactId));
    }

    [WindowsFact]
    public async Task Session_owner_role_digest_and_key_generation_are_all_required_to_read()
    {
        using var fixture = new OwnedStorageFixture();
        using var key = await new WindowsStorageKeyStore(fixture).CreateNewAsync(TestContext.Current.CancellationToken);
        var store = new WindowsEncryptedArtifactStore(fixture, key);
        var reference = await store.PublishAsync(OwnedStorageFixture.NewIdentity(), "content"u8.ToArray(),
            TestContext.Current.CancellationToken);
        ArtifactReference[] wrongReferences =
        [
            reference with { Identity = reference.Identity with { SessionId = new HostId<SessionIdentity>(Guid.NewGuid()) } },
            reference with { Identity = reference.Identity with { DeletionOwnerId = new HostId<SessionIdentity>(Guid.NewGuid()) } },
            reference with { Identity = reference.Identity with { Role = ArtifactRole.ToolResult } },
            reference with { KeyId = Guid.NewGuid() },
            reference with { Length = reference.Length + 1 },
            reference with { Digest = new string('0', 64) },
        ];
        foreach (var wrong in wrongReferences)
        {
            var read = () => store.ReadAsync(wrong, TestContext.Current.CancellationToken);
            await read.Should().ThrowAsync<InvalidDataException>();
        }
        (await store.ReadAsync(reference, TestContext.Current.CancellationToken)).Should().Equal("content"u8.ToArray());
    }

    [WindowsFact]
    public async Task A_different_AES_key_with_the_same_generation_identity_cannot_authenticate_an_artifact()
    {
        using var fixture = new OwnedStorageFixture();
        using var key = await new WindowsStorageKeyStore(fixture).CreateNewAsync(TestContext.Current.CancellationToken);
        var reference = await new WindowsEncryptedArtifactStore(fixture, key).PublishAsync(
            OwnedStorageFixture.NewIdentity(), "content"u8.ToArray(), TestContext.Current.CancellationToken);
        using var wrongKey = new WindowsStorageKey(key.Id, RandomNumberGenerator.GetBytes(32));
        var envelope = await File.ReadAllBytesAsync(fixture.ArtifactPath(reference.Identity.ArtifactId),
            TestContext.Current.CancellationToken);
        var decrypt = () => ArtifactEnvelope.Decrypt(wrongKey, envelope, reference);
        decrypt.Should().Throw<CryptographicException>();
        var read = () => new WindowsEncryptedArtifactStore(fixture, wrongKey).ReadAsync(reference,
            TestContext.Current.CancellationToken);
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task Ciphertext_corruption_fails_authentication_without_overwriting_the_artifact()
    {
        using var fixture = new OwnedStorageFixture();
        using var key = await new WindowsStorageKeyStore(fixture).CreateNewAsync(TestContext.Current.CancellationToken);
        var store = new WindowsEncryptedArtifactStore(fixture, key);
        var reference = await store.PublishAsync(OwnedStorageFixture.NewIdentity(), "content"u8.ToArray(),
            TestContext.Current.CancellationToken);
        var path = fixture.ArtifactPath(reference.Identity.ArtifactId);
        var envelope = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        envelope[^1] ^= 1;
        await File.WriteAllBytesAsync(path, envelope, TestContext.Current.CancellationToken);
        var read = () => store.ReadAsync(reference, TestContext.Current.CancellationToken);
        await read.Should().ThrowAsync<CryptographicException>();
        (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)).Should().Equal(envelope);
        var report = await store.ReconcileAsync([reference], TestContext.Current.CancellationToken);
        report.Issues.Should().Equal(new ArtifactRecoveryIssue(reference.Identity.ArtifactId, ArtifactRecoveryKind.Corrupt));
    }

    [WindowsFact]
    public async Task Reconciliation_reports_staging_orphans_missing_and_corrupt_without_mutating_any_file()
    {
        using var fixture = new OwnedStorageFixture();
        using var key = await new WindowsStorageKeyStore(fixture).CreateNewAsync(TestContext.Current.CancellationToken);
        var store = new WindowsEncryptedArtifactStore(fixture, key);
        var good = await store.PublishAsync(OwnedStorageFixture.NewIdentity(), "good"u8.ToArray(),
            TestContext.Current.CancellationToken);
        var orphan = await store.PublishAsync(OwnedStorageFixture.NewIdentity(), "orphan"u8.ToArray(),
            TestContext.Current.CancellationToken);
        var missing = await store.PublishAsync(OwnedStorageFixture.NewIdentity(), "missing"u8.ToArray(),
            TestContext.Current.CancellationToken);
        var corrupt = await store.PublishAsync(OwnedStorageFixture.NewIdentity(), "corrupt"u8.ToArray(),
            TestContext.Current.CancellationToken);
        File.Delete(fixture.ArtifactPath(missing.Identity.ArtifactId));
        await File.WriteAllBytesAsync(fixture.ArtifactPath(corrupt.Identity.ArtifactId), [1, 2, 3],
            TestContext.Current.CancellationToken);

        var stagedIdentity = OwnedStorageFixture.NewIdentity();
        var interrupted = new WindowsEncryptedArtifactStore(fixture, key,
            new InterruptPublicationCheckpoint(StoragePublicationKind.Artifact));
        var publish = () => interrupted.PublishAsync(stagedIdentity, "staged"u8.ToArray(),
            TestContext.Current.CancellationToken);
        await publish.Should().ThrowAsync<IOException>();
        var filesBefore = Directory.GetFiles(fixture.Artifacts).ToDictionary(
            static path => path, static path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
            StringComparer.Ordinal);
        var report = await store.ReconcileAsync([good, missing, corrupt], TestContext.Current.CancellationToken);
        ArtifactRecoveryIssue[] expectedIssues =
        [
            new ArtifactRecoveryIssue(stagedIdentity.ArtifactId, ArtifactRecoveryKind.Staged),
            new ArtifactRecoveryIssue(orphan.Identity.ArtifactId, ArtifactRecoveryKind.Orphan),
            new ArtifactRecoveryIssue(missing.Identity.ArtifactId, ArtifactRecoveryKind.Missing),
            new ArtifactRecoveryIssue(corrupt.Identity.ArtifactId, ArtifactRecoveryKind.Corrupt),
        ];
        report.Issues.Should().BeEquivalentTo(expectedIssues);
        report.ExaminedFiles.Should().Be(4);
        var filesAfter = Directory.GetFiles(fixture.Artifacts).ToDictionary(
            static path => path, static path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
            StringComparer.Ordinal);
        filesAfter.Should().BeEquivalentTo(filesBefore);
        File.Exists(fixture.ArtifactPath(stagedIdentity.ArtifactId)).Should().BeFalse();
    }

    [WindowsFact]
    public async Task Artifact_cancellation_after_flush_leaves_read_only_staging_recovery_not_publication_or_replay()
    {
        using var fixture = new OwnedStorageFixture();
        using var key = await new WindowsStorageKeyStore(fixture).CreateNewAsync(TestContext.Current.CancellationToken);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var identity = OwnedStorageFixture.NewIdentity();
        var store = new WindowsEncryptedArtifactStore(fixture, key, new CancelPublicationCheckpoint(source));
        var publish = () => store.PublishAsync(identity, "content"u8.ToArray(), source.Token);
        await publish.Should().ThrowAsync<OperationCanceledException>();
        var report = await new WindowsEncryptedArtifactStore(fixture, key).ReconcileAsync([],
            TestContext.Current.CancellationToken);
        report.Issues.Should().Equal(new ArtifactRecoveryIssue(identity.ArtifactId, ArtifactRecoveryKind.Staged));
        File.Exists(fixture.ArtifactPath(identity.ArtifactId)).Should().BeFalse();
        File.Exists(fixture.ArtifactPath(identity.ArtifactId, staged: true)).Should().BeTrue();
    }

    [WindowsFact]
    public async Task Immutable_artifact_identity_cannot_be_overwritten_with_new_content()
    {
        using var fixture = new OwnedStorageFixture();
        using var key = await new WindowsStorageKeyStore(fixture).CreateNewAsync(TestContext.Current.CancellationToken);
        var store = new WindowsEncryptedArtifactStore(fixture, key);
        var identity = OwnedStorageFixture.NewIdentity();
        var original = await store.PublishAsync(identity, "original"u8.ToArray(), TestContext.Current.CancellationToken);
        var replace = () => store.PublishAsync(identity, "replacement"u8.ToArray(), TestContext.Current.CancellationToken);
        await replace.Should().ThrowAsync<InvalidDataException>();
        (await store.ReadAsync(original, TestContext.Current.CancellationToken)).Should().Equal("original"u8.ToArray());
    }

    [WindowsFact]
    public async Task Oversized_plaintext_is_rejected_without_a_staged_or_published_copy()
    {
        using var fixture = new OwnedStorageFixture();
        using var key = await new WindowsStorageKeyStore(fixture).CreateNewAsync(TestContext.Current.CancellationToken);
        var store = new WindowsEncryptedArtifactStore(fixture, key);
        var publish = () => store.PublishAsync(OwnedStorageFixture.NewIdentity(),
            new byte[ArtifactEnvelope.MaximumPlaintextBytes + 1], TestContext.Current.CancellationToken);
        await publish.Should().ThrowAsync<InvalidDataException>();
        Directory.EnumerateFileSystemEntries(fixture.Artifacts).Should().BeEmpty();
    }

    [WindowsFact]
    public async Task Hostile_envelope_length_is_rejected_before_decryption_allocation()
    {
        using var fixture = new OwnedStorageFixture();
        using var key = await new WindowsStorageKeyStore(fixture).CreateNewAsync(TestContext.Current.CancellationToken);
        var store = new WindowsEncryptedArtifactStore(fixture, key);
        var reference = await store.PublishAsync(OwnedStorageFixture.NewIdentity(), "content"u8.ToArray(),
            TestContext.Current.CancellationToken);
        var path = fixture.ArtifactPath(reference.Identity.ArtifactId);
        var envelope = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        BinaryPrimitives.WriteInt32LittleEndian(envelope.AsSpan(76), int.MaxValue);
        await File.WriteAllBytesAsync(path, envelope, TestContext.Current.CancellationToken);
        var read = () => store.ReadAsync(reference, TestContext.Current.CancellationToken);
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task Reconciliation_rejects_reference_and_inventory_limits_instead_of_truncating_results()
    {
        using var fixture = new OwnedStorageFixture();
        using var key = await new WindowsStorageKeyStore(fixture).CreateNewAsync(TestContext.Current.CancellationToken);
        var store = new WindowsEncryptedArtifactStore(fixture, key);
        var references = Enumerable.Range(0, WindowsEncryptedArtifactStore.MaximumReconciliationEntries + 1)
            .Select(_ => new ArtifactReference(OwnedStorageFixture.NewIdentity(), key.Id, 0, new string('0', 64)));
        var reconcileReferences = () => store.ReconcileAsync(references, TestContext.Current.CancellationToken);
        await reconcileReferences.Should().ThrowAsync<InvalidDataException>();
        for (var index = 0; index <= WindowsEncryptedArtifactStore.MaximumReconciliationEntries; index++)
        {
            await using var stream = new RestrictedStorageDirectory(fixture).CreateNewFile(
                fixture.ArtifactPath(Guid.NewGuid()), FileOptions.Asynchronous);
            await stream.WriteAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
        }
        var reconcileInventory = () => store.ReconcileAsync([], TestContext.Current.CancellationToken);
        await reconcileInventory.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task Empty_content_is_an_authenticated_artifact_not_a_missing_file()
    {
        using var fixture = new OwnedStorageFixture();
        using var key = await new WindowsStorageKeyStore(fixture).CreateNewAsync(TestContext.Current.CancellationToken);
        var store = new WindowsEncryptedArtifactStore(fixture, key);
        var reference = await store.PublishAsync(OwnedStorageFixture.NewIdentity(), ReadOnlyMemory<byte>.Empty,
            TestContext.Current.CancellationToken);
        (await store.ReadAsync(reference, TestContext.Current.CancellationToken)).Should().BeEmpty();
        (await store.ReconcileAsync([reference], TestContext.Current.CancellationToken)).Issues.Should().BeEmpty();
    }

    [WindowsFact]
    public async Task Changing_the_envelope_role_and_reference_together_still_fails_authentication()
    {
        using var fixture = new OwnedStorageFixture();
        using var key = await new WindowsStorageKeyStore(fixture).CreateNewAsync(TestContext.Current.CancellationToken);
        var store = new WindowsEncryptedArtifactStore(fixture, key);
        var reference = await store.PublishAsync(OwnedStorageFixture.NewIdentity(), "content"u8.ToArray(),
            TestContext.Current.CancellationToken);
        var path = fixture.ArtifactPath(reference.Identity.ArtifactId);
        var envelope = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        BinaryPrimitives.WriteInt32LittleEndian(envelope.AsSpan(72), (int)ArtifactRole.ToolResult);
        await File.WriteAllBytesAsync(path, envelope, TestContext.Current.CancellationToken);
        var changedReference = reference with { Identity = reference.Identity with { Role = ArtifactRole.ToolResult } };
        var read = () => store.ReadAsync(changedReference, TestContext.Current.CancellationToken);
        await read.Should().ThrowAsync<CryptographicException>();
    }

    [WindowsFact]
    public async Task Moving_valid_ciphertext_to_a_different_artifact_identity_is_not_accepted_as_an_orphan()
    {
        using var fixture = new OwnedStorageFixture();
        using var key = await new WindowsStorageKeyStore(fixture).CreateNewAsync(TestContext.Current.CancellationToken);
        var store = new WindowsEncryptedArtifactStore(fixture, key);
        var reference = await store.PublishAsync(OwnedStorageFixture.NewIdentity(), "content"u8.ToArray(),
            TestContext.Current.CancellationToken);
        var renamedId = Guid.NewGuid();
        File.Move(fixture.ArtifactPath(reference.Identity.ArtifactId), fixture.ArtifactPath(renamedId));
        var report = await store.ReconcileAsync([reference], TestContext.Current.CancellationToken);
        ArtifactRecoveryIssue[] expectedIssues =
        [
            new ArtifactRecoveryIssue(reference.Identity.ArtifactId, ArtifactRecoveryKind.Missing),
            new ArtifactRecoveryIssue(renamedId, ArtifactRecoveryKind.Corrupt),
        ];
        report.Issues.Should().BeEquivalentTo(expectedIssues);
    }

    [WindowsFact]
    public async Task Cached_key_does_not_allow_new_artifacts_after_its_durable_wrapper_is_lost()
    {
        using var fixture = new OwnedStorageFixture();
        using var key = await new WindowsStorageKeyStore(fixture).CreateNewAsync(TestContext.Current.CancellationToken);
        var store = new WindowsEncryptedArtifactStore(fixture, key);
        File.Delete(fixture.KeyFile);
        var publish = () => store.PublishAsync(OwnedStorageFixture.NewIdentity(), "content"u8.ToArray(),
            TestContext.Current.CancellationToken);
        await publish.Should().ThrowAsync<InvalidDataException>();
        Directory.EnumerateFileSystemEntries(fixture.Artifacts).Should().BeEmpty();
    }

    [WindowsFact]
    public async Task Precancelled_publication_read_and_reconciliation_do_not_mutate_artifacts()
    {
        using var fixture = new OwnedStorageFixture();
        using var key = await new WindowsStorageKeyStore(fixture).CreateNewAsync(TestContext.Current.CancellationToken);
        var store = new WindowsEncryptedArtifactStore(fixture, key);
        var reference = await store.PublishAsync(OwnedStorageFixture.NewIdentity(), "content"u8.ToArray(),
            TestContext.Current.CancellationToken);
        using var source = new CancellationTokenSource();
        source.Cancel();
        var publish = () => store.PublishAsync(OwnedStorageFixture.NewIdentity(), "other"u8.ToArray(), source.Token);
        var read = () => store.ReadAsync(reference, source.Token);
        var reconcile = () => store.ReconcileAsync([reference], source.Token);
        await publish.Should().ThrowAsync<OperationCanceledException>();
        await read.Should().ThrowAsync<OperationCanceledException>();
        await reconcile.Should().ThrowAsync<OperationCanceledException>();
        Directory.EnumerateFileSystemEntries(fixture.Artifacts).Should().ContainSingle();
        (await store.ReadAsync(reference, TestContext.Current.CancellationToken)).Should().Equal("content"u8.ToArray());
    }

    [WindowsFact]
    public async Task Reconciliation_total_byte_budget_is_enforced_even_for_corrupt_sparse_files()
    {
        using var fixture = new OwnedStorageFixture();
        using var key = await new WindowsStorageKeyStore(fixture).CreateNewAsync(TestContext.Current.CancellationToken);
        var store = new WindowsEncryptedArtifactStore(fixture, key);
        for (var index = 0; index < 17; index++)
        {
            await using var stream = new RestrictedStorageDirectory(fixture).CreateNewFile(
                fixture.ArtifactPath(Guid.NewGuid()), FileOptions.Asynchronous);
            stream.SetLength(ArtifactEnvelope.MaximumPlaintextBytes);
        }
        var reconcile = () => store.ReconcileAsync([], TestContext.Current.CancellationToken);
        await reconcile.Should().ThrowAsync<InvalidDataException>();
        Directory.EnumerateFileSystemEntries(fixture.Artifacts).Should().HaveCount(17);
    }

    [Fact]
    public void Default_host_session_and_deletion_owner_identities_fail_domain_validation()
    {
        var valid = OwnedStorageFixture.NewIdentity();
        var missingSession = () => (valid with { SessionId = default }).Validate();
        var missingOwner = () => (valid with { DeletionOwnerId = default }).Validate();
        missingSession.Should().Throw<InvalidDataException>();
        missingOwner.Should().Throw<InvalidDataException>();
    }

    [WindowsFact]
    public async Task An_empty_persisted_host_identity_is_rejected_as_invalid_data_without_reinterpretation()
    {
        using var fixture = new OwnedStorageFixture();
        using var key = await new WindowsStorageKeyStore(fixture).CreateNewAsync(TestContext.Current.CancellationToken);
        var store = new WindowsEncryptedArtifactStore(fixture, key);
        var reference = await store.PublishAsync(OwnedStorageFixture.NewIdentity(), "content"u8.ToArray(),
            TestContext.Current.CancellationToken);
        var path = fixture.ArtifactPath(reference.Identity.ArtifactId);
        var envelope = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        envelope.AsSpan(24, 16).Clear();
        await File.WriteAllBytesAsync(path, envelope, TestContext.Current.CancellationToken);
        var read = () => store.ReadAsync(reference, TestContext.Current.CancellationToken);
        await read.Should().ThrowAsync<InvalidDataException>();
        (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)).Should().Equal(envelope);
    }
}

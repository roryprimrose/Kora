using System.Buffers.Binary;
using System.Security.Cryptography;

using Kora.Core.Dependencies;

namespace Kora.Windows.Storage;

internal sealed class WindowsStorageKeyStore
{
    internal const string PublishedFileName = "key-v1.dpapi";
    internal const string StagingFileName = "key-v1.pending";
    private const int MaximumWrapperBytes = 4096;
    private const int HeaderBytes = 24;
    private readonly RestrictedStorageDirectory directory;
    private readonly IStoragePublicationCheckpoint? checkpoint;

    internal WindowsStorageKeyStore(IApplicationDataPaths paths, IStoragePublicationCheckpoint? checkpoint = null)
    {
        directory = new RestrictedStorageDirectory(paths);
        this.checkpoint = checkpoint;
    }

    internal async Task<WindowsStorageKey> CreateNewAsync(CancellationToken cancellationToken)
    {
        using var operation = new StorageOperation("storage.key.create", cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        // Existence of this partition, even if empty, forbids generation of a replacement key.
        directory.CreateNew();
        using var lease = directory.AcquireLease();
        var id = Guid.NewGuid();
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var transferred = false;
        try
        {
            var wrapper = Wrap(id, keyBytes);
            await StorageFilePublication.WriteStagingAsync(directory,
                Path.Combine(directory.Keys, StagingFileName), wrapper,
                StoragePublicationKind.Key, checkpoint, cancellationToken).ConfigureAwait(false);
            StorageFilePublication.Publish(directory, Path.Combine(directory.Keys, StagingFileName),
                Path.Combine(directory.Keys, PublishedFileName));
            var key = new WindowsStorageKey(id, keyBytes);
            transferred = true;
            operation.Complete();
            return key;
        }
        finally
        {
            if (!transferred)
            {
                CryptographicOperations.ZeroMemory(keyBytes);
            }
        }
    }

    internal async Task<WindowsStorageKey> OpenAsync(CancellationToken cancellationToken)
    {
        using var operation = new StorageOperation("storage.key.open", cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = directory.AcquireLease();
        var published = Path.Combine(directory.Keys, PublishedFileName);
        var staging = Path.Combine(directory.Keys, StagingFileName);
        var entries = Directory.EnumerateFileSystemEntries(directory.Keys).Take(3).ToArray();
        if (entries.Length != 1 || !string.Equals(entries[0], published, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(entries[0], staging, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The storage key is missing or publication has an ambiguous outcome.");
        }

        var bytes = await StorageFilePublication.ReadBoundedAsync(directory, entries[0], MaximumWrapperBytes,
            cancellationToken).ConfigureAwait(false);
        var key = Unwrap(bytes);
        var transferred = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(entries[0], staging, StringComparison.OrdinalIgnoreCase))
            {
                // Only a fully validated, flushed candidate may complete an interrupted publication.
                StorageFilePublication.FlushRecoveredStaging(directory, staging);
                StorageFilePublication.Publish(directory, staging, published);
            }
            transferred = true;
            operation.Complete();
            return key;
        }
        finally
        {
            if (!transferred)
            {
                key.Dispose();
            }
        }
    }

    internal static async Task ValidateLoadedKeyAsync(
        RestrictedStorageDirectory directory, WindowsStorageKey loadedKey, CancellationToken cancellationToken)
    {
        var published = Path.Combine(directory.Keys, PublishedFileName);
        var entries = Directory.EnumerateFileSystemEntries(directory.Keys).Take(2).ToArray();
        if (entries.Length != 1 || !string.Equals(entries[0], published, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Artifacts require an unambiguous published storage key.");
        }
        var wrapper = await StorageFilePublication.ReadBoundedAsync(directory, published, MaximumWrapperBytes,
            cancellationToken).ConfigureAwait(false);
        using var persistedKey = Unwrap(wrapper);
        if (persistedKey.Id != loadedKey.Id
            || !CryptographicOperations.FixedTimeEquals(persistedKey.Bytes, loadedKey.Bytes))
        {
            throw new InvalidDataException("The loaded artifact key does not match this storage partition.");
        }
    }

    private static byte[] Wrap(Guid id, byte[] key)
    {
        var plaintext = new byte[HeaderBytes + 32];
        "KRK1"u8.CopyTo(plaintext);
        BinaryPrimitives.WriteInt32LittleEndian(plaintext.AsSpan(4), 1);
        id.TryWriteBytes(plaintext.AsSpan(8, 16));
        key.CopyTo(plaintext, HeaderBytes);
        try
        {
            var protectedBytes = CurrentUserKeyProtection.Protect(plaintext);
            if (protectedBytes.Length > MaximumWrapperBytes - HeaderBytes)
            {
                throw new InvalidDataException("The protected key exceeds its wrapper bound.");
            }
            var wrapper = new byte[HeaderBytes + protectedBytes.Length];
            plaintext.AsSpan(0, HeaderBytes).CopyTo(wrapper);
            protectedBytes.CopyTo(wrapper, HeaderBytes);
            return wrapper;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static WindowsStorageKey Unwrap(byte[] wrapper)
    {
        if (wrapper.Length <= HeaderBytes || !wrapper.AsSpan(0, 4).SequenceEqual("KRK1"u8)
            || BinaryPrimitives.ReadInt32LittleEndian(wrapper.AsSpan(4)) != 1)
        {
            throw new InvalidDataException("The storage key wrapper version is invalid.");
        }
        var plaintext = CurrentUserKeyProtection.Unprotect(wrapper.AsSpan(HeaderBytes).ToArray());
        try
        {
            if (plaintext.Length != HeaderBytes + 32
                || !plaintext.AsSpan(0, HeaderBytes).SequenceEqual(wrapper.AsSpan(0, HeaderBytes)))
            {
                throw new InvalidDataException("The protected storage key identity is invalid.");
            }
            var id = new Guid(plaintext.AsSpan(8, 16));
            if (id == Guid.Empty)
            {
                throw new InvalidDataException("The protected storage key identity is empty.");
            }
            return new WindowsStorageKey(id, plaintext.AsSpan(HeaderBytes).ToArray());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}

namespace Kora.Windows.Storage;

internal static class StorageFilePublication
{
    internal static async Task WriteStagingAsync(
        RestrictedStorageDirectory directory, string path, ReadOnlyMemory<byte> bytes,
        StoragePublicationKind kind, IStoragePublicationCheckpoint? checkpoint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        directory.Verify();
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                         bufferSize: 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            directory.VerifyFile(path);
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            // FlushAsync drains managed buffers; Flush(true) also asks Windows to flush file data.
            stream.Flush(flushToDisk: true);
        }

        if (checkpoint is not null)
        {
            await checkpoint.AfterFlushAsync(kind, cancellationToken).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal static void Publish(RestrictedStorageDirectory directory, string staging, string published)
    {
        directory.Verify();
        directory.VerifyFile(staging);
        File.Move(staging, published, overwrite: false);
        directory.VerifyFile(published);
    }

    internal static void FlushRecoveredStaging(RestrictedStorageDirectory directory, string path)
    {
        directory.VerifyFile(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Flush(flushToDisk: true);
    }

    internal static async Task<byte[]> ReadBoundedAsync(
        RestrictedStorageDirectory directory, string path, int maximumBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        directory.VerifyFile(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None,
            bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is <= 0 || stream.Length > maximumBytes)
        {
            throw new InvalidDataException("A managed storage file exceeds its format bound.");
        }

        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        if (stream.Length != bytes.Length)
        {
            throw new InvalidDataException("A managed storage file changed during reading.");
        }
        return bytes;
    }
}

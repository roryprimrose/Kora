using System.Security.Cryptography;

namespace Kora.Windows.Storage;

internal sealed class WindowsStorageKey : IDisposable
{
    private readonly byte[] bytes;
    private bool disposed;

    internal WindowsStorageKey(Guid id, byte[] bytes)
    {
        if (id == Guid.Empty || bytes.Length != 32)
        {
            throw new InvalidDataException("The storage key identity or length is invalid.");
        }
        Id = id;
        this.bytes = bytes;
    }

    internal Guid Id { get; }

    internal ReadOnlySpan<byte> Bytes
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return bytes;
        }
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(bytes);
        disposed = true;
    }
}

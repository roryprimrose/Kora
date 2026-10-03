using System.Collections.Concurrent;

namespace Kora.Windows.Audio;

internal sealed class BlockingAudioStream : Stream
{
    private readonly BlockingCollection<byte[]> buffers = new();
    private byte[]? currentBuffer;
    private int currentOffset;
    private long position;
    private bool disposed;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => long.MaxValue;

    public override long Position
    {
        get => position;
        set => throw new NotSupportedException();
    }

    public void Add(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        buffers.Add(buffer.ToArray());
    }

    public void Complete() => buffers.CompleteAdding();

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ObjectDisposedException.ThrowIf(disposed, this);

        while (currentBuffer is null || currentOffset >= currentBuffer.Length)
        {
            try
            {
                currentBuffer = buffers.Take();
                currentOffset = 0;
            }
            catch (InvalidOperationException)
            {
                return 0;
            }
        }

        var bytesToCopy = Math.Min(count, currentBuffer.Length - currentOffset);
        Buffer.BlockCopy(currentBuffer, currentOffset, buffer, offset, bytesToCopy);
        currentOffset += bytesToCopy;
        position += bytesToCopy;
        return bytesToCopy;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            buffers.CompleteAdding();
            buffers.Dispose();
        }

        base.Dispose(disposing);
    }
}
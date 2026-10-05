using System.Security.Cryptography;

namespace Kora.Windows.Audio;

internal sealed class BlockingAudioStream(int maximumBufferedBytes = 64000) : Stream
{
    private readonly object sync = new();
    private readonly Queue<byte[]> buffers = new();
    private byte[]? currentBuffer;
    private int currentOffset;
    private int bufferedBytes;
    private long position;
    private bool completed;
    private bool disposed;

    public override bool CanRead => !disposed;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => long.MaxValue;

    public override long Position
    {
        get
        {
            lock (sync)
            {
                return position;
            }
        }
        set
        {
            lock (sync)
            {
                if (value != position)
                {
                    throw new NotSupportedException();
                }
            }
        }
    }

    internal int BufferedBytes
    {
        get
        {
            lock (sync)
            {
                return bufferedBytes;
            }
        }
    }

    public bool TryAdd(ReadOnlySpan<byte> buffer)
    {
        lock (sync)
        {
            if (disposed || completed)
            {
                return false;
            }

            if (buffer.Length > maximumBufferedBytes - bufferedBytes)
            {
                ClearBuffers();
                completed = true;
                Monitor.PulseAll(sync);
                return false;
            }

            if (!buffer.IsEmpty)
            {
                buffers.Enqueue(buffer.ToArray());
                bufferedBytes += buffer.Length;
                Monitor.PulseAll(sync);
            }

            return true;
        }
    }

    public void Add(ReadOnlySpan<byte> buffer)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!TryAdd(buffer))
            {
                throw new InvalidOperationException("The bounded audio stream is closed.");
            }
        }
    }

    public void Complete()
    {
        lock (sync)
        {
            completed = true;
            Monitor.PulseAll(sync);
        }
    }

    public void ClearAndComplete()
    {
        lock (sync)
        {
            ClearBuffers();
            completed = true;
            Monitor.PulseAll(sync);
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count)
        {
            throw new ArgumentException("The requested range exceeds the destination buffer.", nameof(count));
        }

        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (count == 0)
            {
                return 0;
            }

            var totalRead = 0;
            while (totalRead < count)
            {
                while (currentBuffer is null)
                {
                    if (buffers.TryDequeue(out currentBuffer))
                    {
                        currentOffset = 0;
                        break;
                    }

                    if (completed || disposed)
                    {
                        return totalRead;
                    }

                    Monitor.Wait(sync);
                }

                var bytesToCopy = Math.Min(count - totalRead, currentBuffer.Length - currentOffset);
                Buffer.BlockCopy(currentBuffer, currentOffset, buffer, offset + totalRead, bytesToCopy);
                CryptographicOperations.ZeroMemory(currentBuffer.AsSpan(currentOffset, bytesToCopy));
                currentOffset += bytesToCopy;
                bufferedBytes -= bytesToCopy;
                position += bytesToCopy;
                totalRead += bytesToCopy;
                if (currentOffset == currentBuffer.Length)
                {
                    currentBuffer = null;
                }
            }

            return totalRead;
        }
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        lock (sync)
        {
            var requestedPosition = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => checked(position + offset),
                _ => throw new NotSupportedException(),
            };
            if (requestedPosition != position)
            {
                throw new NotSupportedException();
            }

            return position;
        }
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (sync)
            {
                disposed = true;
                ClearBuffers();
                completed = true;
                Monitor.PulseAll(sync);
            }
        }

        base.Dispose(disposing);
    }

    private void ClearBuffers()
    {
        if (currentBuffer is not null)
        {
            CryptographicOperations.ZeroMemory(currentBuffer);
            currentBuffer = null;
        }

        while (buffers.TryDequeue(out var buffer))
        {
            CryptographicOperations.ZeroMemory(buffer);
        }

        bufferedBytes = 0;
        currentOffset = 0;
    }
}

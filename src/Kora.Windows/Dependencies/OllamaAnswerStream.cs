using Kora.Core.Dependencies;

namespace Kora.Windows.Dependencies;

/// <summary>Validates raw NDJSON before SDK decoding; synchronous SDK disposal never releases the physical source.</summary>
internal sealed class OllamaAnswerStream(Stream source, string model, int limit,
    Action<Exception> releaseFailed, Action<ModelTokenUsage?> observeUsage, CancellationToken operationToken) : Stream
{
    private readonly byte[] bytes = new byte[limit + 1];
    private int count;
    private int lineStart;
    private bool terminal;
    private bool released;
    internal bool Completed { get; private set; }
    internal int WireBytes => count;

    public override bool CanRead => !released;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(released, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(operationToken, cancellationToken);
        linked.Token.ThrowIfCancellationRequested();
        if (buffer.IsEmpty || Completed) { return 0; }
        var read = await source.ReadAsync(bytes.AsMemory(count, Math.Min(buffer.Length, bytes.Length - count)),
            linked.Token).ConfigureAwait(false);
        linked.Token.ThrowIfCancellationRequested();
        if (read == 0)
        {
            if (lineStart < count) { Consume(bytes.AsSpan(lineStart, count - lineStart).ToArray()); }
            if (!terminal) { throw new InvalidDataException("The native stream ended without a normal terminal."); }
            Completed = true;
            return 0;
        }
        var end = count + read;
        if (end > limit) { throw new InvalidDataException("The cumulative native stream exceeds its output budget."); }
        for (var index = count; index < end; index++)
        {
            if (bytes[index] != (byte)'\n') { continue; }
            Consume(bytes.AsSpan(lineStart, index - lineStart).ToArray());
            lineStart = index + 1;
        }
        bytes.AsMemory(count, read).CopyTo(buffer);
        count = end;
        return read;
    }

    private void Consume(byte[] line)
    {
        operationToken.ThrowIfCancellationRequested();
        if (terminal) { throw new InvalidDataException("Native output followed its terminal response."); }
        terminal = OllamaToolWire.ValidateStreamChunk(line, model, observeUsage);
    }

    public override async ValueTask DisposeAsync()
    {
        if (released) { return; }
        released = true;
        try { await OllamaToolWire.DisposeStreamAsync(source, releaseFailed).ConfigureAwait(false); }
        finally { await base.DisposeAsync().ConfigureAwait(false); }
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

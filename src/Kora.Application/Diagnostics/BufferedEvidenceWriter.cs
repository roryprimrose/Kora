using System.Threading.Channels;
using Kora.Core.Diagnostics;

namespace Kora.Application.Diagnostics;

internal sealed class BufferedEvidenceWriter : IAsyncDisposable
{
    private const int QueueCapacity = 1024;
    private abstract record Entry;
    private sealed record Callback(Action Write, bool Record = true) : Entry;
    private sealed record Projection(IPreparedEvidenceWrite Write) : Entry;
    private readonly Channel<Entry> queue = Channel.CreateBounded<Entry>(new BoundedChannelOptions(QueueCapacity)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait,
        AllowSynchronousContinuations = false,
    });
    private readonly Task worker;
    private readonly Action<IReadOnlyList<IPreparedEvidenceWrite>>? writeBatch;
    private IOException? failure;
    private int completing;
    private int pendingRecords;

    public BufferedEvidenceWriter(Action<IReadOnlyList<IPreparedEvidenceWrite>>? writeBatch = null)
    {
        this.writeBatch = writeBatch;
        // Persistence consumes admitted snapshots, not a caller's possibly retired host context.
        using (ExecutionContext.SuppressFlow())
        {
            worker = Task.Run(DrainAsync);
        }
    }

    public void RequireHealthy()
    {
        if (Volatile.Read(ref failure) is { } error)
        {
            throw new IOException("Background evidence delivery failed; durable admission is closed.", error);
        }
        if (worker.IsFaulted)
        {
            throw new IOException("The background evidence writer stopped unexpectedly.", worker.Exception);
        }
    }

    public void Enqueue(Action write)
    {
        if (!TryEnqueue(write))
        {
            var error = new IOException("The bounded evidence buffer is full or closed; evidence was not accepted.");
            Interlocked.CompareExchange(ref failure, error, null);
            throw error;
        }
    }

    public bool TryEnqueue(Action write) => TryEnqueue(new Callback(write));

    public bool TryEnqueue(IPreparedEvidenceWrite write)
    {
        if (writeBatch is null) { throw new InvalidOperationException("This writer does not support projection batches."); }
        return TryEnqueue(new Projection(write));
    }

    private bool TryEnqueue(Entry entry)
    {
        RequireHealthy();
        var accepted = false;
        var reserved = Interlocked.Increment(ref pendingRecords);
        try
        {
            // Dequeued batch members keep their slots until the transaction finishes. Batching
            // must not expand the original 1024 queued records plus one in-flight record bound.
            if (reserved <= QueueCapacity + 1 && queue.Writer.TryWrite(entry))
            {
                accepted = true;
                return true;
            }
        }
        finally
        {
            if (!accepted) { Interlocked.Decrement(ref pendingRecords); }
        }
        RequireHealthy();
        if (Volatile.Read(ref completing) != 0)
        {
            throw new IOException("The evidence buffer is closed; evidence was not accepted.");
        }
        return false;
    }

    public async Task FlushAsync(CancellationToken token)
    {
        RequireHealthy();
        var flushed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            // A barrier waits asynchronously for capacity; it must not poison healthy record admission.
            await queue.Writer.WriteAsync(new Callback(() => flushed.SetResult(), Record: false), token).ConfigureAwait(false);
        }
        catch (ChannelClosedException exception)
        {
            RequireHealthy();
            throw new IOException("The evidence buffer closed before the flush barrier was admitted.", exception);
        }
        var completed = await Task.WhenAny(flushed.Task, worker).WaitAsync(token).ConfigureAwait(false);
        await completed.ConfigureAwait(false);
        RequireHealthy();
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref completing, 1);
        queue.Writer.TryComplete();
#pragma warning disable VSTHRD003 // Join the owned worker; it never dispatches to the caller's synchronization context.
        await worker.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        RequireHealthy();
    }

    private async Task DrainAsync()
    {
        try
        {
            await foreach (var entry in queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                var records = 0;
                try
                {
                    if (entry is Projection projection)
                    {
                        var batch = new List<IPreparedEvidenceWrite>(IBatchedEvidenceSink.MaximumBatchSize) { projection.Write };
                        while (batch.Count < IBatchedEvidenceSink.MaximumBatchSize
                            && queue.Reader.TryPeek(out var next) && next is Projection following
                            && queue.Reader.TryRead(out _))
                        {
                            batch.Add(following.Write);
                        }
                        records = batch.Count;
                        writeBatch!(batch);
                    }
                    else if (entry is Callback callback)
                    {
                        records = callback.Record ? 1 : 0;
                        callback.Write();
                    }
                }
                catch (IOException exception)
                {
                    // Already-accepted records still get an independent delivery attempt.
                    Interlocked.CompareExchange(ref failure, exception, null);
                }
                finally { Interlocked.Add(ref pendingRecords, -records); }
            }
        }
        finally
        {
            Interlocked.Exchange(ref completing, 1);
            queue.Writer.TryComplete();
        }
    }
}

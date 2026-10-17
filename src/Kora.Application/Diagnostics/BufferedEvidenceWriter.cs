using System.Threading.Channels;

namespace Kora.Application.Diagnostics;

internal sealed class BufferedEvidenceWriter : IAsyncDisposable
{
    private readonly Channel<Action> queue = Channel.CreateBounded<Action>(new BoundedChannelOptions(1024)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait,
        AllowSynchronousContinuations = false,
    });
    private readonly Task worker;
    private IOException? failure;

    public BufferedEvidenceWriter()
    {
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
        RequireHealthy();
        if (!queue.Writer.TryWrite(write))
        {
            var error = new IOException("The bounded evidence buffer is full or closed; evidence was not accepted.");
            Interlocked.CompareExchange(ref failure, error, null);
            throw error;
        }
    }

    public async Task FlushAsync(CancellationToken token)
    {
        var flushed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(() => flushed.SetResult());
        var completed = await Task.WhenAny(flushed.Task, worker).WaitAsync(token).ConfigureAwait(false);
        await completed.ConfigureAwait(false);
        RequireHealthy();
    }

    public async ValueTask DisposeAsync()
    {
        queue.Writer.TryComplete();
#pragma warning disable VSTHRD003 // Join the owned worker; it never dispatches to the caller's synchronization context.
        await worker.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        RequireHealthy();
    }

    private async Task DrainAsync()
    {
        await foreach (var write in queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                write();
            }
            catch (IOException exception)
            {
                // Already-accepted records still get an independent delivery attempt.
                Interlocked.CompareExchange(ref failure, exception, null);
            }
        }
    }
}

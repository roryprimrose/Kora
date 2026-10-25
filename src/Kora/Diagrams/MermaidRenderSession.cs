namespace Kora.Diagrams;

internal sealed class MermaidRenderSession(IMermaidRenderer renderer) : IAsyncDisposable
{
    private CancellationTokenSource? cancellation;
    private bool retired;
    private long generation;
    internal Task WhenQuiescent { get; private set; } = Task.CompletedTask;
    internal bool IsRunning => !WhenQuiescent.IsCompleted;

    internal Task<MermaidResult?> StartAsync(string source, Func<bool> canPublish)
    {
        if (retired) { throw new InvalidOperationException("The diagram owner is retired."); }
        if (IsRunning) { throw new InvalidOperationException("The prior diagram worker has not joined."); }
        var current = ++generation;
        cancellation = new();
        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WhenQuiescent = joined.Task;
        return RunAsync(source, current, cancellation, canPublish, joined);
    }

    internal void Cancel()
    {
        ++generation;
        cancellation?.Cancel();
    }

    internal void Retire()
    {
        retired = true;
        Cancel();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003",
        Justification = "Joins this owner's worker asynchronously; never blocks a dispatcher.")]
    public async ValueTask DisposeAsync()
    {
        Retire();
        await WhenQuiescent;
    }

    private async Task<MermaidResult?> RunAsync(string source, long current,
        CancellationTokenSource owned, Func<bool> canPublish, TaskCompletionSource joined)
    {
        try
        {
            var result = await renderer.RenderAsync(source, owned.Token);
            return retired || owned.IsCancellationRequested || current != generation || !canPublish() ? null : result;
        }
        catch (OperationCanceledException) when (owned.IsCancellationRequested) { return null; }
        finally
        {
            if (ReferenceEquals(cancellation, owned)) { cancellation = null; }
            owned.Dispose();
            joined.SetResult();
        }
    }
}

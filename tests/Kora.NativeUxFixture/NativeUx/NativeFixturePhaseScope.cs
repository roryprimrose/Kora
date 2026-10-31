namespace Kora.NativeUxFixture;

internal sealed class NativeFixturePhaseScope : IDisposable
{
    internal enum PhaseKind
    {
        OwnedStorage,
        Setup,
        Context,
        NativeUi,
        Join,
        EvidenceFlush,
        Disposal,
    }

    internal enum Outcome
    {
        Running,
        Completed,
        Cancelled,
        Failed,
    }

    private readonly Action<NativeFixturePhaseScope, Outcome> observe;
    private Outcome outcome = Outcome.Failed;
    private int disposed;

    internal NativeFixturePhaseScope(PhaseKind phase,
        Action<NativeFixturePhaseScope, Outcome> observe)
    {
        Phase = phase;
        this.observe = observe;
        observe(this, Outcome.Running);
    }

    internal PhaseKind Phase { get; }

    internal void Complete() => outcome = Outcome.Completed;
    internal void Cancel() => outcome = Outcome.Cancelled;

    internal void Run(Action operation)
    {
        try
        {
            operation();
            Complete();
        }
        catch (OperationCanceledException)
        {
            Cancel();
            throw;
        }
    }

    internal async Task RunAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            Complete();
        }
        catch (OperationCanceledException)
        {
            Cancel();
            throw;
        }
    }

    internal async Task<T> RunAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            var result = await operation();
            Complete();
            return result;
        }
        catch (OperationCanceledException)
        {
            Cancel();
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0) { observe(this, outcome); }
    }
}

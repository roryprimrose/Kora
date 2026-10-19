namespace Kora.Application.Diagnostics;

internal sealed class EvidenceDeliveryGate
{
    private readonly object gate = new();
    private int ownerThread;
    private int depth;
    private int requiredWaiters;

    internal int WaitingRequired { get { lock (gate) { return requiredWaiters; } } }

    public Lease Enter(bool required)
    {
        var thread = Environment.CurrentManagedThreadId;
        lock (gate)
        {
            if (ownerThread == thread)
            {
                depth++;
                return new(this);
            }
            if (required) { requiredWaiters++; }
            try
            {
                // A completed batch cannot immediately reacquire ahead of an admitted audit.
                while (depth != 0 || (!required && requiredWaiters != 0)) { Monitor.Wait(gate); }
                ownerThread = thread;
                depth = 1;
                return new(this);
            }
            finally
            {
                if (required) { requiredWaiters--; }
                Monitor.PulseAll(gate);
            }
        }
    }

    private void Exit()
    {
        lock (gate)
        {
            if (ownerThread != Environment.CurrentManagedThreadId)
            {
                throw new SynchronizationLockException("Evidence delivery must leave on its owning thread.");
            }
            if (--depth == 0) { ownerThread = 0; }
            Monitor.PulseAll(gate);
        }
    }

    internal readonly struct Lease(EvidenceDeliveryGate owner) : IDisposable
    {
        public void Dispose() => owner.Exit();
    }
}

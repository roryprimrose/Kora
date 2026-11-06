namespace Kora.Application.Skills;

/// <summary>Consumes original activation admission only for the lifetime of an exact host-owned data collection.</summary>
internal sealed class OwnedSkillQuestionContinuation(Func<bool> original, Func<bool> stable, Func<long> generation)
{
    private readonly Lock gate = new();
    private bool collecting;
    private bool completed;
    private long admittedGeneration;

    internal bool IsCurrent()
    {
        lock (gate)
        {
            return stable() && (collecting || (completed ? generation() == admittedGeneration : original()));
        }
    }

    internal IDisposable Open()
    {
        lock (gate)
        {
            if (collecting || !IsCurrent())
            { throw new InvalidOperationException("The exact original activation cannot open another skill input collection."); }
            collecting = true;
            return new Collection(this);
        }
    }

    private void Complete()
    {
        lock (gate)
        {
            admittedGeneration = generation();
            completed = true;
            collecting = false;
        }
    }

    private sealed class Collection(OwnedSkillQuestionContinuation owner) : IDisposable
    {
        private int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) { owner.Complete(); }
        }
    }
}

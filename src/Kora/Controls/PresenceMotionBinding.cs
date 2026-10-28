using Kora.Core.Presentation;

namespace Kora.Controls;

internal sealed class PresenceMotionBinding(
    PresenceControl presence,
    IMotionPreferenceSource source,
    Action<Action> dispatch,
    Action<Exception> reportFailure) : IDisposable
{
    private IDisposable? subscription;
    private SubscriptionGeneration? generation;
    private bool eligible;
    private bool failed;
    private bool disposed;

    internal void SetEligible(bool value)
    {
        if (disposed || failed) { return; }
        if (eligible == value)
        {
            if (!value) { presence.IsMotionEnabled = false; }
            return;
        }
        eligible = value;
        RetireSubscription();
        if (!value) { return; }

        var admittedGeneration = new SubscriptionGeneration();
        Volatile.Write(ref generation, admittedGeneration);
        try
        {
            subscription = source.Subscribe(() => OnChanged(admittedGeneration));
            Refresh();
        }
        catch (InvalidOperationException exception)
        {
            Fail(exception);
        }
    }

    internal void Refresh()
    {
        if (Volatile.Read(ref generation) is { } current)
        {
            Refresh(current, Volatile.Read(ref current.Revision));
        }
    }

    private void OnChanged(SubscriptionGeneration admittedGeneration)
    {
        if (admittedGeneration != Volatile.Read(ref generation)) { return; }
        var admittedRevision = Interlocked.Increment(ref admittedGeneration.Revision);
        dispatch(() => Refresh(admittedGeneration, admittedRevision));
    }

    private void Refresh(SubscriptionGeneration admittedGeneration, long admittedRevision)
    {
        if (!IsCurrent(admittedGeneration, admittedRevision)) { return; }
        try
        {
            var preference = source.Read();
            if (IsCurrent(admittedGeneration, admittedRevision))
            {
                presence.IsMotionEnabled = preference switch
                {
                    MotionPreference.AnimationsEnabled => true,
                    MotionPreference.ReducedMotion => false,
                    _ => throw new InvalidOperationException("The current motion preference is invalid."),
                };
            }
        }
        catch (InvalidOperationException exception)
        {
            Fail(exception);
        }
    }

    private bool IsCurrent(SubscriptionGeneration admittedGeneration, long admittedRevision) =>
        !disposed && !failed && eligible && admittedGeneration == Volatile.Read(ref generation)
        && admittedRevision == Volatile.Read(ref admittedGeneration.Revision);

    private void Fail(InvalidOperationException exception)
    {
        failed = true;
        try { RetireSubscription(); }
        finally { reportFailure(exception); }
    }

    private void RetireSubscription()
    {
        Volatile.Write(ref generation, null);
        presence.IsMotionEnabled = false;
        var retired = subscription;
        subscription = null;
        retired?.Dispose();
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        RetireSubscription();
    }

    private sealed class SubscriptionGeneration
    {
        public long Revision;
    }
}

namespace Kora.Application.Hosting;

/// <summary>One private voluntary operation; no cancellation or application effect is implied by preparation.</summary>
public sealed class ApplicationLifecycleShutdown(Func<bool> current, Func<Task> stopAndJoin, Action release) : IDisposable
{
    public bool IsCurrent => current();
    public Task StopAndJoinAsync() => stopAndJoin();
    public void Dispose() => release();
}

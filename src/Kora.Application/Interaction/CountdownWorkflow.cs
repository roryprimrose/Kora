using System.Globalization;

using Kora.Application.Infrastructure;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Interaction;

public sealed partial class CountdownWorkflow(
    TimeProvider time, IUiDispatcher dispatcher, ILogger<CountdownWorkflow> logger) : ObservableObject, IDisposable
{
    public const string Scope = "Visual only while Kora is running. No sound, speech, notification, wake or saved schedule.";
    private readonly Lock gate = new();
    private ITimer? timer;
    private long generation;
    private long started;
    private int duration;
    private bool disposed;
    private Func<bool> lifetimeEligible = static () => false;
    private string status = "No timer started in this run.";

    public string Status => status;
    public bool IsRunning => timer is not null;

    public string Execute(CountdownCommand command, Func<bool> eligible)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            using var activity = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Presentation);
            if (!eligible())
            {
                Outcome(logger, command.Operation, "Unavailable");
                activity.Complete(HostOperationOutcome.Cancelled);
                return "Timer unavailable: current private host ownership and clear call admission are required.";
            }
            Refresh();
            string result;
            switch (command.Operation)
            {
                case CountdownOperation.Start when CountdownCommand.IsValidSeconds(command.Seconds):
                    if (IsRunning) { result = "Timer already active; use timer cancel first. The existing timer was not replaced."; break; }
                    Start(command.Seconds, eligible);
                    result = status;
                    break;
                case CountdownOperation.Status: result = status; break;
                case CountdownOperation.Cancel:
                    Retire();
                    result = status;
                    break;
                case CountdownOperation.Help: result = CountdownCommand.Syntax; break;
                default: result = CountdownCommand.Syntax; break;
            }
            Outcome(logger, command.Operation, "Completed");
            activity.Complete(HostOperationOutcome.Completed);
            return result;
        }
    }

    private void Start(int seconds, Func<bool> eligible)
    {
        var expected = ++generation;
        var continuation = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Presentation);
        started = time.GetTimestamp();
        timer = time.CreateTimer(_ => QueueTick(expected, continuation),
            null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        lifetimeEligible = eligible;
        duration = seconds;
        status = Remaining(seconds);
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(Status));
    }

    private void QueueTick(long expected, Func<HostActivity> continuation)
    {
        lock (gate)
        {
            if (disposed || generation != expected) { return; }
            dispatcher.Post(() => Tick(expected, continuation));
        }
    }

    private void Tick(long expected, Func<HostActivity> continuation)
    {
        lock (gate)
        {
            if (disposed || generation != expected) { return; }
            using var activity = continuation();
            Refresh();
            activity.Complete(HostOperationOutcome.Completed);
        }
    }

    private void Refresh()
    {
        if (timer is not { } ownedTimer) { return; }
        if (!lifetimeEligible()) { Retire(); return; }
        var elapsed = time.GetElapsedTime(started);
        if (elapsed >= TimeSpan.FromSeconds(duration))
        {
            Stop(ownedTimer, "Timer complete. Visual completion only.");
            Outcome(logger, CountdownOperation.Status, "Expired");
            return;
        }
        SetStatus(Remaining(duration - (int)elapsed.TotalSeconds));
    }

    public void Retire()
    {
        lock (gate)
        {
            if (timer is not { } ownedTimer) { return; }
            Stop(ownedTimer, "Timer cancelled. No completion or reminder is pending.");
            Outcome(logger, CountdownOperation.Cancel, "Cancelled");
        }
    }

    private void Stop(ITimer ownedTimer, string terminalStatus)
    {
        generation++;
        ownedTimer.Dispose();
        timer = null;
        lifetimeEligible = static () => false;
        status = terminalStatus;
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(Status));
    }

    private void SetStatus(string value) => SetProperty(ref status, value, nameof(Status));
    private static string Remaining(int seconds) =>
        "Timer running: " + seconds.ToString(CultureInfo.InvariantCulture) + " seconds remaining.";

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) { return; }
            disposed = true;
            Retire();
        }
    }
}

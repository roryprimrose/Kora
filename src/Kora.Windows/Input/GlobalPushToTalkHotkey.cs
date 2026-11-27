using System.Collections.Concurrent;

using Kora.Core.Voice;

namespace Kora.Windows.Input;

public sealed class GlobalPushToTalkHotkey : IAsyncDisposable
{
    private readonly IGlobalPttNative native;
    private readonly Lock gate = new();
    private readonly ConcurrentQueue<NativeCommand> commands = new();
    private readonly TaskCompletionSource<uint> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool closing;
    private bool quit;
    private bool registered;
    private GlobalPttShortcut? shortcut;
    private long generation;
    private long heldGeneration;
    private sealed record NativeCommand(Action Execute, Action<Exception> Reject);

    public GlobalPushToTalkHotkey() : this(new WindowsGlobalPttNative()) { }
    internal GlobalPushToTalkHotkey(IGlobalPttNative native)
    {
        this.native = native;
        var thread = new Thread(Run) { IsBackground = true, Name = "Kora global PTT input" };
        thread.Start();
    }

    public event Action<long, bool>? HoldChanged;
    public event Action<Exception>? Failed;
    public bool IsHeld(long observedGeneration) => observedGeneration > 0 && Volatile.Read(ref heldGeneration) == observedGeneration;

    public async Task ConfigureAsync(GlobalPttShortcut? value)
    {
#pragma warning disable VSTHRD003 // Joins this instance's exact native queue initialization.
        var threadId = await started.Task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            commands.Enqueue(new(() =>
            {
                try
                {
                    Release();
                    RemoveRegistration();
                    shortcut = null;
                    if (value is not null && value != GlobalPttShortcut.Unassigned)
                    {
                        native.Register(value);
                        registered = true;
                        shortcut = value;
                    }
                    result.SetResult();
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                { result.SetException(exception); }
            }, exception => result.TrySetException(exception)));
            native.Wake(threadId);
        }
        await result.Task.ConfigureAwait(false);
    }

    private void Run()
    {
        Exception? failure = null;
        try
        {
            started.SetResult(native.Initialize());
            while (!quit)
            {
                while (commands.TryDequeue(out var command)) { command.Execute(); }
                if (quit) { break; }
                var message = native.Wait(Volatile.Read(ref heldGeneration) != 0);
                if (message == GlobalPttNativeMessage.Press && registered && shortcut is { } chord
                    && Volatile.Read(ref heldGeneration) == 0 && native.IsHeld(chord))
                {
                    var next = checked(++generation);
                    Volatile.Write(ref heldGeneration, next);
                    HoldChanged?.Invoke(next, true);
                }
                if (Volatile.Read(ref heldGeneration) != 0 && shortcut is { } held && !native.IsHeld(held))
                { Release(); }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failure = exception;
            started.TrySetException(exception);
            Failed?.Invoke(exception);
        }
        finally
        {
            try { Release(); RemoveRegistration(); }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            { failure ??= exception; Failed?.Invoke(exception); }
            lock (gate) { closing = true; }
            // Complete queued callers with an explicit unavailable owner rather than abandoning their joins.
            while (commands.TryDequeue(out var command))
            { command.Reject(failure ?? new ObjectDisposedException(nameof(GlobalPushToTalkHotkey))); }
            if (failure is null) { stopped.TrySetResult(); }
            else { stopped.TrySetException(failure); }
        }
    }

    private void Release()
    {
        var previous = Interlocked.Exchange(ref heldGeneration, 0);
        if (previous != 0) { HoldChanged?.Invoke(previous, false); }
    }

    private void RemoveRegistration()
    {
        if (!registered) { return; }
        native.Unregister();
        registered = false;
    }

    public async ValueTask DisposeAsync()
    {
#pragma warning disable VSTHRD003 // Joins the dedicated native input thread before releasing this owner.
        try
        {
            var threadId = await started.Task.ConfigureAwait(false);
            lock (gate)
            {
                if (!closing)
                {
                    closing = true;
                    commands.Enqueue(new(() => quit = true, static _ => { }));
                    native.Wake(threadId);
                }
            }
        }
        finally { await stopped.Task.ConfigureAwait(false); }
#pragma warning restore VSTHRD003
    }
}

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    // Clipboard cancellation retires presentation immediately; exit must still join the actual adapter slot.
    internal async Task WaitForClipboardQuiescenceAsync()
    {
        Task outstanding;
        lock (dispatchGate) { outstanding = quiescence?.Task ?? Task.CompletedTask; }
        await outstanding.ConfigureAwait(false);
    }
}
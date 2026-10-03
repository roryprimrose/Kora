namespace Kora.Core.Communication;

public sealed class CallStateChangedEventArgs(CallState state) : EventArgs
{
    public CallState State { get; } = Enum.IsDefined(state)
        ? state
        : throw new ArgumentOutOfRangeException(nameof(state), state, "The call state is invalid.");
}

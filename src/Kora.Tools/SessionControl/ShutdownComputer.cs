using Kora.Core.Skills;

namespace Kora.Tools.SessionControl;

public sealed class ShutdownComputer(FixedSessionActionBroker broker)
{
    public Task<FixedSessionEffectResult> ExecuteAsync(string identityDigest,
        Func<CancellationToken, ValueTask<bool>> authorize, Func<bool> current, CancellationToken token) =>
        broker.RequestAsync(FixedSessionAction.Shutdown, identityDigest, authorize, current, token);
}
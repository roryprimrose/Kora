using Kora.Core.Skills;

namespace Kora.Tools.SessionControl;

public sealed class RestartComputer(FixedSessionActionBroker broker)
{
    public Task<FixedSessionEffectResult> ExecuteAsync(string identityDigest,
        Func<CancellationToken, ValueTask<bool>> authorize, Func<bool> current, CancellationToken token) =>
        broker.RequestAsync(FixedSessionAction.Restart, identityDigest, authorize, current, token);
}
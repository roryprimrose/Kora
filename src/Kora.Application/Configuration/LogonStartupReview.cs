using Kora.Core.Authorization;
using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

public sealed class LogonStartupReview
{
    internal LogonStartupReview(bool? value, LogonStartupRegistration registration, bool? saved,
        WorkSessionAuthorization session, RequestOrigin origin, Func<bool> eligible, long callRevision, DateTimeOffset expires)
    {
        Value = value; Registration = registration; Saved = saved; Session = session;
        Origin = origin; Eligible = eligible; CallRevision = callRevision; Expires = expires;
    }
    internal bool? Value { get; }
    internal LogonStartupRegistration Registration { get; }
    internal bool? Saved { get; }
    internal WorkSessionAuthorization Session { get; }
    internal RequestOrigin Origin { get; }
    internal Func<bool> Eligible { get; }
    internal long CallRevision { get; }
    internal DateTimeOffset Expires { get; }
    public string Description => $"Review: {(Value is null ? "reset to Off and remove saved intent" : Value.Value ? "enable" : "disable")} "
        + "Start Kora at Windows user logon for this installed version and current Windows account only. "
        + $"Current registration: {Registration.Enabled}; saved intent: {Saved?.ToString() ?? "unsaved"}. "
        + "No change yet. Confirm separately within 60 seconds or Cancel. No launch, listening, provider, network or permission change. "
        + "Next logon is an ordinary fresh launch; ongoing voice consent and every instance, private-storage, unlocked-session and audio gate still apply.";
}

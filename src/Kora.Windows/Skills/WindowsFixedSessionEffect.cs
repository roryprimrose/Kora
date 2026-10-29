using Kora.Core.Skills;

namespace Kora.Windows.Skills;

public sealed class WindowsFixedSessionEffect : IFixedSessionEffect
{
    private readonly IWindowsFixedSessionPlatform platform;

    public WindowsFixedSessionEffect(Kora.Core.Platform.ISessionController session)
        : this(new WindowsFixedSessionPlatform(session)) { }

    internal WindowsFixedSessionEffect(IWindowsFixedSessionPlatform platform) => this.platform = platform;

    public string? CurrentIdentityDigest
    {
        get
        {
            if (!platform.IsCurrentInteractiveSession()) { return null; }
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            if (identity.User is not { } user) { return null; }
            var bytes = System.Text.Encoding.UTF8.GetBytes(
                $"kora.fixed-session-identity.v1|{user.Value}|{process.SessionId}");
            return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        }
    }

    public FixedSessionEffectResult Request(FixedSessionAction action)
    {
        if (!Enum.IsDefined(action)) { throw new ArgumentOutOfRangeException(nameof(action)); }
        if (!platform.IsCurrentInteractiveSession()) { return new(FixedSessionEffectOutcome.Denied); }
        if (action == FixedSessionAction.Lock) { return platform.Lock(); }
        using var authority = platform.DuplicateShutdownAuthority();
        var activationError = authority.Enable();
        if (activationError != 0) { return new(FixedSessionEffectOutcome.Denied, activationError); }
        FixedSessionEffectResult result;
        var restoreError = 0;
        try
        {
            result = platform.IsCurrentInteractiveSession()
                ? authority.Request(action) : new(FixedSessionEffectOutcome.Denied);
        }
        finally { restoreError = authority.Restore(); }
        return restoreError == 0 ? result : new(FixedSessionEffectOutcome.Unconfirmed, restoreError);
    }
}
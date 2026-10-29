namespace Kora.Application.Configuration;

public sealed record LogonStartupState(bool? Saved, bool? Effective, bool Available, string? Error)
{
    public bool Desired => Saved == true;
    public string Description => $"Last confirmed saved: {Saved?.ToString() ?? "unsaved (default Off)"}; desired: {Desired}; "
        + $"registered for next logon: {Effective?.ToString() ?? "unknown"}; available: {Available}. "
        + (Error ?? "Registration is not application readiness or voice consent. Startup uses the ordinary tray/minimal shell and all fresh host/privacy/audio gates.");
}

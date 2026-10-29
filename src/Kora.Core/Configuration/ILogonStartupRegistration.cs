namespace Kora.Core.Configuration;

/// <summary>Observes and changes only the current user's exact installed application registration.</summary>
public interface ILogonStartupRegistration
{
    LogonStartupRegistration Inspect();
    void Apply(bool enabled, LogonStartupRegistration reviewed, Func<bool> admitted);
}

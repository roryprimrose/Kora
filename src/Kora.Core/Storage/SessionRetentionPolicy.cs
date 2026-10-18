using Kora.Core.Configuration;

namespace Kora.Core.Storage;

public sealed class SessionRetentionPolicy
{
    private readonly Lock gate = new();
    private SessionRetentionSettings settings = SessionRetentionSettings.Default;
    private bool available = true;
    private long revision;

    public SessionRetentionSettings Settings => Observe().Settings;

    public (SessionRetentionSettings Settings, long Revision) Observe()
    {
        lock (gate)
        {
            if (!available) { throw new InvalidDataException("Session-retention policy is unconfirmed; retention and new clock writes are held."); }
            return (settings, revision);
        }
    }

    public void Activate(SessionRetentionSettings value)
    {
        value.Validate();
        lock (gate) { settings = value; available = true; revision = checked(revision + 1); }
    }

    public void HoldUnavailable() { lock (gate) { available = false; revision = checked(revision + 1); } }
}

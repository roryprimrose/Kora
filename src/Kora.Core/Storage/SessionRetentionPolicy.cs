using Kora.Core.Configuration;

namespace Kora.Core.Storage;

public sealed class SessionRetentionPolicy
{
    private readonly Lock gate = new();
    private SessionRetentionSettings settings = SessionRetentionSettings.Default;
    private bool available = true;

    public SessionRetentionSettings Settings
    {
        get
        {
            lock (gate)
            {
                if (!available) { throw new InvalidDataException("Session-retention policy is unconfirmed; retention and new clock writes are held."); }
                return settings;
            }
        }
    }

    public void Activate(SessionRetentionSettings value)
    {
        value.Validate();
        lock (gate) { settings = value; available = true; }
    }

    public void HoldUnavailable() { lock (gate) { available = false; } }
}

namespace Kora;

internal interface IPresenceWindow
{
    bool CanReplace { get; }
    PresenceWindowState SuspendForReplacement();
    void CloseForReplacement();
    void RestoreAfterReplacement(PresenceWindowState state);
}

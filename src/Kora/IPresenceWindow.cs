namespace Kora;

internal interface IPresenceWindow
{
    bool CanReplace { get; }
    bool IsReplacementReady { get; }
    bool PrepareForReplacement();
    void CancelReplacement();
    PresenceWindowState SuspendForReplacement();
    void CloseForReplacement();
    void RestoreAfterReplacement(PresenceWindowState state);
}

namespace Kora.Windows.Coordination;

/// <summary>Owns installer admission and durable continuity across native package work.</summary>
public interface IInstallerMaintenanceLease : IAsyncDisposable
{
    /// <summary>Records that native Apply may begin; an exception does not undo this state.</summary>
    void BeginNativeApply();

    /// <summary>Records Burn's terminal Apply/rollback callback while the reservation is still active.</summary>
    /// <returns>Whether the current, undisposed reservation accepted the terminal callback.</returns>
    bool ConfirmNativeApplyComplete();
}

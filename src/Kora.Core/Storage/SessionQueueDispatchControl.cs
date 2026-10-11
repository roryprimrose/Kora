using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Observed current-run dispatch control, never persisted execution authority or a grant.</summary>
public sealed record SessionQueueDispatchControl(
    Guid RunId, HostRevision Generation, long Revision, long ControlRevision, bool Paused, bool Held)
{
    public bool BlocksAdmission => Paused || Held;
    public string Reason => Held
        ? "Dispatch control is unconfirmed or its private admission changed; fresh exact pause/recovery is required. No replay."
        : Paused ? "Explicit current-run session queue pause blocks new admissions only; committed work drains normally."
        : "Current-run dispatch is not explicitly paused; manual dispatch and all current eligibility checks remain required.";
}

using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    internal LocalUnavailableObservation? ObserveJoinedLocalUnavailable(ModelTurn turn)
    {
        if (!CanOfferAfterLocal(turn) || !IsTurnQuiescent(turn) || turn.RetirementReason != ModelTurnReason.None
            || !ConsumerAdmissionCurrent(turn) || !Eligible(turn.ControlRevision))
        { return null; }
        return turn.LocalUnavailable ??= new(turn.TerminalResult!, turn.Context, turn.SessionGeneration,
            turn.TaskRevision, turn.ControlRevision,
            () => CanOfferAfterLocal(turn) && IsTurnQuiescent(turn) && turn.RetirementReason == ModelTurnReason.None
                && ConsumerAdmissionCurrent(turn) && Eligible(turn.ControlRevision)
                && ContextReason(turn.Context, turn.Provenance.Request) == ModelTurnReason.None);
    }
}

using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record OrdinaryTurnDeadlineObservation(long Revision, OrdinaryTurnDeadline? Saved)
{
    public OrdinaryTurnDeadline Effective => Saved ?? OrdinaryTurnDeadline.Default;
}

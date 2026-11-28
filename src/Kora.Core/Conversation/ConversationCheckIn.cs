using Kora.Core.Configuration;

namespace Kora.Core.Conversation;

/// <summary>
/// A volatile host-issued check-in for one running ordinary turn. The turn keeps running while it is pending.
/// It is not an approval, grant, durable question or replayable decision and retires when the turn ends.
/// </summary>
/// <param name="Id">The host-issued identity that an answer must name exactly.</param>
/// <param name="Interval">The captured ordinary check-in interval offered by "keep going".</param>
/// <param name="ActiveElapsed">Active turn time, excluding host waits, when the check-in was issued.</param>
public sealed record ConversationCheckIn(Guid Id, OrdinaryTurnDeadline Interval, TimeSpan ActiveElapsed);

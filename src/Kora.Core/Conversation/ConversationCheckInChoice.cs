using Kora.Core.Configuration;

namespace Kora.Core.Conversation;

/// <summary>A typed check-in answer. Only <see cref="ConversationCheckInOperation.KeepGoingFor"/> carries minutes.</summary>
public sealed record ConversationCheckInChoice
{
    public ConversationCheckInChoice(ConversationCheckInOperation operation, OrdinaryTurnDeadline? minutes = null)
    {
        if (!Enum.IsDefined(operation) || (operation == ConversationCheckInOperation.KeepGoingFor) != minutes.HasValue)
        { throw new ArgumentException("Only an explicit keep-going-for choice carries whole minutes from 1 through 60.", nameof(minutes)); }
        Operation = operation;
        Minutes = minutes;
    }

    public ConversationCheckInOperation Operation { get; }
    public OrdinaryTurnDeadline? Minutes { get; }

    /// <summary>The stable low-cardinality audit reason for this choice.</summary>
    public string ReasonCode => Operation switch
    {
        ConversationCheckInOperation.KeepGoing => "keep-going",
        ConversationCheckInOperation.KeepGoingFor => "keep-going-minutes",
        ConversationCheckInOperation.KeepRunning => "keep-running",
        _ => "stop",
    };
}

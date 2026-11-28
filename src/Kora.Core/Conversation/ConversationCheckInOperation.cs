namespace Kora.Core.Conversation;

public enum ConversationCheckInOperation
{
    /// <summary>Keep running for another captured interval, then check in again.</summary>
    KeepGoing,

    /// <summary>Keep running for a user-chosen number of minutes, then check in again.</summary>
    KeepGoingFor,

    /// <summary>Keep running until the turn finishes, without further check-ins.</summary>
    KeepRunning,

    /// <summary>Stop the turn through the ordinary running-conversation cancellation path.</summary>
    Stop,
}

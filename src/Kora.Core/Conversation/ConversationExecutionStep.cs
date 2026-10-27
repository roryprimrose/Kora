namespace Kora.Core.Conversation;

/// <summary>The work the host has actually entered in the admitted model/tool loop.</summary>
public enum ConversationExecutionStep
{
    GeneratingAnswer,
    ReadingObservation,
}

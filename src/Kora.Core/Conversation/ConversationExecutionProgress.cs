namespace Kora.Core.Conversation;

/// <summary>Content-free host execution steps, never provider reasoning or durable results.</summary>
public sealed record ConversationExecutionProgress(ConversationExecutionStep Step, int ObservationCount, long Revision);

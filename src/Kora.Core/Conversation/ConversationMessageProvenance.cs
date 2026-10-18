namespace Kora.Core.Conversation;

internal static class ConversationMessageProvenance
{
    internal static bool IsValidGeneration(long value) => value > 0;
    internal static bool IsValidRevision(long value) => value is >= 1 and <= 2;
}

using System.Runtime.InteropServices;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.Conversation;

/// <summary>Volatile exact predicates over terminal HOST metadata; null means Any.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ConversationTerminalFilter(
    ModelTurnOutcome? Outcome = null, ModelTurnReason? Reason = null, HostTaskState? TaskState = null)
{
    public void Validate()
    {
        ValidateDefined(Outcome);
        ValidateDefined(Reason);
        ValidateDefined(TaskState);
    }

    public bool Matches(ConversationMessage message, ConversationSearchRole role) =>
        this == default || role != ConversationSearchRole.USER && message.Outcome is not null
            && (Outcome is null || Outcome == message.Outcome)
            && (Reason is null || Reason == message.Reason)
            && (TaskState is null || TaskState == message.ExpectedTaskState);

    private static void ValidateDefined<T>(T? value) where T : struct, Enum
    {
        if (value is { } exact && !Enum.IsDefined(exact))
        { throw new ArgumentOutOfRangeException(nameof(value), "The terminal HOST predicate is undefined."); }
    }
}

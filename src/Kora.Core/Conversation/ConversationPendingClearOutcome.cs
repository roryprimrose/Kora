using Kora.Core.Hosting;

namespace Kora.Core.Conversation;

/// <summary>Observed per-original completion; Unknown never means cancelled or physically stopped.</summary>
public sealed record ConversationPendingClearOutcome(ConversationPendingClearEntry Entry,
    bool CancellationRequested, HostTaskState? RetainedState, bool Confirmed, string Outcome);

namespace Kora.Core.Conversation;

/// <summary>Current metadata observation only; none of these states admits source-body access.</summary>
public enum SubmittedHistoryAvailability
{
    Unknown,
    RetainedRevision,
    Unavailable,
}

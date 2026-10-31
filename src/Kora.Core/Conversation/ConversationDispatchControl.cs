using Kora.Core.Storage;

namespace Kora.Core.Conversation;

/// <summary>Exact volatile host observation required for a deliberate dispatch decision.</summary>
public sealed record ConversationDispatchControl(
    SessionWorkspaceEntry Target, Guid AdmissionId, long LaneRevision, long ControlRevision);

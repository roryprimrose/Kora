namespace Kora.Core.Conversation;

/// <summary>Content-free facts observed by the host allocator, not a provider estimate or task receipt.</summary>
public sealed record ConversationResourceStatus(int ConfiguredMaximum, int CapturedMaximum, int QualifiedCeiling,
    int OwnedLeases, int WaitingRequests, ConversationResourceWaitReason WaitReason)
{
    public int EffectiveCeiling => Math.Min(Math.Min(ConfiguredMaximum, CapturedMaximum), QualifiedCeiling);
}

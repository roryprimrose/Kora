using System.Text.Json;
using System.Text.Json.Serialization;

using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Storage;

namespace Kora.Application.Memory.ModelProposals;

/// <summary>Private volatile exact-source review; retained text supplies no authority.</summary>
internal sealed class ModelMemoryProposalReview(
    MemoryManagementService owner, SessionWorkspaceEntry target, ConversationMessage source,
    MemoryRecord record, HostId<RequestIdentity> trigger, long control, DateTimeOffset expires,
    Func<bool> eligible) : IDisposable
{
    private static readonly JsonSerializerOptions ReviewFormat = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
    private MemoryRecord current = record;
    private readonly HostId<RequestIdentity> reviewTrigger = trigger;
    private volatile bool retired;
    private bool taken;
    private readonly Lock gate = new();
    internal MemoryManagementService Owner => owner;
    internal SessionWorkspaceEntry Target => target;
    internal ConversationMessage Source => source;
    internal HostId<RequestIdentity> Trigger { get; private set; } = trigger;
    internal bool Current => !retired && eligible() && owner.ModelProposalCurrent(control, expires);
    internal bool CanAccept { get { lock (gate) { return !taken && Current && current.Review == MemoryReviewState.Proposed; } } }
    internal bool CanAdmit { get { lock (gate) { return !taken && Current && current.Review == MemoryReviewState.Reviewed; } } }
    internal string Description => Current ? "Lower-trust MODEL proposal. Scope: this exact SESSION only. "
        + "Inspection and acceptance do not admit, recall or disclose memory. Verify the value is explicitly yours, "
        + "not an inferred trait, secret, health detail, transient task or model claim. Reject if it is not appropriate.\n"
        + "Complete retained original USER and ASSISTANT source (including execution/turn/channel lineage):\n"
        + JsonSerializer.Serialize(source, ReviewFormat)
        + "\nOriginal USER (complete exact text):\n" + source.Body
        + "\nASSISTANT (complete exact inert answer):\n" + source.AssistantResponse!.Body
        + "\nComplete candidate, classification, session scope and current review:\n"
        + JsonSerializer.Serialize(current, ReviewFormat)
        + "\nExact proposed value:\n" + current.Candidate!.Value : string.Empty;

    internal MemoryRecord Take(bool admit, HostId<RequestIdentity> request)
    {
        lock (gate)
        {
            if (request == reviewTrigger || request == source.Request.RequestId || request == Trigger || !(admit ? CanAdmit : CanAccept))
            { throw new InvalidOperationException("A separate fresh original-user action and current exact model-memory review are required."); }
            taken = true;
            Trigger = request;
            return current;
        }
    }

    internal void Accepted(MemoryRecord reviewed)
    {
        lock (gate)
        {
            current = reviewed;
            taken = false;
        }
    }

    internal void Transitioned(MemoryRecord record) { lock (gate) { current = record; } }

    public void Dispose()
    {
        lock (gate)
        {
            retired = true;
            owner.Admission.RetireModelProposal(current);
        }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Storage;

namespace Kora.Application.Memory;

/// <summary>One volatile original-user scope-change review; source content never supplies authority.</summary>
internal sealed class SessionMemoryCopyReview(
    MemoryManagementService owner, SessionWorkspaceEntry target, MemoryRecord source,
    HostId<RequestIdentity> trigger, long control, DateTimeOffset expires, Func<bool> eligible) : IDisposable
{
    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
    private readonly Lock gate = new();
    private volatile bool retired;
    private bool taken;
    private HostId<RequestIdentity> lastInput = trigger;
    internal MemoryManagementService Owner => owner;
    internal SessionWorkspaceEntry Target => target;
    internal MemoryRecord Source => source;
    internal MemoryRecord? Candidate { get; private set; }
    internal bool Current => !retired && eligible() && owner.ModelProposalCurrent(control, expires);
    internal bool CanPropose => Current && !taken && Candidate is null;
    internal bool CanAccept => Current && !taken && Candidate?.Review == MemoryReviewState.Proposed;
    internal bool CanAdmit => Current && !taken && Candidate?.Review == MemoryReviewState.Reviewed;
    internal string Description => !Current ? string.Empty
        : "Keep for later sessions in this device profile.\n"
            + "Scope changes from this SESSION to a NEW DeviceProfile memory on this same device profile. "
            + "The Session original is unchanged and follows its own session deletion/retention. "
            + "After separate admission the new profile memory has an independent lifetime: deleting or forgetting "
            + "the original session memory does NOT delete the profile copy. Disable or forget that copy separately. "
            + "No source receipt, grant, review, expiry or use authority is inherited. No automatic use, model read, "
            + "hosted disclosure or activation. Later one-shot local use remains a separate explicit review.\n"
            + "Treat the original as lower-trust candidate content. Choose a FRESH classification; do not retain "
            + "credentials, secrets, health information, inferred traits, transient tasks or model claims. "
            + "Classification is not secrecy detection. Source changes before admission retire this copy.\n"
            + "Complete current SESSION source, classification, lineage and receipt:\n"
            + JsonSerializer.Serialize(source, Format) + "\nExact source value:\n" + source.Candidate!.Value
            + "\nComplete NEW unadmitted profile candidate (if proposed):\n"
            + JsonSerializer.Serialize(Candidate, Format);

    internal MemoryRecord? Take(HostId<RequestIdentity> input, MemoryOperation operation)
    {
        lock (gate)
        {
            if (input == lastInput || input == source.Lineage.Request.RequestId
                || !(operation == MemoryOperation.Propose ? CanPropose
                    : operation == MemoryOperation.Review ? CanAccept : CanAdmit))
            {
                throw new InvalidOperationException("A current exact scope-change review and separate fresh original-user action are required.");
            }
            lastInput = input;
            taken = true;
            return Candidate;
        }
    }

    internal void Transitioned(MemoryRecord candidate)
    {
        lock (gate) { Candidate = candidate; taken = false; }
    }

    public void Dispose()
    {
        lock (gate)
        {
            retired = true;
            if (Candidate is { } record) { owner.RetireSessionCopy(record); }
        }
    }
}

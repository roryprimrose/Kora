using System.Text;
using System.Text.Json;
using Kora.Core.Hosting;

namespace Kora.Core.Memory;

public static class MemoryPolicy
{
    public const int MaximumValueCharacters = 512;
    public const int MaximumValueUtf8Bytes = 1024;
    public const int MaximumCandidateUtf8Bytes = 2048;
    public const int MaximumEntries = 128;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static MemoryReason ValidateCandidate(MemoryCandidate? candidate)
    {
        if (candidate is null || candidate.ContentClass is not (MemoryContentClass.ExplicitFact
            or MemoryContentClass.ResponsePreference or MemoryContentClass.WorkflowPreference or MemoryContentClass.Decision))
        {
            return MemoryReason.ContentForbidden;
        }
        var value = candidate.Value;
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            return MemoryReason.ValueInvalid;
        }
        if (value.Length > MaximumValueCharacters)
        {
            return MemoryReason.ValueLimitExceeded;
        }
        int bytes;
        try { bytes = StrictUtf8.GetByteCount(value); }
        catch (EncoderFallbackException) { return MemoryReason.ValueInvalid; }
        if (bytes > MaximumValueUtf8Bytes)
        {
            return MemoryReason.ValueLimitExceeded;
        }
        return JsonSerializer.SerializeToUtf8Bytes(candidate).Length > MaximumCandidateUtf8Bytes
            ? MemoryReason.PayloadLimitExceeded : MemoryReason.None;
    }

    public static MemoryReason CheckBoundary(MemoryScope? scope, MemoryBoundary? boundary)
    {
        if (boundary is null || boundary.Profile.Value == Guid.Empty || boundary.Session.Value == Guid.Empty
            || boundary.Generation.Value <= 0 || boundary.Revision <= 0 || !boundary.IsPrivate || !boundary.IsOwner)
        {
            return MemoryReason.AuthorityClosed;
        }
        if (!boundary.LineageKnown || (boundary.Source.HasValue != boundary.SourceRevision.HasValue)
            || boundary.Source is { Value: var source } && source == Guid.Empty
            || boundary.SourceRevision is { Value: <= 0 })
        {
            return MemoryReason.LineageUnknown;
        }
        var matches = scope?.Kind switch
        {
            MemoryScopeKind.Session => scope.Identity == boundary.Session.Value,
            MemoryScopeKind.DeviceProfile => scope.Identity == boundary.Profile.Value,
            MemoryScopeKind.Project => boundary.Project is { Value: var project }
                && project != Guid.Empty && scope.Identity == project,
            MemoryScopeKind.Source => boundary.Source is { Value: var sourceId }
                && scope.Identity == sourceId,
            _ => false,
        };
        return matches ? MemoryReason.None : MemoryReason.ScopeMismatch;
    }

    public static MemoryReason Eligible(MemoryRecord memory, MemoryBoundary? boundary, MemoryDestination destination)
    {
        ArgumentNullException.ThrowIfNull(memory);
        var reason = CheckBoundary(memory.Scope, boundary);
        if (reason != MemoryReason.None) { return reason; }
        reason = CheckLineage(memory, boundary!);
        if (reason != MemoryReason.None) { return reason; }
        if (memory.Review != MemoryReviewState.Admitted) { return MemoryReason.NotReviewed; }
        if (memory.Retention != MemoryRetentionState.Enabled) { return MemoryReason.NotEnabled; }
        if (memory.Id.Value == Guid.Empty || memory.Revision.Value <= 0
            || memory.Receipt is null || memory.Receipt.Request.Value == Guid.Empty
            || memory.Receipt.Revision.Value <= 0 || memory.Receipt.Revision.Value >= memory.Revision.Value)
        {
            return MemoryReason.LineageUnknown;
        }
        var validation = ValidateCandidate(memory.Candidate);
        if (validation != MemoryReason.None) { return validation; }
        // Local retention is never authority for any hosted destination, including an unknown destination.
        return destination == MemoryDestination.Local ? MemoryReason.None : MemoryReason.DisclosureNotAdmitted;
    }

    public static MemoryReason CheckLineage(MemoryRecord memory, MemoryBoundary boundary)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(boundary);
        if (memory.Lineage.SessionGeneration.Value <= 0
            || memory.Lineage.Origin is not (MemoryProposalOrigin.User or MemoryProposalOrigin.Model)
            || memory.Lineage.Source.HasValue != memory.Lineage.SourceRevision.HasValue
            || memory.Lineage.SourceRevision is { Value: <= 0 })
        {
            return MemoryReason.LineageUnknown;
        }
        if (memory.Lineage.Profile != boundary.Profile
            || memory.Scope.Kind == MemoryScopeKind.Session && memory.Lineage.SessionGeneration != boundary.Generation)
        {
            return MemoryReason.ScopeMismatch;
        }
        if (memory.Lineage.Source is { } source
            && (source.Value != boundary.Source.GetValueOrDefault().Value
                || memory.Lineage.SourceRevision.GetValueOrDefault() != boundary.SourceRevision.GetValueOrDefault()))
        {
            return MemoryReason.LineageUnknown;
        }
        if (memory.Review is MemoryReviewState.Proposed or MemoryReviewState.Reviewed
            && (memory.Lineage.Request.SessionId != boundary.Session || memory.Lineage.SessionGeneration != boundary.Generation))
        {
            return MemoryReason.ScopeMismatch;
        }
        return MemoryReason.None;
    }

    public static bool CanAdmit(MemoryRecord memory, MemoryBoundary boundary)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(boundary);
        return memory.Review == MemoryReviewState.Reviewed && memory.Retention == MemoryRetentionState.Pending
            && memory.Receipt is { } receipt && receipt.Revision == memory.Revision && receipt.Boundary == boundary;
    }
}

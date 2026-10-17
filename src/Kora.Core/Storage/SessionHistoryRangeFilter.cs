using System.Runtime.InteropServices;
using System.Text.Json;

namespace Kora.Core.Storage;

/// <summary>Passive inclusive receipt provenance bounds; null means Any, not authority.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct SessionHistoryRangeFilter(
    long? MinimumSequence = null, long? MaximumSequence = null,
    long? MinimumGeneration = null, long? MaximumGeneration = null,
    long? MinimumSourceRevision = null, long? MaximumSourceRevision = null,
    long? MinimumAuditSequence = null, long? MaximumAuditSequence = null)
{
    public void Validate()
    {
        SessionHistoryEvent.ValidateProvenance(MinimumSequence ?? 1, MinimumGeneration ?? 1,
            MinimumSourceRevision ?? 0, MinimumAuditSequence);
        SessionHistoryEvent.ValidateProvenance(MaximumSequence ?? 1, MaximumGeneration ?? 1,
            MaximumSourceRevision ?? 0, MaximumAuditSequence);
        if (MinimumSequence > MaximumSequence || MinimumGeneration > MaximumGeneration
            || MinimumSourceRevision > MaximumSourceRevision || MinimumAuditSequence > MaximumAuditSequence)
        { throw new InvalidDataException("Receipt provenance ranges require minimum bounds not to exceed maximum bounds."); }
    }

    public bool Matches(SessionHistoryEvent record) =>
        Includes(record.Sequence, MinimumSequence, MaximumSequence)
        && Includes(record.Generation.Value, MinimumGeneration, MaximumGeneration)
        && Includes(record.SourceRevision, MinimumSourceRevision, MaximumSourceRevision)
        && (MinimumAuditSequence is null && MaximumAuditSequence is null
            || record.AuditSequence is { } audit && Includes(audit, MinimumAuditSequence, MaximumAuditSequence));

    public static SessionHistoryRangeFilter Parse(string minimumSequence, string maximumSequence,
        string minimumGeneration, string maximumGeneration, string minimumSourceRevision, string maximumSourceRevision,
        string minimumAuditSequence, string maximumAuditSequence)
    {
        var filter = new SessionHistoryRangeFilter(
            OptionalUnsignedDecimal.Parse(minimumSequence), OptionalUnsignedDecimal.Parse(maximumSequence),
            OptionalUnsignedDecimal.Parse(minimumGeneration), OptionalUnsignedDecimal.Parse(maximumGeneration),
            OptionalUnsignedDecimal.Parse(minimumSourceRevision), OptionalUnsignedDecimal.Parse(maximumSourceRevision),
            OptionalUnsignedDecimal.Parse(minimumAuditSequence), OptionalUnsignedDecimal.Parse(maximumAuditSequence));
        filter.Validate();
        return filter;
    }

    internal string CanonicalValues => JsonSerializer.Serialize(this);

    private static bool Includes(long value, long? minimum, long? maximum) =>
        (minimum is not { } lower || value >= lower) && (maximum is not { } upper || value <= upper);
}

using Kora.Core.Configuration;

namespace Kora.Core.Diagnostics;

/// <summary>Only newly due committed records count; no content crosses this aggregation boundary.</summary>
public sealed class AuditRetentionImpact
{
    public const int MaximumRows = 4096;
    public const int MaximumEncodedBytes = 4 * 1024 * 1024;
    public static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan PreviewLifetime = TimeSpan.FromSeconds(30);
    private readonly AuditRetentionDays proposed;
    private readonly DateTimeOffset cutoff;
    private int rows;
    private int bytes;
    private bool incomplete;
    private AuditImpactAggregate aggregate = new(0, null, null, null, null);

    public AuditRetentionImpact(AuditRetentionDays proposed, DateTimeOffset cutoff)
    {
        this.proposed = new(proposed.Days);
        this.cutoff = cutoff.ToUniversalTime();
    }

    public bool Add(DateTimeOffset committed, DateTimeOffset originalDue, int encodedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(encodedBytes);
        if (incomplete || rows == MaximumRows || encodedBytes > MaximumEncodedBytes - bytes)
        {
            incomplete = true;
            return false;
        }
        if (!AuditRetentionDays.IsValidDeadline(committed, originalDue) || committed > cutoff)
        {
            throw new InvalidDataException("Committed audit timing is invalid or in the future.");
        }
        rows++;
        bytes += encodedBytes;
        if (proposed.Days >= (originalDue - committed).TotalDays) { return true; }
        var due = committed.ToUniversalTime().AddDays(proposed.Days);
        if (due <= cutoff && cutoff < originalDue)
        {
            committed = committed.ToUniversalTime();
            aggregate = new(aggregate.AffectedCount + 1,
                aggregate.OldestCommittedUtc is { } oldest && oldest < committed ? oldest : committed,
                aggregate.NewestCommittedUtc is { } newest && newest > committed ? newest : committed,
                aggregate.EarliestDueUtc is { } first && first < due ? first : due,
                aggregate.LatestDueUtc is { } last && last > due ? last : due);
        }
        return true;
    }

    public AuditImpactResult Finish(AuditImpactSource source, string snapshot)
    {
        if (!Enum.IsDefined(source)) { throw new ArgumentOutOfRangeException(nameof(source)); }
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot);
        return new(source, incomplete ? AuditImpactStatus.Incomplete : AuditImpactStatus.Complete,
            incomplete ? null : aggregate, snapshot);
    }

    public static void Validate(AuditImpactResult result, AuditImpactSource source,
        AuditRetentionDays proposed, DateTimeOffset cutoff)
    {
        if (result.Source != source || !Enum.IsDefined(result.Status)
            || result.Status != AuditImpactStatus.Complete && result.Aggregate is not null
            || result.Status == AuditImpactStatus.Unavailable && result.Snapshot is not null)
        {
            throw new InvalidDataException("The audit impact source/status is invalid.");
        }
        if (result.Status != AuditImpactStatus.Complete) { return; }
        if (string.IsNullOrWhiteSpace(result.Snapshot) || result.Aggregate is not { } value
            || value.AffectedCount is < 0 or > MaximumRows)
        {
            throw new InvalidDataException("The complete audit aggregate is invalid.");
        }
        if (value.AffectedCount == 0)
        {
            if (value.OldestCommittedUtc is not null || value.NewestCommittedUtc is not null
                || value.EarliestDueUtc is not null || value.LatestDueUtc is not null)
            {
                throw new InvalidDataException("Zero audit impact must have no timestamp ranges.");
            }
        }
        else if (value.OldestCommittedUtc is not { } oldest || value.NewestCommittedUtc is not { } newest
            || oldest > newest || value.EarliestDueUtc != oldest.AddDays(proposed.Days)
            || value.LatestDueUtc != newest.AddDays(proposed.Days) || newest.AddDays(proposed.Days) > cutoff)
        {
            throw new InvalidDataException("The audit impact timestamp ranges are invalid.");
        }
    }
}

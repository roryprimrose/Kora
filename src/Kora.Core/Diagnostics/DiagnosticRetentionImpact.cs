using Kora.Core.Configuration;

namespace Kora.Core.Diagnostics;

/// <summary>Content-free, bounded hypothetical impact; committed deadlines remain authoritative.</summary>
public sealed class DiagnosticRetentionImpact
{
    public const int MaximumRows = 4096;
    public const int MaximumEncodedBytes = 4 * 1024 * 1024;
    public static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan PreviewLifetime = TimeSpan.FromSeconds(30);
    private readonly DiagnosticRetentionDays proposed;
    private readonly DateTimeOffset cutoff;
    private int rows;
    private int bytes;
    private bool incomplete;
    private DiagnosticImpactAggregate aggregate = new(0, null, null, null, null);

    public DiagnosticRetentionImpact(DiagnosticRetentionDays proposed, DateTimeOffset cutoff)
    {
        this.proposed = new(proposed.Days);
        this.cutoff = cutoff.ToUniversalTime();
    }

    // Check before materializing/decoding an envelope, including the single overflow sentinel.
    public bool Admit(int encodedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(encodedBytes);
        if (incomplete || rows == MaximumRows || encodedBytes > MaximumEncodedBytes - bytes)
        {
            incomplete = true;
            return false;
        }
        rows++;
        bytes += encodedBytes;
        return true;
    }

    public void Add(DateTimeOffset committed, DateTimeOffset originalDue)
    {
        if (!DiagnosticRetentionDays.IsValidDeadline(committed, originalDue) || committed > cutoff)
        {
            throw new InvalidDataException("Committed diagnostic timing is invalid.");
        }
        // Subtraction avoids overflowing DateTimeOffset near its upper bound.
        if (originalDue <= cutoff || cutoff - committed < TimeSpan.FromDays(proposed.Days)) { return; }
        var due = committed.ToUniversalTime().AddDays(proposed.Days);
        committed = committed.ToUniversalTime();
        aggregate = new(checked(aggregate.NewlyDueCount + 1),
            aggregate.OldestCommittedUtc is { } oldest && oldest < committed ? oldest : committed,
            aggregate.NewestCommittedUtc is { } newest && newest > committed ? newest : committed,
            aggregate.EarliestProposedDueUtc is { } first && first < due ? first : due,
            aggregate.LatestProposedDueUtc is { } last && last > due ? last : due);
    }

    public DiagnosticImpactResult Finish(DiagnosticImpactSource source)
    {
        if (!Enum.IsDefined(source)) { throw new ArgumentOutOfRangeException(nameof(source)); }
        return new(source, incomplete ? DiagnosticImpactStatus.Incomplete : DiagnosticImpactStatus.Complete,
            incomplete ? null : aggregate);
    }

    public static void Validate(DiagnosticImpactSnapshot snapshot, DiagnosticRetentionDays proposed, DateTimeOffset cutoff)
    {
        _ = new DiagnosticRetentionDays(proposed.Days);
        ValidateResult(snapshot.Logs, DiagnosticImpactSource.OrdinaryLogs, proposed, cutoff);
        ValidateResult(snapshot.Spans, DiagnosticImpactSource.Spans, proposed, cutoff);
        ValidateResult(snapshot.Links, DiagnosticImpactSource.OwnedLinks, proposed, cutoff);
        if (snapshot.Spans.Status != snapshot.Links.Status)
        {
            throw new InvalidDataException("The diagnostic owner/link qualifications disagree.");
        }
        if (snapshot.Links.Aggregate is { NewlyDueCount: > 0 } owned)
        {
            // Equal Complete qualifications and the validation above guarantee non-null positive ranges.
            var owners = snapshot.Spans.Aggregate!;
            if (owners.NewlyDueCount == 0 || owned.OldestCommittedUtc!.Value < owners.OldestCommittedUtc!.Value
                || owned.NewestCommittedUtc!.Value > owners.NewestCommittedUtc!.Value)
            {
                throw new InvalidDataException("The diagnostic links lack qualified affected owners.");
            }
        }
        if (snapshot.Snapshot is null && (snapshot.Logs.Status != DiagnosticImpactStatus.Unavailable
                || snapshot.Spans.Status != DiagnosticImpactStatus.Unavailable)
            || snapshot.Snapshot is not null && string.IsNullOrWhiteSpace(snapshot.Snapshot))
        {
            throw new InvalidDataException("The diagnostic snapshot or link qualification is invalid.");
        }
    }

    private static void ValidateResult(DiagnosticImpactResult result, DiagnosticImpactSource source,
        DiagnosticRetentionDays proposed, DateTimeOffset cutoff)
    {
        if (result.Source != source || !Enum.IsDefined(result.Status))
        {
            throw new InvalidDataException("The diagnostic source/status is invalid.");
        }
        if (result.Status != DiagnosticImpactStatus.Complete)
        {
            if (result.Aggregate is not null) { throw new InvalidDataException("An unqualified source must not expose counts."); }
            return;
        }
        if (result.Aggregate is not { } value || value.NewlyDueCount is < 0 or > MaximumRows)
        {
            throw new InvalidDataException("The complete diagnostic aggregate is invalid.");
        }
        if (value.NewlyDueCount == 0)
        {
            if (value.OldestCommittedUtc is not null || value.NewestCommittedUtc is not null
                || value.EarliestProposedDueUtc is not null || value.LatestProposedDueUtc is not null)
            {
                throw new InvalidDataException("Zero diagnostic impact must have no ranges.");
            }
        }
        else if (value.OldestCommittedUtc is not { } oldest || value.NewestCommittedUtc is not { } newest
            || oldest > newest || newest > cutoff || cutoff - newest < TimeSpan.FromDays(proposed.Days)
            || value.EarliestProposedDueUtc != oldest.AddDays(proposed.Days)
            || value.LatestProposedDueUtc != newest.AddDays(proposed.Days))
        {
            throw new InvalidDataException("The diagnostic impact ranges are invalid.");
        }
    }
}

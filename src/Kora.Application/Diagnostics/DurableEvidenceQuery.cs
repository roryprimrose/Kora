using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Diagnostics;

public sealed partial class DurableEvidenceQuery(
    IEvidenceReader reader, IEvidenceQueryAccess access, TimeProvider time, ILogger<DurableEvidenceQuery> logger)
{
    private static readonly JsonSerializerOptions Format = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter() },
    };
    private readonly byte[] cursorKey = RandomNumberGenerator.GetBytes(32);

    internal DurableEvidenceQuery(IEvidenceReader reader, IEvidenceQueryAccess access, TimeProvider time,
        ILogger<DurableEvidenceQuery> logger, byte[] cursorKey) : this(reader, access, time, logger) =>
        this.cursorKey = cursorKey.ToArray();

    public static byte[] Serialize(EvidencePage page) => JsonSerializer.SerializeToUtf8Bytes(page, Format);

    public async ValueTask<EvidencePage> QueryAsync(EvidenceQuery query, string? cursor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var current = HostActivity.RequireCurrent();
        if (current.Activity!.IsStopped)
        {
            throw new InvalidOperationException("Evidence queries cannot reuse a stopped host activity.");
        }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Evidence);
        try
        {
            RequireAccess(current.Request);
            Validate(query);
            cancellationToken.ThrowIfCancellationRequested();
            var now = time.GetUtcNow();
            var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(query, Format)));
            var continuation = cursor is null ? null : Decode(cursor, fingerprint, current.Request.SessionId, now);
            var unavailable = query.Source == EvidenceSource.All
                ? new[] { EvidenceSource.Session, EvidenceSource.Conversation }
                : query.Source is EvidenceSource.Session or EvidenceSource.Conversation ? [query.Source] : [];
            if (query.Source is EvidenceSource.Session or EvidenceSource.Conversation)
            {
                activity.Complete(HostOperationOutcome.Completed);
                return new(EvidencePageStatus.Unavailable, [], null, unavailable, EvidencePage.StorageDisclosure);
            }
            var batch = await reader.ReadAsync(query, continuation?.Checkpoint, current.Request, now, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            RequireAccess(current.Request);
            var records = batch.Candidates.Take(query.Limit).Select(item => item.Record).ToList();
            EvidencePage page;
            do
            {
                var more = batch.HasMore || records.Count < batch.Candidates.Count;
                var after = records.Count == 0 ? batch.ScannedThrough : batch.Candidates[records.Count - 1].Position;
                var next = more ? Encode(new(fingerprint, current.Request.SessionId,
                    continuation?.ExpiresUtc ?? now.AddMinutes(15), new(batch.Snapshot, after))) : null;
                var status = batch.ScanLimitReached ? EvidencePageStatus.ScanLimitReached
                    : records.Count == 0 && (query.Record is not null || query.TraceId is not null)
                        ? EvidencePageStatus.MissingOrRemoved : EvidencePageStatus.Available;
                page = new(status,
                    records.ToArray(), next, unavailable, EvidencePage.StorageDisclosure);
                if (Serialize(page).Length <= EvidencePage.MaximumBytes) { break; }
                if (records.Count == 1)
                {
                    if (records[0].ContentOmitted)
                    {
                        throw new InvalidDataException("Evidence metadata exceeds the serialized result budget.");
                    }
                    records[0] = records[0] with
                    {
                        ContentOmitted = true, Text = null, Properties = new Dictionary<string, EvidenceValue>(StringComparer.Ordinal),
                    };
                }
                else { records.RemoveAt(records.Count - 1); }
            } while (true);
            cancellationToken.ThrowIfCancellationRequested();
            RequireAccess(current.Request);
            Returned(logger, page.Records.Count, Serialize(page).Length, page.Status);
            activity.Complete(HostOperationOutcome.Completed);
            return page;
        }
        catch (FileNotFoundException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireAccess(current.Request);
            Failed(logger, nameof(FileNotFoundException));
            activity.Complete(HostOperationOutcome.Failed);
            var sources = query.Source == EvidenceSource.All
                ? Enum.GetValues<EvidenceSource>().Where(source => source != EvidenceSource.All).ToArray() : [query.Source];
            return new(EvidencePageStatus.Unavailable, [], null, sources,
                "The private evidence store is unavailable; no replacement was created. " + EvidencePage.StorageDisclosure);
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failed(logger, exception.GetType().Name);
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    private void RequireAccess(HostRequest request)
    {
        if (!access.CanInspect || request.Origin != RequestOrigin.LocalUi)
        {
            throw new InvalidOperationException("Evidence inspection requires live local-UI ownership and privacy admission.");
        }
    }

    private string Encode(Continuation continuation)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(continuation, Format);
        return Convert.ToBase64String(bytes) + "." + Convert.ToBase64String(HMACSHA256.HashData(cursorKey, bytes));
    }

    private Continuation Decode(string cursor, string fingerprint, HostId<SessionIdentity> session, DateTimeOffset now)
    {
        try
        {
            if (cursor.Length > 2048) { throw new InvalidDataException("The evidence cursor exceeds its bound."); }
            var parts = cursor.Split('.');
            if (parts.Length != 2) { throw new InvalidDataException("The evidence cursor is malformed."); }
            var bytes = Convert.FromBase64String(parts[0]);
            if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(cursorKey, bytes), Convert.FromBase64String(parts[1])))
            {
                throw new InvalidDataException("The evidence cursor is not host-issued.");
            }
            var value = JsonSerializer.Deserialize<Continuation>(bytes, Format)
                ?? throw new InvalidDataException("The evidence cursor is missing.");
            if (!string.Equals(value.Fingerprint, fingerprint, StringComparison.Ordinal)
                || value.Session != session || value.ExpiresUtc <= now)
            {
                throw new InvalidDataException("The evidence cursor is expired or belongs to another query/session.");
            }
            return value;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new InvalidDataException("The evidence cursor is malformed.", exception);
        }
    }

    internal static void Validate(EvidenceQuery query)
    {
        if (!Enum.IsDefined(query.Source) || query.Limit is < 1 or > EvidencePage.MaximumRecords
            || query.FromUtc > query.UntilUtc || query.Severity is { } severity && !Enum.IsDefined(severity)
            || query.AuditOutcome is { } outcome && !Enum.IsDefined(outcome))
        {
            throw new ArgumentException("The evidence source, limits, time range or typed filters are invalid.", nameof(query));
        }
        query.SessionId?.Validate();
        query.TaskId?.Validate();
        query.RequestId?.Validate();
        query.InvocationId?.Validate();
        if (query.ApprovalId == Guid.Empty || query.CorrelationId == Guid.Empty)
        {
            throw new ArgumentException("Evidence correlation identifiers cannot be empty.", nameof(query));
        }
        W3C(query.TraceId, 32);
        W3C(query.SpanId, 16);
        Text(query.Text, 256);
        Text(query.Category, 256);
        Text(query.ActionId, 128);
        if (query.Record is { } record)
        {
            record.Id.Validate();
            if (record.Source is not (EvidenceSource.Log or EvidenceSource.Audit or EvidenceSource.Span or EvidenceSource.Link)
                || (record.Source == EvidenceSource.Link ? record.LinkOrdinal is not (>= 0 and <= 31) : record.LinkOrdinal is not null))
            {
                throw new ArgumentException("The evidence citation is invalid.", nameof(query));
            }
        }
        if (query.Property is { } property)
        {
            Text(property.Name, 128);
            if (string.IsNullOrWhiteSpace(property.Name) || EvidenceFieldPolicy.IsSensitive(property.Name)
                || property.Value is null)
            {
                throw new ArgumentException("The evidence property filter is invalid.", nameof(query));
            }
            EvidenceFieldPolicy.ValidateValue(property.Value);
            Text(property.Value.CanonicalValue, 1024);
        }
    }

    private static void Text(string? value, int limit)
    {
        if (value is not null && (value.Length > limit || value.Any(char.IsControl)))
        {
            throw new ArgumentException("Evidence text filters require bounded plain text.", nameof(value));
        }
        if (value is not null) { _ = new UTF8Encoding(false, true).GetByteCount(value); }
    }

    private static void W3C(string? value, int length)
    {
        if (value is not null && (value.Length != length || value.All(character => character == '0')
            || value.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))))
        {
            throw new ArgumentException("The evidence W3C correlation filter is invalid.", nameof(value));
        }
    }

    private sealed record Continuation(string Fingerprint, HostId<SessionIdentity> Session,
        DateTimeOffset ExpiresUtc, EvidenceReadCheckpoint Checkpoint);

    [LoggerMessage(180, LogLevel.Information, "Evidence query returned {RecordCount} records, {SerializedBytes} serialized bytes, status {QueryStatus}.")]
    private static partial void Returned(ILogger logger, int recordCount, int serializedBytes, EvidencePageStatus queryStatus);

    [LoggerMessage(181, LogLevel.Error, "Evidence query failed; exception type {ExceptionType}.")]
    private static partial void Failed(ILogger logger, string exceptionType);
}

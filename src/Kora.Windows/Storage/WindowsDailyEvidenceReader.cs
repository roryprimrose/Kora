using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.Coordination;

using Microsoft.Win32.SafeHandles;

namespace Kora.Windows.Storage;

public sealed partial class WindowsDailyEvidenceReader : IEvidenceReader
{
    public const int MaximumFiles = 32;
    public const int MaximumPrefixBytes = 8 * 1024 * 1024;
    public const int MaximumLineBytes = 256 * 1024;
    public const int MaximumLines = 4096;
    public const int MaximumSnapshots = 8;
    public static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(5);
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly RestrictedStorageDirectory admission;
    private readonly string root;
    private readonly TimeProvider time;
    private readonly Dictionary<string, Snapshot> snapshots = new(StringComparer.Ordinal);
    private readonly Lock snapshotLock = new();

    public WindowsDailyEvidenceReader(IApplicationDataPaths paths, TimeProvider? timeProvider = null)
    {
        admission = new(paths, includeKeys: false, partitionName: DailyLogFilePolicy.DirectoryName);
        root = admission.Root;
        time = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<EvidenceReadBatch> ReadAsync(EvidenceQuery query, EvidenceReadCheckpoint? checkpoint,
        HostRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var current = HostActivity.RequireCurrent();
        if (current.Activity!.IsStopped || current.Request != request || request.Origin != RequestOrigin.LocalUi)
        {
            throw new InvalidOperationException("Daily evidence requires the admitted live local-UI request.");
        }
        var started = time.GetTimestamp();
        using var deadline = new CancellationTokenSource(MaximumDuration, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var snapshotId = checkpoint?.Snapshot.DailySnapshotId;
        try
        {
            Check(started, linked.Token);
            lock (snapshotLock)
            {
                foreach (var key in snapshots.Where(pair => pair.Value.Expires <= now).Select(pair => pair.Key).ToArray())
                {
                    snapshots.Remove(key);
                }
            }
            Snapshot snapshot;
            List<byte[]> contents;
            if (checkpoint is null)
            {
                (snapshot, contents) = await CaptureAsync(request.SessionId, now, started, linked.Token).ConfigureAwait(false);
                snapshotId = snapshot.Id;
            }
            else
            {
                lock (snapshotLock)
                {
                    if (snapshotId is null || !snapshots.TryGetValue(snapshotId, out snapshot!)
                        || snapshot.Session != request.SessionId)
                    {
                        return Failure(EvidencePageStatus.SnapshotExpired, snapshotId);
                    }
                }
                contents = await LoadAsync(snapshot, started, linked.Token).ConfigureAwait(false);
            }
            var candidates = new List<EvidenceCandidate>();
            var unsupported = 0;
            var mirrors = 0;
            var gaps = 0;
            var lines = 0;
            for (var fileIndex = 0; fileIndex < contents.Count; fileIndex++)
            {
                var bytes = contents[fileIndex];
                var file = snapshot.Files[fileIndex];
                var offset = 0;
                while (offset < bytes.Length && lines < MaximumLines)
                {
                    Check(started, linked.Token);
                    var end = Array.IndexOf(bytes, (byte)'\n', offset);
                    if (end < 0) { throw new ReadFailure(EvidencePageStatus.Truncated); }
                    if (end - offset > MaximumLineBytes) { throw new ReadFailure(EvidencePageStatus.Corrupt); }
                    var position = new EvidencePosition(fileIndex, EvidenceSource.DailyLog, file.Name, offset);
                    var record = Parse(bytes.AsSpan(offset, end - offset), file, offset, ref unsupported, ref mirrors, ref gaps);
                    lines++;
                    offset = end + 1;
                    if (record is not null && (checkpoint?.After is not { } after
                        || fileIndex > after.CommittedTicks || fileIndex == after.CommittedTicks && position.Ordinal > after.Ordinal)
                        && Matches(record, query, request))
                    {
                        candidates.Add(new(position, record));
                    }
                }
                if (offset < bytes.Length) { snapshot = snapshot with { Limited = true }; }
            }
            // Re-open by trusted name: an old open handle alone would hide replacement or pruning.
            _ = await LoadAsync(snapshot, started, linked.Token).ConfigureAwait(false);
            Check(started, linked.Token);
            if (checkpoint is null)
            {
                lock (snapshotLock)
                {
                    if (snapshots.Count == MaximumSnapshots)
                    {
                        snapshots.Remove(snapshots.MinBy(pair => pair.Value.Expires).Key);
                    }
                    snapshots.Add(snapshot.Id, snapshot);
                }
            }
            var report = new DailyEvidenceReport(snapshot.Id, snapshot.Files.Count,
                snapshot.Files.Sum(file => (long)file.Length), lines, unsupported, mirrors, gaps);
            var status = snapshot.Limited ? EvidencePageStatus.ScanLimitReached
                : unsupported + mirrors + gaps > 0 ? EvidencePageStatus.Partial : (EvidencePageStatus?)null;
            var selected = candidates.Take(query.Limit + 1).ToArray();
            return new(new(0, 0, 0, 0, DailySnapshotId: snapshot.Id), selected,
                selected.LastOrDefault()?.Position ?? checkpoint?.After, candidates.Count > query.Limit,
                snapshot.Limited, status, report);
        }
        catch (ReadFailure failure) { return Failure(failure.Status, snapshotId); }
        catch (FileNotFoundException) { return Failure(checkpoint is null ? EvidencePageStatus.Unavailable : EvidencePageStatus.MissingOrRemoved, snapshotId); }
        catch (DirectoryNotFoundException) { return Failure(checkpoint is null ? EvidencePageStatus.Unavailable : EvidencePageStatus.MissingOrRemoved, snapshotId); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(EvidencePageStatus.TimedOut, snapshotId);
        }
        catch (IOException) { return Failure(EvidencePageStatus.Unavailable, snapshotId); }
    }

    private async Task<(Snapshot Snapshot, List<byte[]> Contents)> CaptureAsync(
        HostId<SessionIdentity> session, DateTimeOffset now, long started, CancellationToken token)
    {
        VerifyDirectory();
        var names = new List<string>();
        foreach (var path in Directory.EnumerateFileSystemEntries(root))
        {
            Check(started, token);
            var name = Path.GetFileName(path);
            if (!DailyLogFilePolicy.TryParseName(name, out _)) { continue; }
            names.Add(name);
            if (names.Count > MaximumFiles) { throw new ReadFailure(EvidencePageStatus.ScanLimitReached); }
        }
        names.Sort(StringComparer.Ordinal);
        var files = new List<Prefix>();
        var contents = new List<byte[]>();
        var budget = MaximumPrefixBytes;
        var limited = false;
        foreach (var name in names)
        {
            Check(started, token);
            await using var stream = Open(name);
            var identity = Identity(stream);
            var length = stream.Length;
            var count = (int)Math.Min(length, budget);
            var bytes = new byte[count];
            await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
            if (length > count)
            {
                limited = true;
                var end = Array.LastIndexOf(bytes, (byte)'\n');
                Array.Resize(ref bytes, end + 1);
            }
            files.Add(new(name, identity, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))));
            contents.Add(bytes);
            budget -= count;
            if (budget == 0)
            {
                limited |= files.Count < names.Count;
                break;
            }
        }
        return (new(Guid.NewGuid().ToString("N"), session, now.AddMinutes(15), files, limited), contents);
    }

    private async Task<List<byte[]>> LoadAsync(Snapshot snapshot, long started, CancellationToken token)
    {
        VerifyDirectory();
        var result = new List<byte[]>();
        foreach (var file in snapshot.Files)
        {
            Check(started, token);
            await using var stream = Open(file.Name);
            if (!string.Equals(Identity(stream), file.Identity, StringComparison.Ordinal) || stream.Length < file.Length)
            {
                throw new ReadFailure(EvidencePageStatus.Changed);
            }
            var bytes = new byte[file.Length];
            await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), file.Digest, StringComparison.Ordinal))
            {
                throw new ReadFailure(EvidencePageStatus.Changed);
            }
            result.Add(bytes);
        }
        return result;
    }

    private void VerifyDirectory()
    {
        RestrictedStorageDirectory.RejectReparseAncestors(root);
        admission.VerifyPermissions(new DirectoryInfo(root).GetAccessControl(
            AccessControlSections.Access | AccessControlSections.Owner), requireProtected: false, allowSystemAdministrators: true);
    }

    private FileStream Open(string name)
    {
        var path = Path.Combine(root, name);
        RestrictedStorageDirectory.RejectReparseAncestors(path);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            if (!string.Equals(CoordinationNative.CanonicalPath(stream.SafeFileHandle), path, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("The daily evidence handle left its trusted store.");
            }
            admission.VerifyPermissions(stream.GetAccessControl(), requireProtected: false, allowSystemAdministrators: true);
            RestrictedStorageDirectory.RejectReparseAncestors(path);
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    private static EvidenceRecord? Parse(ReadOnlySpan<byte> line, Prefix file, int offset,
        ref int unsupported, ref int mirrors, ref int gaps)
    {
        try
        {
            using var json = JsonDocument.Parse(Utf8.GetString(line), new() { MaxDepth = 32 });
            WindowsSqliteEvidenceSink.RequireUnique(json.RootElement);
            var outer = json.RootElement;
            _ = outer.GetProperty("Timestamp").GetDateTimeOffset();
            _ = outer.GetProperty("Level").GetString();
            _ = outer.GetProperty("MessageTemplate").GetString();
            var properties = outer.GetProperty("Properties");
            if (properties.TryGetProperty("EvidenceAuthority", out var authority)
                && string.Equals(authority.GetString(), "TypedAuditCopy", StringComparison.Ordinal))
            {
                mirrors++;
                return null;
            }
            if (!properties.TryGetProperty("EvidenceEnvelope", out var payload))
            {
                if (properties.TryGetProperty("EvidenceGap", out _)) { gaps++; }
                else { unsupported++; }
                return null;
            }
            if (authority.ValueKind != JsonValueKind.String
                || !string.Equals(authority.GetString(), "Diagnostic", StringComparison.Ordinal))
            {
                throw new InvalidDataException("The daily diagnostic authority marker is invalid.");
            }
            var diagnostic = WindowsSqliteEvidenceSink.DecodeDiagnostic(payload.GetString()
                ?? throw new InvalidDataException("The daily diagnostic envelope is missing."));
            var digest = Convert.ToHexString(SHA256.HashData(line));
            var citation = new Guid(SHA256.HashData(Utf8.GetBytes($"{file.Identity}:{offset}:{digest}")).AsSpan(0, 16));
            var values = diagnostic.Properties.ToDictionary(pair => pair.Key,
                pair => EvidenceFieldPolicy.IsSensitive(pair.Key) ? new(EvidenceValueKind.Text, "[redacted]") : pair.Value,
                StringComparer.Ordinal);
            var segments = diagnostic.Trace is { } trace
                ? new[] { new EvidenceSegment(trace.TraceId, trace.SpanId, EvidenceSegmentStatus.Unavailable, null) } : [];
            return new(new(EvidenceSource.DailyLog, new(citation)), null, null,
                EvidenceSegmentStatus.RetentionUnknown, diagnostic.Host, diagnostic.Trace, diagnostic.AuditCorrelationId,
                diagnostic.ApprovalId, diagnostic.Level, diagnostic.Category, diagnostic.EventId,
                diagnostic.MessageTemplate, values, null, null, segments,
                DailyProvenance: new(file.Name, file.Identity, offset, digest, diagnostic.EvidenceId),
                ObservedUtc: diagnostic.ObservedUtc);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or ArgumentException
            or InvalidOperationException or KeyNotFoundException or DecoderFallbackException or FormatException)
        {
            throw new ReadFailure(EvidencePageStatus.Corrupt, exception);
        }
    }

    private static bool Matches(EvidenceRecord record, EvidenceQuery query, HostRequest viewer)
    {
        var host = record.Host;
        if (host?.RequestId == viewer.RequestId || query.Source is not (EvidenceSource.All or EvidenceSource.DailyLog)
            || query.Record is { } reference && reference != record.Reference
            || query.SessionId is { } session && host?.SessionId != session
            || query.TaskId is { } task && host?.TaskId != task
            || query.RequestId is { } request && host?.RequestId != request
            || query.InvocationId is { } invocation && host?.InvocationId != invocation
            || query.ApprovalId is { } approval && record.ApprovalId != approval
            || query.CorrelationId is { } correlation && record.CorrelationId != correlation
            || query.TraceId is { } trace && !string.Equals(record.Trace?.TraceId, trace, StringComparison.Ordinal)
            || query.SpanId is { } span && !string.Equals(record.Trace?.SpanId, span, StringComparison.Ordinal)
            || query.FromUtc is { } from && record.ObservedUtc < from
            || query.UntilUtc is { } until && record.ObservedUtc > until
            || query.Severity is { } severity && !string.Equals(record.Level, severity.ToString(), StringComparison.Ordinal)
            || query.EventId is { } eventId && record.EventId != eventId
            || query.Category is { } category && !string.Equals(record.Category, category, StringComparison.Ordinal)
            || query.ActionId is not null || query.AuditOutcome is not null)
        {
            return false;
        }
        if (query.Property is { } property && (!record.Properties.TryGetValue(property.Name, out var value) || value != property.Value))
        {
            return false;
        }
        return query.Text is not { } text || (record.Text?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
            || record.Properties.Values.Any(value => value.CanonicalValue?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void Check(long started, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (time.GetElapsedTime(started) >= MaximumDuration) { throw new ReadFailure(EvidencePageStatus.TimedOut); }
    }

    private static EvidenceReadBatch Failure(EvidencePageStatus status, string? snapshot) =>
        new(new(0, 0, 0, 0, DailySnapshotId: snapshot), [], null, false, false, status,
            new(snapshot, 0, 0, 0, 0, 0, 0));

    private static string Identity(FileStream stream)
    {
        if (!GetFileInformationByHandleEx(stream.SafeFileHandle, 18, out var id, Marshal.SizeOf<FileIdInfo>()))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The daily evidence file identity is unavailable.");
        }
        return $"{id.Volume:x16}{id.Low:x16}{id.High:x16}";
    }

    private sealed record Prefix(string Name, string Identity, int Length, string Digest);
    private sealed record Snapshot(string Id, HostId<SessionIdentity> Session, DateTimeOffset Expires,
        IReadOnlyList<Prefix> Files, bool Limited);
    private sealed class ReadFailure(EvidencePageStatus status, Exception? inner = null)
        : IOException("The bounded daily evidence source could not be admitted: " + status, inner)
    {
        internal EvidencePageStatus Status { get; } = status;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo
    {
        internal ulong Volume;
        internal ulong Low;
        internal ulong High;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass,
        out FileIdInfo information, int size);
}

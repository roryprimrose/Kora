using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSegmentedEvidenceSink : IBatchedEvidenceSink, IDisposable
{
    internal const string PartitionName = "EvidenceSegmentsV2";
    internal const int MaximumSegments = 128;
    internal const int MaximumInventoryBytes = 128 * 1024;
    internal const int MaximumSegmentPages = 4096;
    internal const int RotationPages = 2048;
    private const string LegacyId = "legacy-v1";
    private const string InventoryName = "inventory.json";
    private const string StagingName = "inventory.pending";
    private static readonly JsonSerializerOptions Format = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8,
    };
    private readonly Lock gate = new();
    private readonly IApplicationDataPaths paths;
    private readonly RestrictedStorageDirectory directory;
    private readonly EvidenceRetentionPolicy? retention;
    private readonly TimeProvider? time;
    private readonly DiagnosticRetentionPolicy? diagnosticPolicy;
    private readonly AuditRetentionPolicy? auditPolicy;
    private readonly int maximumPages;
    private readonly int rotationPages;
    private readonly TimeProvider? integrityTimeProvider;
    private readonly WindowsSqliteEvidenceSink admissionSink;
    private readonly List<Segment> segments = [];
    private FileStream? inventoryLease;
    private FileStream? inventoryPin;
    private byte[]? inventoryDigest;
    private Inventory? inventory;
    private Exception? failure;
    private bool disposed;
    private long activeAudits;

    public WindowsSegmentedEvidenceSink(IApplicationDataPaths paths,
        EvidenceRetentionPolicy? retentionPolicy = null, TimeProvider? timeProvider = null,
        DiagnosticRetentionPolicy? diagnosticPolicy = null, AuditRetentionPolicy? auditPolicy = null)
        : this(paths, retentionPolicy, timeProvider, diagnosticPolicy, auditPolicy,
            MaximumSegmentPages, RotationPages)
    {
    }

    internal WindowsSegmentedEvidenceSink(IApplicationDataPaths paths,
        EvidenceRetentionPolicy? retentionPolicy, TimeProvider? timeProvider,
        DiagnosticRetentionPolicy? diagnosticPolicy, AuditRetentionPolicy? auditPolicy,
        int maximumPages, int rotationPages, TimeProvider? integrityTimeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumPages, 64);
        ArgumentOutOfRangeException.ThrowIfLessThan(rotationPages, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(rotationPages, maximumPages);
        this.paths = paths;
        retention = retentionPolicy;
        time = timeProvider;
        this.diagnosticPolicy = diagnosticPolicy;
        this.auditPolicy = auditPolicy;
        this.maximumPages = maximumPages;
        this.rotationPages = rotationPages;
        this.integrityTimeProvider = integrityTimeProvider;
        directory = new(paths, includeKeys: false, partitionName: PartitionName);
        admissionSink = new(paths, retentionPolicy, timeProvider);
    }

    public string Name => "windows-sqlite";

    public void Initialize()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (inventory is not null) { RequireAvailable(); return; }
            if (failure is not null) { throw new IOException("Evidence segment admission previously failed.", failure); }
            try
            {
                var exists = directory.HasExistingPartition();
                // Validate and pin legacy history before creating any new persistent cutover state.
                if (!exists) { AdmitLegacy(); directory.CreateNew(); }
                inventoryLease = directory.AcquireBoundedLease(requireExisting: exists, CancellationToken.None);
                if (exists) { AdmitExisting(); }
                else { CreateInventory(); }
                VerifyInventory();
                PopulateIdentityFilter();
            }
            catch (Exception exception)
            {
                failure = exception;
                ReleaseOwnedResources();
                throw;
            }
        }
    }

    public void WriteDiagnostic(DiagnosticEnvelope envelope) => PrepareDiagnosticWrite(envelope).Write();
    public Action PrepareDiagnostic(DiagnosticEnvelope envelope) => PrepareDiagnosticWrite(envelope).Write;
    public void WriteActivity(CompletedActivityEnvelope envelope) => PrepareActivityWrite(envelope).Write();
    public Action PrepareActivity(CompletedActivityEnvelope envelope) => PrepareActivityWrite(envelope).Write;

    public IPreparedEvidenceWrite PrepareDiagnosticWrite(DiagnosticEnvelope envelope)
    {
        RequireAvailable();
        return new PreparedWrite(this, admissionSink, admissionSink.PrepareDiagnosticWrite(envelope));
    }

    public IPreparedEvidenceWrite PrepareActivityWrite(CompletedActivityEnvelope envelope)
    {
        RequireAvailable();
        return new PreparedWrite(this, admissionSink, admissionSink.PrepareActivityWrite(envelope));
    }

    public void WriteBatch(IReadOnlyList<IPreparedEvidenceWrite> writes)
    {
        ArgumentNullException.ThrowIfNull(writes);
        if (writes.Count is < 1 or > IBatchedEvidenceSink.MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(writes));
        }
        var admitted = new PreparedWrite[writes.Count];
        for (var index = 0; index < writes.Count; index++)
        {
            admitted[index] = writes[index] is PreparedWrite prepared && ReferenceEquals(prepared.Owner, this)
                ? prepared : throw new ArgumentException("A batch must contain this store's admitted snapshots.", nameof(writes));
        }
        lock (gate)
        {
            RequireAvailable();
            ExecuteWrite(() =>
            {
                var ids = admitted.Select(write => write.EvidenceId.Value).ToArray();
                RequireUniqueEvidence(ids);
                PrepareActive();
                var target = segments[^1].Sink;
                target.WriteBatch(admitted.Select(write => write.Source.RetargetPreparedWrite(write.Inner, target)).ToArray());
                foreach (var id in ids) { RememberIdentity(id); }
            });
        }
    }

    public void WriteAudit(AuditEnvelope envelope)
    {
        WindowsSqliteEvidenceSink.AdmitAudit(envelope);
        lock (gate)
        {
            RequireAvailable();
            ExecuteWrite(() =>
            {
                RequireUniqueEvidence([envelope.Diagnostic.EvidenceId.Value]);
                PrepareActive();
                segments[^1].Sink.WriteAudit(envelope);
                activeAudits = checked(activeAudits + 1);
                RememberIdentity(envelope.Diagnostic.EvidenceId.Value);
            });
        }
    }

    internal T ReadSegments<T>(Func<EvidenceSegmentReadSet, T> read, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(read);
        token.ThrowIfCancellationRequested();
        lock (gate)
        {
            token.ThrowIfCancellationRequested();
            RequireAvailable();
            VerifyInventory();
            foreach (var segment in segments.Where(segment => segment.Entry.Sealed))
            {
                token.ThrowIfCancellationRequested();
                segment.VerifySeal();
            }
            return read(new(inventory!.StoreIdentity, inventory.Generation,
                segments.Select(segment => new EvidenceSegmentReadView(
                    segment.Entry.Id, segment.Entry.AuditBase, segment.Sink)).ToArray()));
        }
    }

    private void ExecuteWrite(Action write)
    {
        try { write(); }
        catch (Exception exception) when (exception is IOException or InvalidDataException
            or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or JsonException
            or OverflowException)
        {
            failure = exception;
            throw new IOException("The bounded evidence store cannot confirm a durable checked write; new writes are held.", exception);
        }
    }

    private void PrepareActive()
    {
        VerifyInventory();
        if (segments[^1].DatabaseLength / 4096 >= rotationPages) { Rotate(); }
    }

    private void RequireAvailable()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed), this);
        if (Volatile.Read(ref failure) is { } error) { throw new IOException("The bounded evidence store is held after an unconfirmed operation.", error); }
        if (Volatile.Read(ref inventory) is null) { throw new InvalidOperationException("Evidence segments have not been admitted."); }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) { return; }
            disposed = true;
            ReleaseOwnedResources();
        }
    }

    private void ReleaseOwnedResources()
    {
        foreach (var segment in segments) { segment.Dispose(); }
        segments.Clear();
        inventoryPin?.Dispose();
        inventoryPin = null;
        inventoryLease?.Dispose();
        inventoryLease = null;
    }

    private sealed record Inventory(int Version, string StoreIdentity, long Generation,
        int MaximumPages, SegmentEntry[] Segments);

    private sealed record SegmentEntry(string Id, bool Sealed, long AuditBase, long AuditCount,
        string? DatabaseDigest, string? JournalDigest, string? DatabaseIdentity, string? JournalIdentity);

    private sealed class PreparedWrite(WindowsSegmentedEvidenceSink owner,
        WindowsSqliteEvidenceSink source, IPreparedEvidenceWrite inner) : IPreparedEvidenceWrite
    {
        public WindowsSegmentedEvidenceSink Owner => owner;
        public WindowsSqliteEvidenceSink Source => source;
        public IPreparedEvidenceWrite Inner => inner;
        public HostId<EvidenceIdentity> EvidenceId => inner.EvidenceId;
        public void Write() => owner.WriteBatch([this]);
    }

    private sealed class SegmentPaths(string localRoot, string roamingRoot) : IApplicationDataPaths
    {
        public string LocalRoot => localRoot;
        public string RoamingRoot => roamingRoot;
    }
}

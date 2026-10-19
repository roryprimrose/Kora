using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSegmentedEvidenceSink
{
    private string InventoryPath => Path.Combine(directory.Root, InventoryName);
    private string StagingPath => Path.Combine(directory.Root, StagingName);

    private Segment NewSegment(SegmentEntry entry)
    {
        var legacy = string.Equals(entry.Id, LegacyId, StringComparison.Ordinal);
        var localPaths = legacy ? paths : new SegmentPaths(directory.Artifacts, paths.RoamingRoot);
        var partition = legacy ? WindowsSqliteEvidenceSink.PartitionName : entry.Id;
        var sink = new WindowsSqliteEvidenceSink(localPaths, partition, retention, time, null,
            diagnosticPolicy, auditPolicy, legacy ? null : maximumPages, integrityTimeProvider);
        return new(entry, sink, new RestrictedStorageDirectory(localPaths, includeKeys: false, partitionName: partition));
    }

    private void AdmitLegacy(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var legacyDirectory = new RestrictedStorageDirectory(paths, includeKeys: false,
            partitionName: WindowsSqliteEvidenceSink.PartitionName);
        if (legacyDirectory.HasExistingPartition())
        {
            var legacy = NewSegment(new(LegacyId, true, 0, 0, null, null, null, null));
            segments.Add(legacy);
            legacy.AdmitAndSeal(initialAdmission: true, cancellationToken);
        }
    }

    private void CreateInventory(Action? requireAuthority = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var auditBase = segments.Count == 0 ? 0 : segments[0].Entry.AuditCount;
        var active = NewSegment(new(SegmentId(0), false, auditBase, 0, null, null, null, null));
        segments.Add(active);
        active.Sink.Initialize();
        inventory = new(2, Guid.NewGuid().ToString("D"), 1, maximumPages,
            segments.Select(segment => segment.Entry).ToArray());
        PublishInventory(first: true, requireAuthority, cancellationToken);
    }

    private void AdmitExisting()
    {
        try { _ = File.GetAttributes(InventoryPath); }
        catch (FileNotFoundException exception)
        {
            throw new InvalidDataException("The evidence inventory is incomplete: committed metadata is missing. Existing files were preserved; explicit supported investigation is required.", exception);
        }
        directory.VerifyFile(InventoryPath);
        inventoryPin = new(InventoryPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = ReadInventoryBytes(inventoryPin);
        inventoryDigest = SHA256.HashData(bytes);
        try
        {
            using var document = JsonDocument.Parse(bytes);
            WindowsSqliteEvidenceSink.RequireUnique(document.RootElement);
            inventory = JsonSerializer.Deserialize<Inventory>(bytes, Format)
                ?? throw new InvalidDataException("The evidence inventory is missing.");
            ValidateInventoryFormat(inventory);
        }
        catch (Exception exception) when (exception is JsonException or OverflowException)
        {
            throw new InvalidDataException("The saved evidence inventory is malformed.", exception);
        }
        VerifyInventory();
        foreach (var entry in inventory.Segments)
        {
            var segment = NewSegment(entry);
            segments.Add(segment);
            if (entry.Sealed) { segment.AdmitAndSeal(initialAdmission: true); }
            else
            {
                segment.Sink.Initialize();
                activeAudits = segment.Sink.ReadSegmentState(initialAdmission: true, CancellationToken.None).AuditCount;
            }
        }
    }

    private static string SegmentId(int ordinal) => "segment-" + ordinal.ToString("D6", CultureInfo.InvariantCulture);

    private void ValidateInventoryFormat(Inventory value)
    {
        if (value.Version != 2 || !Guid.TryParseExact(value.StoreIdentity, "D", out var identity)
            || identity == Guid.Empty || value.Generation < 1 || value.MaximumPages != maximumPages
            || value.Segments is null || value.Segments.Length is < 1 or > MaximumSegments)
        {
            throw new InvalidDataException("The evidence inventory format or capacity is invalid.");
        }
        long auditBase = 0;
        var ordinal = 0;
        for (var index = 0; index < value.Segments.Length; index++)
        {
            var entry = value.Segments[index] ?? throw new InvalidDataException("A segment entry is missing.");
            var legacy = index == 0 && string.Equals(entry.Id, LegacyId, StringComparison.Ordinal);
            if ((!legacy && !string.Equals(entry.Id, SegmentId(ordinal++), StringComparison.Ordinal))
                || entry.AuditBase != auditBase || entry.AuditCount < 0
                || entry.Sealed != (index < value.Segments.Length - 1))
            {
                throw new InvalidDataException("The evidence inventory order or audit continuity is invalid.");
            }
            if (entry.Sealed)
            {
                if (!IsDigest(entry.DatabaseDigest) || !IsDigest(entry.JournalDigest)
                    || string.IsNullOrWhiteSpace(entry.DatabaseIdentity) || entry.DatabaseIdentity.Length > 256
                    || string.IsNullOrWhiteSpace(entry.JournalIdentity) || entry.JournalIdentity.Length > 256)
                {
                    throw new InvalidDataException("A sealed segment lacks its bounded immutable identity.");
                }
            }
            else if (legacy || entry.AuditCount != 0 || entry.DatabaseDigest is not null
                || entry.JournalDigest is not null || entry.DatabaseIdentity is not null || entry.JournalIdentity is not null)
            {
                throw new InvalidDataException("The active segment inventory state is invalid.");
            }
            auditBase = checked(auditBase + entry.AuditCount);
        }
    }

    private static bool IsDigest(string? digest) =>
        digest is { Length: 64 } && digest.All(char.IsAsciiHexDigit);

    private static byte[] ReadInventoryBytes(FileStream stream)
    {
        if (stream.Length is < 1 or > MaximumInventoryBytes)
        {
            throw new InvalidDataException("The evidence inventory exceeds its format bound.");
        }
        stream.Position = 0;
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private void VerifyInventory()
    {
        directory.Verify();
        var current = inventory ?? throw new InvalidOperationException("No evidence inventory is admitted.");
        var rootEntries = Directory.EnumerateFileSystemEntries(directory.Root).Take(5)
            .Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        if (!rootEntries.SetEquals(["Artifacts", "operation.lock", InventoryName]))
        {
            throw new InvalidDataException("The evidence inventory contains unregistered or incomplete publication state.");
        }
        var registered = current.Segments.Where(entry => !string.Equals(entry.Id, LegacyId, StringComparison.Ordinal))
            .Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        var actual = Directory.EnumerateFileSystemEntries(directory.Artifacts).Take(MaximumSegments + 1)
            .Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(registered))
        {
            throw new InvalidDataException("The evidence segment inventory is missing a registered segment or contains an unregistered candidate.");
        }
        var legacyExists = new RestrictedStorageDirectory(paths, includeKeys: false,
            partitionName: WindowsSqliteEvidenceSink.PartitionName).HasExistingPartition();
        if (legacyExists != current.Segments.Any(entry => string.Equals(entry.Id, LegacyId, StringComparison.Ordinal)))
        {
            throw new InvalidDataException("The legacy evidence inventory changed; no replacement is permitted.");
        }
        directory.VerifyFile(InventoryPath);
        if (inventoryPin is null || inventoryDigest is null
            || !CryptographicOperations.FixedTimeEquals(inventoryDigest, SHA256.HashData(ReadInventoryBytes(inventoryPin))))
        {
            throw new InvalidDataException("The pinned evidence inventory changed after admission.");
        }
    }

    private void PublishInventory(bool first, Action? requireAuthority = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        requireAuthority?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        var current = inventory ?? throw new InvalidOperationException("No inventory to publish.");
        ValidateInventoryFormat(current);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(current, Format);
        if (bytes.Length > MaximumInventoryBytes) { throw new InvalidDataException("The evidence inventory is full."); }
        using (var stage = directory.CreateNewFile(StagingPath, FileOptions.WriteThrough))
        {
            stage.Write(bytes);
            stage.Flush(flushToDisk: true);
        }
        directory.VerifyFile(StagingPath);
        requireAuthority?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        inventoryPin?.Dispose();
        inventoryPin = null;
        if (first) { File.Move(StagingPath, InventoryPath, overwrite: false); }
        else { File.Replace(StagingPath, InventoryPath, destinationBackupFileName: null); }
        directory.VerifyFile(InventoryPath);
        inventoryPin = new(InventoryPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var observed = ReadInventoryBytes(inventoryPin);
        if (!observed.AsSpan().SequenceEqual(bytes))
        {
            throw new InvalidDataException("The published evidence inventory does not match its durable staged bytes.");
        }
        inventoryDigest = SHA256.HashData(observed);
    }

    private void Rotate()
    {
        if (segments.Count >= MaximumSegments)
        {
            throw new IOException("The bounded evidence segment inventory is full; no retained evidence was removed.");
        }
        var previous = segments[^1];
        previous.AdmitAndSeal();
        if (previous.Entry.AuditCount != activeAudits)
        {
            throw new InvalidDataException("The active audit sequence changed outside admitted evidence delivery.");
        }
        var ordinal = segments.Count(segment => !string.Equals(segment.Entry.Id, LegacyId, StringComparison.Ordinal));
        var active = NewSegment(new(SegmentId(ordinal), false,
            checked(previous.Entry.AuditBase + activeAudits), 0, null, null, null, null));
        segments.Add(active);
        active.Sink.Initialize();
        inventory = inventory! with
        {
            Generation = checked(inventory.Generation + 1),
            Segments = segments.Select(segment => segment.Entry).ToArray(),
        };
        PublishInventory(first: false);
        activeAudits = 0;
    }
}

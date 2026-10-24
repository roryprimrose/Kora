using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Kora.Core.Customizations;

namespace Kora.Windows.Artifacts;

public sealed partial class WindowsCustomizationStore
{
    private const int MaximumJournalBytes = 4 * 1024 * 1024;
    private sealed record Journal(CustomizationSource Source, string DirectoryIdentity, DiskFile[] Before, DiskFile[] After);
    private string JournalDirectory => Path.Combine(paths.LocalRoot, "DefinitionTransactions");
    private static string JournalName(Target target) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(target.Source.Id))) + ".json";
    private string JournalPath(Target target) => Path.Combine(JournalDirectory, JournalName(target));

    public Task<CustomizationSnapshot> Save(CustomizationSource source, CustomizationSnapshot? expected,
        IReadOnlyList<CustomizationFile> files, Func<bool> eligible,
        Func<Func<CustomizationSnapshot>, Task<CustomizationSnapshot>> commit, CancellationToken token)
    {
        var frozen = CustomizationSnapshot.ValidateFiles(source, files).ToArray();
        return Task.Run(async () => await commit(() =>
        {
            lock (gate) { return Publish(Resolve(source), expected, frozen, eligible, token); }
        }).ConfigureAwait(false), token);
    }

    private CustomizationSnapshot Publish(Target target, CustomizationSnapshot? expected,
        CustomizationFile[] files, Func<bool> eligible, CancellationToken token)
    {
        RequireEligible(eligible, token);
        using var root = WindowsOwnedInstructionStore.PinDefinitionDirectory(target.Directory, true, token);
        using var journalRoot = WindowsOwnedInstructionStore.PinDefinitionDirectory(JournalDirectory, true, token);
        var current = Capture(target, token);
        var existing = ReadJournal(target, token);
        if (expected is not null && (expected.Source != target.Source
            || !string.Equals(expected.Revision, current.Revision, StringComparison.Ordinal))
            || expected is null && Exists(target, current))
        { throw new InvalidOperationException("The complete source changed or the destination appeared. Reload, compare and review again; the draft is retained."); }
        var after = ProposedFiles(target, current, files);
        Journal journal;
        if (existing is not null)
        {
            if (expected?.RecoveryDetails is null || !SameBytes(after, existing.After)
                || !string.Equals(current.DirectoryIdentity, existing.DirectoryIdentity, StringComparison.Ordinal))
            { throw new InvalidOperationException("Review the exact unfinished transaction before recovery; it cannot be retargeted to an edited proposal."); }
            RequireRecoverable(current, existing);
            journal = existing;
        }
        else
        {
            if (Directory.EnumerateFiles(JournalDirectory, "*.json").Take(32).Count() >= 32)
            { throw new InvalidDataException("Recover an existing transaction before creating another pending transaction."); }
            journal = new(target.Source, current.DirectoryIdentity, current.Files.ToArray(), after);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(journal);
            if (bytes.Length > MaximumJournalBytes) { throw new InvalidDataException("The complete transaction exceeds its bounded recovery capacity."); }
            WindowsOwnedInstructionStore.WriteDefinitionFile(JournalDirectory, journalRoot, JournalName(target), bytes, false, eligible, token);
            var verified = WindowsOwnedInstructionStore.ReadDefinitionFile(JournalPath(target), MaximumJournalBytes, token);
            if (verified is null || !verified.Bytes.AsSpan().SequenceEqual(bytes))
            { throw new IOException("Transaction staging is unconfirmed. Review recovery before further writes."); }
        }

        foreach (var name in current.Files.Select(file => file.Name).Union(journal.After.Select(file => file.Name), StringComparer.Ordinal)
            .OrderBy(name => string.Equals(name, target.Source.PrimaryFile, StringComparison.Ordinal))
            .ThenBy(name => journal.After.Any(file => string.Equals(file.Name, name, StringComparison.Ordinal)))
            .ThenBy(name => name, StringComparer.Ordinal))
        {
            RequireEligible(eligible, token);
            var observed = Capture(target, token);
            if (!string.Equals(current.Revision, observed.Revision, StringComparison.Ordinal))
            { throw new InvalidOperationException("An external edit interrupted publication. The transaction remains recovery-required."); }
            var prior = current.Files.SingleOrDefault(file => string.Equals(file.Name, name, StringComparison.Ordinal));
            var next = journal.After.SingleOrDefault(file => string.Equals(file.Name, name, StringComparison.Ordinal));
            if (prior is not null && next is not null && prior.Bytes.AsSpan().SequenceEqual(next.Bytes)) { continue; }
            var fullPath = Path.Combine(target.Directory, name);
            using var parent = WindowsOwnedInstructionStore.PinDefinitionDirectory(Path.GetDirectoryName(fullPath)!, next is not null, token);
            string? identity = null;
            if (next is null) { WindowsOwnedInstructionStore.DeleteDefinitionFile(fullPath, eligible, token); }
            else
            {
                identity = WindowsOwnedInstructionStore.WriteDefinitionFile(Path.GetDirectoryName(fullPath)!, parent,
                    Path.GetFileName(fullPath), next.Bytes, prior is not null, eligible, token);
            }
            var written = Capture(target, token);
            var predicted = current.Files.Where(file => !string.Equals(file.Name, name, StringComparison.Ordinal)).ToList();
            if (next is not null) { predicted.Add(next with { Identity = identity! }); }
            if (!string.Equals(new DiskState(current.DirectoryIdentity, predicted).Revision, written.Revision, StringComparison.Ordinal))
            { throw new IOException("Publication readback changed unexpectedly. No success is claimed; review recovery."); }
            current = written;
            AfterComponentPublished?.Invoke(name);
        }
        RequireEligible(eligible, token);
        if (!SameBytes(current.Files, journal.After)
            || !string.Equals(Capture(target, token).Revision, current.Revision, StringComparison.Ordinal))
        { throw new IOException("Complete transaction publication is unconfirmed. Review recovery."); }
        WindowsOwnedInstructionStore.DeleteDefinitionFile(JournalPath(target), eligible, token);
        return Expose(target, current);
    }

    public Task<CustomizationSnapshot> ReadRecovery(CustomizationSource source, CancellationToken token) => Task.Run(() =>
    {
        lock (gate)
        {
            var target = Resolve(source);
            var journal = ReadJournal(target, token)
                ?? throw new InvalidOperationException("There is no unfinished transaction for this source.");
            var current = Capture(target, token);
            var review = new StringBuilder("RECOVERY REQUIRED. No atomic package update is claimed.\n\n");
            AppendReview(review, "Original complete file set", journal.Before);
            AppendReview(review, "Current complete file set", current.Files);
            AppendReview(review, "Proposed complete file set", journal.After);
            var proposed = Expose(target, new(journal.DirectoryIdentity, journal.After));
            return new CustomizationSnapshot(target.Source, current.Revision, proposed.Files, review.ToString());
        }
    }, token);

    private static void AppendReview(StringBuilder result, string label, IReadOnlyList<DiskFile> files)
    {
        result.AppendLine(label);
        foreach (var file in files.OrderBy(file => file.Name, StringComparer.Ordinal))
        {
            result.AppendLine("----- " + file.Name + " -----");
            result.AppendLine(new UTF8Encoding(false, true).GetString(file.Bytes));
        }
        if (files.Count == 0) { result.AppendLine("(No files)"); }
    }

    private static bool Exists(Target target, DiskState state) => target.Entry is null
        ? state.Files.Any(file => string.Equals(file.Name, target.FileName, StringComparison.Ordinal))
        : state.Files.Count != 0 && new McpDefinitionDocument(state.Files.Single().Bytes).Names.Contains(target.Entry, StringComparer.Ordinal);

    private static DiskFile[] ProposedFiles(Target target, DiskState current, IReadOnlyList<CustomizationFile> files)
    {
        if (target.Entry is null)
        { return files.Select(file => new DiskFile(file.Name, file.CopyBytes(), string.Empty)).ToArray(); }
        var original = current.Files.Count == 0 ? Encoding.UTF8.GetBytes("{\n  \"servers\": {}\n}\n") : current.Files.Single().Bytes;
        return [new(target.FileName, new McpDefinitionDocument(original).Replace(target.Entry, files.Single().CopyBytes()), string.Empty)];
    }

    private static bool SameBytes(IReadOnlyList<DiskFile> left, IReadOnlyList<DiskFile> right) =>
        left.Count == right.Count && left.All(file => right.Any(other =>
            string.Equals(file.Name, other.Name, StringComparison.Ordinal) && file.Bytes.AsSpan().SequenceEqual(other.Bytes)));

    private static void RequireRecoverable(DiskState current, Journal journal)
    {
        foreach (var file in current.Files)
        {
            var original = journal.Before.SingleOrDefault(other => string.Equals(file.Name, other.Name, StringComparison.Ordinal));
            var proposed = journal.After.SingleOrDefault(other => string.Equals(file.Name, other.Name, StringComparison.Ordinal));
            if (!(original is not null && string.Equals(original.Identity, file.Identity, StringComparison.Ordinal)
                    && original.Bytes.AsSpan().SequenceEqual(file.Bytes))
                && !(proposed is not null && proposed.Bytes.AsSpan().SequenceEqual(file.Bytes)))
            { throw new InvalidOperationException("Recovery conflicts with an external edit or replacement. Retain the transaction and resolve that conflict explicitly; nothing was overwritten."); }
        }
        foreach (var missing in journal.Before.Where(before => !current.Files.Any(file => string.Equals(file.Name, before.Name, StringComparison.Ordinal))))
        {
            if (journal.After.Any(after => string.Equals(after.Name, missing.Name, StringComparison.Ordinal)))
            { throw new InvalidOperationException("A component disappeared outside the transaction. Recovery requires explicit external reconciliation."); }
        }
    }

    private Journal? ReadJournal(Target target, CancellationToken token)
    {
        if (!Directory.Exists(JournalDirectory)) { return null; }
        using var pinned = WindowsOwnedInstructionStore.PinDefinitionDirectory(JournalDirectory, false, token);
        var file = WindowsOwnedInstructionStore.ReadDefinitionFile(JournalPath(target), MaximumJournalBytes, token);
        return file is null ? null : ParseJournal(file.Bytes, target);
    }

    private static Journal ParseJournal(byte[] bytes, Target target)
    {
        var journal = DeserializeJournal(bytes);
        if (journal.Source != target.Source || journal.DirectoryIdentity is null
            || journal.DirectoryIdentity.Length != 48 || !journal.DirectoryIdentity.All(char.IsAsciiHexDigit)
            || journal.Before is null || journal.After is null)
        { throw new InvalidDataException("Transaction identity is invalid."); }
        foreach (var files in new[] { journal.Before, journal.After })
        {
            if (files.Length > CustomizationSnapshot.MaximumFiles
                || files.Any(file => file is null || file.Bytes is null || file.Identity is null)
                || files.Select(file => file.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length
                || files.Sum(file => (long)file.Bytes.Length) > CustomizationSnapshot.MaximumTotalBytes)
            { throw new InvalidDataException("Transaction file set is invalid."); }
            foreach (var file in files)
            {
                CustomizationFile.ValidateName(file.Name);
                if (target.Entry is null) { _ = new CustomizationFile(file.Name, file.Bytes); }
                else if (files.Length != 1 || !string.Equals(file.Name, target.FileName, StringComparison.Ordinal)
                    || file.Bytes.Length > 256 * 1024)
                { throw new InvalidDataException("Transaction escaped its selected MCP configuration."); }
                if (target.Source.Kind != CustomizationKind.Skill && !string.Equals(file.Name, target.FileName, StringComparison.Ordinal))
                { throw new InvalidDataException("Transaction escaped its selected standalone file."); }
            }
        }
        _ = Expose(target, new(journal.DirectoryIdentity, journal.After));
        return journal;
    }

    private static Journal DeserializeJournal(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 16 });
            var root = document.RootElement;
            RequireProperties(root, "Source", "DirectoryIdentity", "Before", "After");
            RequireProperties(root.GetProperty("Source"), "Id", "Kind", "Scope", "Name", "Location",
                "PrimaryFile", "IsReadOnly", "UnavailableReason");
            foreach (var key in new[] { "Before", "After" })
            {
                var files = root.GetProperty(key);
                if (files.ValueKind != JsonValueKind.Array) { throw new InvalidDataException("Transaction files must be an array."); }
                foreach (var file in files.EnumerateArray()) { RequireProperties(file, "Name", "Bytes", "Identity"); }
            }
            return JsonSerializer.Deserialize<Journal>(bytes) ?? throw new InvalidDataException("Missing transaction.");
        }
        catch (JsonException exception) { throw new InvalidDataException("Malformed transaction; no recovery is authorized.", exception); }
    }

    private static void RequireProperties(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object
            || value.EnumerateObject().Count() != names.Length
            || !value.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal).SetEquals(names))
        { throw new InvalidDataException("Transaction properties are missing, duplicated or unknown."); }
    }

    private IEnumerable<Target> ListJournalTargets(CancellationToken token)
    {
        if (!Directory.Exists(JournalDirectory)) { yield break; }
        using var pinned = WindowsOwnedInstructionStore.PinDefinitionDirectory(JournalDirectory, false, token);
        var files = Directory.EnumerateFiles(JournalDirectory, "*.json").Take(33).ToArray();
        if (files.Length > 32) { throw new InvalidDataException("The pending transaction inventory exceeds its bound."); }
        foreach (var file in files)
        {
            var bytes = WindowsOwnedInstructionStore.ReadDefinitionFile(file, MaximumJournalBytes, token)
                ?? throw new IOException("A pending transaction disappeared.");
            var source = DeserializeJournal(bytes.Bytes).Source ?? throw new InvalidDataException("Missing transaction source.");
            var expectedFile = source.Kind switch
            {
                CustomizationKind.Skill => "SKILL.md",
                CustomizationKind.McpServer => "mcp.json",
                _ => source.Name + Suffix(source.Kind),
            };
            if (!string.Equals(source.Location, Path.GetFullPath(source.Location), StringComparison.Ordinal)
                || !string.Equals(Path.GetFileName(source.Location), expectedFile, StringComparison.Ordinal)
                || source.Kind == CustomizationKind.Skill
                    && !string.Equals(Path.GetFileName(Path.GetDirectoryName(source.Location)), source.Name, StringComparison.Ordinal))
            { throw new InvalidDataException("The pending transaction does not identify a canonical artifact file."); }
            var root = Roots().SingleOrDefault(root => root.Kind == source.Kind && root.Scope == source.Scope
                && string.Equals(root.Path, source.Kind == CustomizationKind.Skill
                    ? Path.GetDirectoryName(Path.GetDirectoryName(source.Location)) : Path.GetDirectoryName(source.Location),
                    StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("The pending transaction has no admitted personal or Kora source.");
            var target = MakeTarget(root, source.Location, source.Name);
            if (target.Source != source || !string.Equals(file, JournalPath(target), StringComparison.OrdinalIgnoreCase))
            { throw new InvalidDataException("Pending transaction source identity is invalid."); }
            _ = ParseJournal(bytes.Bytes, target);
            yield return target;
        }
    }

    private static void RequireEligible(Func<bool> eligible, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!eligible()) { throw new InvalidOperationException("Definition session, source or privacy authority changed. No successful publication is claimed."); }
    }
}

using System.Text.Json;

using Kora.Core.Artifacts;
using Kora.Core.Dependencies;

namespace Kora.Application.Configuration;

public sealed class ArtifactUsePreferences : IArtifactUsePolicy
{
    private const string FileName = "artifact-use.json";
    private const int MaximumBytes = 128 * 1024;
    private const int MaximumEntries = 1024;
    private readonly IPreferenceStore store;
    private readonly Lock gate = new();
    private HashSet<string> disabled;
    private Dictionary<string, string> reviewed;
    private long revision;
    private bool unavailable;
    private bool observed;

    public ArtifactUsePreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }

    internal ArtifactUsePreferences(IPreferenceStore store)
    {
        this.store = store;
        var saved = Load();
        disabled = saved.Disabled;
        reviewed = saved.Reviewed;
    }

    public event EventHandler? Changed;
    public long Revision => Interlocked.Read(ref revision);

    public bool IsDisabled(string identity)
    {
        ArtifactDefinition.ValidateId(identity);
        lock (gate) { return unavailable || disabled.Contains(identity); }
    }

    public bool IsRevisionAllowed(string identity, string digest)
    {
        ArtifactDefinition.ValidateId(identity);
        ArtifactDigest.Validate(digest);
        lock (gate) { return !unavailable && !disabled.Contains(identity)
            && string.Equals(reviewed.GetValueOrDefault(identity), digest, StringComparison.Ordinal); }
    }

    public void Reconcile(IReadOnlyList<ArtifactDefinition> artifacts)
    {
        Mutate(current =>
        {
            var live = artifacts.Where(item => item.UseControlId is not null)
                .ToLookup(item => item.UseControlId!, StringComparer.Ordinal);
            foreach (var identity in current.Reviewed.Keys.ToArray())
            {
                if (!live[identity].Any(item => string.Equals(item.Digest, current.Reviewed[identity], StringComparison.Ordinal)))
                { current.Reviewed.Remove(identity); }
            }
        }, onlyIfChanged: true);
    }

    public void SetDisabled(string identity, bool value, string? reviewedDigest = null)
    {
        ArtifactDefinition.ValidateId(identity);
        if (reviewedDigest is not null) { ArtifactDigest.Validate(reviewedDigest); }
        Mutate(current =>
        {
            if (value) { current.Disabled.Add(identity); }
            else { current.Disabled.Remove(identity); }
            if (!value && reviewedDigest is not null) { current.Reviewed[identity] = reviewedDigest; }
            else { current.Reviewed.Remove(identity); }
        });
    }

    private void Mutate(Action<UseState> mutate, bool onlyIfChanged = false)
    {
        try
        {
            lock (gate)
            {
                if (unavailable)
                { throw new InvalidOperationException("Artifact use control is unavailable. Restore verified preferences and restart; nothing was enabled."); }
                var current = Load();
                mutate(current);
                if (onlyIfChanged && current.Disabled.SetEquals(disabled)
                    && current.Reviewed.Count == reviewed.Count
                    && current.Reviewed.All(item => string.Equals(reviewed.GetValueOrDefault(item.Key), item.Value, StringComparison.Ordinal)))
                { return; }
                if (current.Disabled.Count + current.Reviewed.Count > MaximumEntries)
                { throw new InvalidDataException("The artifact use-control inventory is full."); }
                var text = JsonSerializer.Serialize(new { Version = 2,
                    Disabled = current.Disabled.Order(StringComparer.Ordinal).ToArray(), Reviewed = current.Reviewed });
                if (System.Text.Encoding.UTF8.GetByteCount(text) > MaximumBytes)
                { throw new InvalidDataException("Artifact use-control state exceeds its persisted byte bound."); }
                store.WriteText(FileName, text);
                if (!string.Equals(store.ReadText(FileName, MaximumBytes), text, StringComparison.Ordinal))
                { throw new IOException("Artifact use-control publication is unconfirmed."); }
                disabled = current.Disabled;
                reviewed = current.Reviewed;
                observed = true;
                Interlocked.Increment(ref revision);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            lock (gate) { unavailable = true; Interlocked.Increment(ref revision); }
            Changed?.Invoke(this, EventArgs.Empty);
            throw;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed record UseState(HashSet<string> Disabled, Dictionary<string, string> Reviewed);

    private UseState Load()
    {
        var text = store.ReadText(FileName, MaximumBytes);
        if (text is null)
        {
            if (observed) { throw new InvalidDataException("Previously observed artifact use-control state is missing."); }
            return new(new(StringComparer.Ordinal), new(StringComparer.Ordinal));
        }
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 3 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("Version", out var version) || !version.TryGetInt32(out var savedVersion)
                || savedVersion is not (1 or 2)
                || !root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
                    .SequenceEqual(savedVersion == 1 ? ["Disabled", "Version"] : ["Disabled", "Reviewed", "Version"], StringComparer.Ordinal))
            { throw new InvalidDataException("Invalid artifact use-control schema."); }
            var entries = root.GetProperty("Disabled");
            if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > MaximumEntries)
            { throw new InvalidDataException("Invalid artifact use-control inventory."); }
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String
                    || !result.Add(ArtifactDefinition.ValidateId(entry.GetString()!)))
                { throw new InvalidDataException("Invalid or duplicate artifact use-control identity."); }
            }
            var approvals = new Dictionary<string, string>(StringComparer.Ordinal);
            if (savedVersion == 2)
            {
                var values = root.GetProperty("Reviewed");
                if (values.ValueKind != JsonValueKind.Object) { throw new InvalidDataException("Invalid reviewed revision inventory."); }
                foreach (var item in values.EnumerateObject())
                {
                    if (item.Value.ValueKind != JsonValueKind.String
                        || !approvals.TryAdd(ArtifactDefinition.ValidateId(item.Name), ArtifactDigest.Validate(item.Value.GetString()!))
                        || result.Contains(item.Name) || result.Count + approvals.Count > MaximumEntries)
                    { throw new InvalidDataException("Invalid or duplicate reviewed revision."); }
                }
            }
            observed = true;
            return new(result, approvals);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException
            or FormatException)
        { throw new InvalidDataException("Saved artifact use-control state is invalid. No artifact may be used.", exception); }
    }
}

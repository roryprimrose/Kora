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
    private long revision;
    private bool unavailable;
    private bool observed;

    public ArtifactUsePreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }

    internal ArtifactUsePreferences(IPreferenceStore store)
    {
        this.store = store;
        disabled = Load();
    }

    public event EventHandler? Changed;
    public long Revision => Interlocked.Read(ref revision);

    public bool IsDisabled(string identity)
    {
        ArtifactDefinition.ValidateId(identity);
        lock (gate) { return unavailable || disabled.Contains(identity); }
    }

    public void SetDisabled(string identity, bool value)
    {
        ArtifactDefinition.ValidateId(identity);
        try
        {
            lock (gate)
            {
                if (unavailable)
                { throw new InvalidOperationException("Artifact use control is unavailable. Restore verified preferences and restart; nothing was enabled."); }
                var current = Load();
                if (value) { current.Add(identity); }
                else { current.Remove(identity); }
                if (current.Count > MaximumEntries)
                { throw new InvalidDataException("The artifact use-control inventory is full."); }
                var text = JsonSerializer.Serialize(new { Version = 1, Disabled = current.Order(StringComparer.Ordinal).ToArray() });
                if (System.Text.Encoding.UTF8.GetByteCount(text) > MaximumBytes)
                { throw new InvalidDataException("Artifact use-control state exceeds its persisted byte bound."); }
                store.WriteText(FileName, text);
                if (!string.Equals(store.ReadText(FileName, MaximumBytes), text, StringComparison.Ordinal))
                { throw new IOException("Artifact use-control publication is unconfirmed."); }
                disabled = current;
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

    private HashSet<string> Load()
    {
        var text = store.ReadText(FileName, MaximumBytes);
        if (text is null)
        {
            if (observed) { throw new InvalidDataException("Previously observed artifact use-control state is missing."); }
            return new(StringComparer.Ordinal);
        }
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 3 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
                    .SequenceEqual(["Disabled", "Version"], StringComparer.Ordinal)
                || root.GetProperty("Version").GetInt32() != 1)
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
            observed = true;
            return result;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException
            or FormatException)
        { throw new InvalidDataException("Saved artifact use-control state is invalid. No artifact may be used.", exception); }
    }
}

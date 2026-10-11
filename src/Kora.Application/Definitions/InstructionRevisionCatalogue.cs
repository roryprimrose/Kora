using System.Text.Json;

using Kora.Application.Configuration;
using Kora.Core.Artifacts;
using Kora.Core.Dependencies;

namespace Kora.Application.Definitions;

public sealed class InstructionRevisionCatalogue
{
    private readonly string fileName;
    private readonly ArtifactKind kind;
    private const int MaximumPreferenceBytes = 128 * 1024;
    private static readonly string[] PreferenceProperties = ["Revisions", "Version"];
    private readonly IPreferenceStore store;
    private readonly Lock gate = new();
    private bool unavailable;
    private bool stateObserved;
    private readonly Dictionary<string, string> preparedReviews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> enabledForRun = new(StringComparer.Ordinal);

    public InstructionRevisionCatalogue(IApplicationDataPaths paths, ArtifactKind kind = ArtifactKind.Instruction)
        : this(new LocalPreferenceStore(paths), kind) { }
    internal InstructionRevisionCatalogue(IPreferenceStore store, ArtifactKind kind = ArtifactKind.Instruction)
    {
        _ = OwnedDefinitionKind.DirectoryName(kind);
        this.kind = kind;
        this.store = store;
        // Keep the instruction v1 state byte/schema contract; the other closed kind has independent state.
        fileName = kind == ArtifactKind.Instruction ? "instruction-revisions.json" : "prompt-revisions.json";
    }

    public IReadOnlyList<ArtifactDefinition> Reconcile(IReadOnlyList<ArtifactDefinition> discovered)
    {
        lock (gate)
        {
            var before = Load();
            var initial = before is null;
            var state = before ?? new Dictionary<string, string?>(StringComparer.Ordinal);
            var names = discovered.Where(item => item.Kind == kind && item.OwnedInstructionName is not null)
                .Select(item => item.OwnedInstructionName!).ToHashSet(StringComparer.Ordinal);
            foreach (var name in state.Keys.Where(name => !names.Contains(name)).ToArray())
            { state.Remove(name); preparedReviews.Remove(name); enabledForRun.Remove(name); }
            var result = discovered.Select(artifact =>
            {
                if (artifact.Kind != kind || artifact.OwnedInstructionName is not { } name) { return artifact; }
                var revision = artifact.OwnedInstructionRevision ?? artifact.Digest;
                if (!state.TryGetValue(name, out var approved))
                {
                    // One-time unchanged legacy compatibility; native v1 saves are always new disabled candidates.
                    approved = initial && !unavailable && !artifact.RequiresInstructionReview
                        ? revision : null;
                    state[name] = approved;
                }
                if (unavailable || !string.Equals(approved, revision, StringComparison.Ordinal)) { state[name] = null; }
                if (!string.Equals(preparedReviews.GetValueOrDefault(name), revision, StringComparison.Ordinal)) { preparedReviews.Remove(name); }
                if (!string.Equals(enabledForRun.GetValueOrDefault(name), revision, StringComparison.Ordinal)) { enabledForRun.Remove(name); }
                return artifact with { IsUseEnabled = !unavailable && (string.Equals(state[name], revision, StringComparison.Ordinal)
                    || string.Equals(enabledForRun.GetValueOrDefault(name), revision, StringComparison.Ordinal)) };
            }).ToArray();
            Save(state);
            unavailable = false;
            return result;
        }
    }

    public void Hold(string name, bool existingOnly = false)
    {
        OwnedInstructionNames.ReadName(name);
        lock (gate)
        {
            var state = Load() ?? new Dictionary<string, string?>(StringComparer.Ordinal);
            preparedReviews.Remove(name);
            enabledForRun.Remove(name);
            // New native copies have no authority to revoke. Failed absent destinations must not
            // consume persisted slots or make a full unrelated original inventory unavailable.
            if (existingOnly && !state.ContainsKey(name)) { return; }
            state[name] = null;
            Save(state);
        }
    }

    public void PrepareActivation(OwnedInstructionSnapshot reviewed)
    {
        RequireKind(reviewed);
        lock (gate)
        {
            var state = Load() ?? throw new InvalidDataException("Observe the managed catalogue before activation.");
            if (unavailable || !state.ContainsKey(reviewed.Name))
            { throw new InvalidOperationException("The owned instruction catalogue is unavailable."); }
            enabledForRun.Remove(reviewed.Name);
            state[reviewed.Name] = null;
            Save(state);
            preparedReviews[reviewed.Name] = reviewed.Revision;
        }
    }

    public void CompleteActivation(OwnedInstructionSnapshot reviewed)
    {
        RequireKind(reviewed);
        lock (gate)
        {
            if (unavailable || !string.Equals(preparedReviews.GetValueOrDefault(reviewed.Name), reviewed.Revision, StringComparison.Ordinal))
            { throw new InvalidOperationException("The exact reviewed instruction activation was retired."); }
            preparedReviews.Remove(reviewed.Name);
            enabledForRun[reviewed.Name] = reviewed.Revision;
        }
    }

    public IReadOnlyList<string> SuspendReviewedUse()
    {
        lock (gate)
        {
            var names = preparedReviews.Keys.Union(enabledForRun.Keys, StringComparer.Ordinal).ToArray();
            preparedReviews.Clear();
            enabledForRun.Clear();
            return names;
        }
    }

    public bool IsEnabled(OwnedInstructionSnapshot snapshot)
        {
            RequireKind(snapshot);
            lock (gate)
            {
                if (unavailable) { return false; }
                var saved = Load();
                return string.Equals(enabledForRun.GetValueOrDefault(snapshot.Name), snapshot.Revision, StringComparison.Ordinal)
                    || string.Equals(saved?.GetValueOrDefault(snapshot.Name), snapshot.Revision, StringComparison.Ordinal);
        }
    }

    public void MarkUnavailable()
    {
        lock (gate)
        {
            unavailable = true;
            preparedReviews.Clear();
            enabledForRun.Clear();
            var state = Load();
            if (state is null) { return; }
            foreach (var name in state.Keys.ToArray()) { state[name] = null; }
            Save(state);
        }
    }

    private Dictionary<string, string?>? Load()
    {
        var text = store.ReadText(fileName, MaximumPreferenceBytes);
        if (text is null)
        {
            if (stateObserved) { throw new InvalidDataException("Previously observed instruction revision state is missing; restore verified preferences."); }
            return null;
        }
        stateObserved = true;
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 3 });
            var root = document.RootElement;
            if (root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(PreferenceProperties, StringComparer.Ordinal) is false
                || root.GetProperty("Version").GetInt32() != 1)
            { throw new InvalidDataException("Invalid instruction revision preferences."); }
            var result = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var item in root.GetProperty("Revisions").EnumerateObject())
            {
                OwnedInstructionNames.ReadName(item.Name);
                var digest = item.Value.ValueKind == JsonValueKind.Null ? null : item.Value.GetString();
                if (digest is not null && (digest.Length is < 64 or > 256 || digest.Any(c => c is not (>= 'a' and <= 'f' or >= '0' and <= '9' or '.')))
                    || !result.TryAdd(item.Name, digest) || result.Count > 128)
                { throw new InvalidDataException("Invalid instruction revision identity or digest."); }
            }
            return result;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        { throw new InvalidDataException("Malformed instruction revision state; restore verified local preferences.", exception); }
    }

    private void Save(Dictionary<string, string?> state)
    {
        var text = JsonSerializer.Serialize(new { Version = 1, Revisions = state });
        try
        {
            if (state.Count > 128 || System.Text.Encoding.UTF8.GetByteCount(text) > MaximumPreferenceBytes)
            { throw new InvalidDataException("The owned instruction revision inventory exceeds its persisted bounds."); }
            store.WriteText(fileName, text);
            if (!string.Equals(store.ReadText(fileName, MaximumPreferenceBytes), text, StringComparison.Ordinal))
            { throw new IOException("Instruction revision publication is unconfirmed."); }
            stateObserved = true;
        }
        catch { unavailable = true; throw; }
    }

    private void RequireKind(OwnedInstructionSnapshot snapshot)
    {
        if (snapshot.Document.Definition.Kind != kind)
        { throw new InvalidDataException("The exact reviewed revision belongs to a different owned kind."); }
    }
}

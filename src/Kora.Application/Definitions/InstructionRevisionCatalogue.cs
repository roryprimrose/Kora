using System.Text.Json;

using Kora.Application.Configuration;
using Kora.Core.Artifacts;
using Kora.Core.Dependencies;

namespace Kora.Application.Definitions;

public sealed class InstructionRevisionCatalogue
{
    private const string FileName = "instruction-revisions.json";
    private static readonly string[] PreferenceProperties = ["Revisions", "Version"];
    private readonly IPreferenceStore store;
    private readonly Lock gate = new();
    private bool unavailable;
    private bool stateObserved;
    private readonly Dictionary<string, string> preparedReviews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> enabledForRun = new(StringComparer.Ordinal);

    public InstructionRevisionCatalogue(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal InstructionRevisionCatalogue(IPreferenceStore store) => this.store = store;

    public IReadOnlyList<ArtifactDefinition> Reconcile(IReadOnlyList<ArtifactDefinition> discovered)
    {
        lock (gate)
        {
            var before = Load();
            var initial = before is null;
            var state = before ?? new Dictionary<string, string?>(StringComparer.Ordinal);
            var names = discovered.Where(item => item.OwnedInstructionName is not null)
                .Select(item => item.OwnedInstructionName!).ToHashSet(StringComparer.Ordinal);
            foreach (var name in state.Keys.Where(name => !names.Contains(name)).ToArray())
            { state[name] = null; preparedReviews.Remove(name); enabledForRun.Remove(name); }
            var result = discovered.Select(artifact =>
            {
                if (artifact.OwnedInstructionName is not { } name) { return artifact; }
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

    public void Hold(string name)
    {
        lock (gate)
        {
            var state = Load() ?? new Dictionary<string, string?>(StringComparer.Ordinal);
            preparedReviews.Remove(name);
            enabledForRun.Remove(name);
            state[name] = null;
            Save(state);
        }
    }

    public void PrepareActivation(OwnedInstructionSnapshot reviewed)
    {
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
        var text = store.ReadText(FileName, 32 * 1024);
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
                var digest = item.Value.ValueKind == JsonValueKind.Null ? null : item.Value.GetString();
                if (item.Name.Length is 0 or > 64 || item.Name.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
                    || digest is not null && (digest.Length is < 64 or > 256 || digest.Any(c => c is not (>= 'a' and <= 'f' or >= '0' and <= '9' or '.')))
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
            store.WriteText(FileName, text);
            if (!string.Equals(store.ReadText(FileName, 32 * 1024), text, StringComparison.Ordinal))
            { throw new IOException("Instruction revision publication is unconfirmed."); }
            stateObserved = true;
        }
        catch { unavailable = true; throw; }
    }
}

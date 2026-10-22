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
    private readonly Dictionary<string, (string Revision, Guid Receipt)> preparedReviews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> enabledForRun = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Revision, Guid Epoch)> observedRevisions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> explicitHolds = new(StringComparer.Ordinal);

    public void PublishReconciled(IReadOnlyList<ArtifactDefinition> discovered,
        Action<IReadOnlyList<ArtifactDefinition>> publish)
    {
        // Revision state and its consumer publication share this short gate. A reload cannot
        // publish an enabled snapshot calculated before an exact hold.
        lock (gate) { publish(Reconcile(discovered)); }
    }

    public Guid CaptureReviewEpoch(OwnedInstructionSnapshot reviewed)
    {
        RequireKind(reviewed);
        lock (gate)
        {
            if (!observedRevisions.TryGetValue(reviewed.Name, out var observed)
                || !string.Equals(observed.Revision, reviewed.Revision, StringComparison.Ordinal))
            { throw new InvalidOperationException("The exact owned revision is stale. Reload and review again."); }
            return observed.Epoch;
        }
    }

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
            if (unavailable) { SuspendReviewedUse(); }
            var before = Load();
            var initial = before is null;
            var state = before ?? new Dictionary<string, string?>(StringComparer.Ordinal);
            var names = discovered.Where(item => item.Kind == kind && item.OwnedInstructionName is not null)
                .Select(item => item.OwnedInstructionName!).ToHashSet(StringComparer.Ordinal);
            foreach (var name in state.Keys.Where(name => !names.Contains(name)).ToArray())
            { state.Remove(name); preparedReviews.Remove(name); enabledForRun.Remove(name); observedRevisions.Remove(name); explicitHolds.Remove(name); }
            var result = discovered.Select(artifact =>
            {
                if (artifact.Kind != kind || artifact.OwnedInstructionName is not { } name) { return artifact; }
                var revision = artifact.OwnedInstructionRevision ?? artifact.Digest;
                if (!observedRevisions.TryGetValue(name, out var observed)
                    || !string.Equals(observed.Revision, revision, StringComparison.Ordinal))
                { observedRevisions[name] = (revision, Guid.NewGuid()); }
                if (!state.TryGetValue(name, out var approved))
                {
                    // One-time unchanged legacy compatibility; native saves are always new disabled candidates.
                    approved = initial && !unavailable && !artifact.RequiresInstructionReview
                        ? revision : null;
                    state[name] = approved;
                }
                if (unavailable || !string.Equals(approved, revision, StringComparison.Ordinal)) { state[name] = null; }
                if (!string.Equals(preparedReviews.GetValueOrDefault(name).Revision, revision, StringComparison.Ordinal)) { preparedReviews.Remove(name); }
                if (!string.Equals(enabledForRun.GetValueOrDefault(name), revision, StringComparison.Ordinal)) { enabledForRun.Remove(name); }
                if (!string.Equals(explicitHolds.GetValueOrDefault(name), revision, StringComparison.Ordinal)) { explicitHolds.Remove(name); }
                return artifact with { IsUseEnabled = !unavailable && (string.Equals(state[name], revision, StringComparison.Ordinal)
                    || string.Equals(enabledForRun.GetValueOrDefault(name), revision, StringComparison.Ordinal)),
                    PreservesCapturedUse = !unavailable && string.Equals(explicitHolds.GetValueOrDefault(name), revision, StringComparison.Ordinal) };
            }).ToArray();
            Save(state);
            unavailable = false;
            return result;
        }
    }

    public void Hold(string name, bool existingOnly = false, Action? publish = null)
    {
        OwnedInstructionNames.ReadName(name);
        lock (gate)
        {
            preparedReviews.Remove(name);
            enabledForRun.Remove(name);
            explicitHolds.Remove(name);
            RetireEpoch(name);
            try
            {
                var state = Load() ?? new Dictionary<string, string?>(StringComparer.Ordinal);
                // Failed absent copies must not consume inventory slots or revoke an original.
                if (existingOnly && !state.ContainsKey(name)) { return; }
                state[name] = null;
                Save(state);
            }
            catch { unavailable = true; throw; }
            finally { publish?.Invoke(); }
        }
    }

    public void HoldExact(OwnedInstructionSnapshot reviewed, ArtifactCatalogue catalogue)
    {
        RequireKind(reviewed);
        lock (gate)
        {
            RequireExactCandidate(reviewed, catalogue);
            Hold(reviewed.Name, publish: () => PublishUse(reviewed, catalogue, enabled: false));
        }
    }

    public bool DisableExact(OwnedInstructionSnapshot reviewed, ArtifactCatalogue catalogue)
    {
        RequireKind(reviewed);
        lock (gate)
        {
            RequireExactCandidate(reviewed, catalogue);
            var enabled = IsEnabled(reviewed);
            Hold(reviewed.Name, publish: () =>
            {
                explicitHolds[reviewed.Name] = reviewed.Revision;
                PublishUse(reviewed, catalogue, enabled: false, preservesCapturedUse: true);
            });
            return enabled;
        }
    }

    public void HoldCandidate(ArtifactDefinition expected, ArtifactCatalogue catalogue)
    {
        lock (gate)
        {
            if (expected.Kind != kind || expected.OwnedInstructionName is not { } name
                || !catalogue.Candidates.Any(candidate => MatchesRevision(candidate, expected)))
            { return; }
            Hold(name, publish: () => catalogue.Update(items => items.Select(candidate => MatchesRevision(candidate, expected)
                ? candidate with { IsUseEnabled = false, PreservesCapturedUse = false } : candidate).ToArray()));
        }
    }

    private void RequireExactCandidate(OwnedInstructionSnapshot reviewed, ArtifactCatalogue catalogue)
    {
        if (!catalogue.Candidates.Any(candidate => MatchesRevision(candidate, reviewed)))
        { throw new InvalidOperationException("The exact owned revision is no longer current. Reload; no replacement was selected."); }
    }

    private bool MatchesRevision(ArtifactDefinition candidate, OwnedInstructionSnapshot reviewed) =>
        MatchesRevision(candidate, reviewed.Document.Definition with { OwnedInstructionRevision = reviewed.Revision });

    private bool MatchesRevision(ArtifactDefinition candidate, ArtifactDefinition expected) =>
        candidate.Kind == kind && candidate.Source is "kora-profile"
        && string.Equals(candidate.Id, expected.Id, StringComparison.Ordinal)
        && string.Equals(candidate.OwnedInstructionName, expected.OwnedInstructionName, StringComparison.Ordinal)
        && string.Equals(candidate.Digest, expected.Digest, StringComparison.Ordinal)
        && string.Equals(candidate.OwnedInstructionRevision, expected.OwnedInstructionRevision, StringComparison.Ordinal);

    private void PublishUse(OwnedInstructionSnapshot reviewed, ArtifactCatalogue catalogue, bool enabled, bool preservesCapturedUse = false) =>
        catalogue.Update(items => items.Select(candidate => MatchesRevision(candidate, reviewed)
            ? candidate with { IsUseEnabled = enabled, PreservesCapturedUse = preservesCapturedUse } : candidate).ToArray());

    private void RetireEpoch(string name)
    {
        if (observedRevisions.TryGetValue(name, out var observed))
        { observedRevisions[name] = (observed.Revision, Guid.NewGuid()); }
    }

    public Guid PrepareActivation(OwnedInstructionSnapshot reviewed, Guid? expectedEpoch = null)
    {
        RequireKind(reviewed);
        lock (gate)
        {
            var state = Load() ?? throw new InvalidDataException("Observe the managed catalogue before activation.");
            if (unavailable || !state.ContainsKey(reviewed.Name))
            { throw new InvalidOperationException("The owned instruction catalogue is unavailable."); }
            if (expectedEpoch is { } epoch && CaptureReviewEpoch(reviewed) != epoch)
            { throw new InvalidOperationException("The reviewed use was retired. Begin a fresh exact review."); }
            enabledForRun.Remove(reviewed.Name);
            explicitHolds.Remove(reviewed.Name);
            state[reviewed.Name] = null;
            Save(state);
            var receipt = Guid.NewGuid();
            preparedReviews[reviewed.Name] = (reviewed.Revision, receipt);
            return receipt;
        }
    }

    public void CompleteActivation(OwnedInstructionSnapshot reviewed, Guid receipt, ArtifactCatalogue? catalogue = null)
    {
        RequireKind(reviewed);
        lock (gate)
        {
            var prepared = preparedReviews.GetValueOrDefault(reviewed.Name);
            if (unavailable || !string.Equals(prepared.Revision, reviewed.Revision, StringComparison.Ordinal)
                || prepared.Receipt != receipt)
            { throw new InvalidOperationException("The exact reviewed instruction activation was retired."); }
            if (catalogue is not null) { RequireExactCandidate(reviewed, catalogue); }
            preparedReviews.Remove(reviewed.Name);
            enabledForRun[reviewed.Name] = reviewed.Revision;
            if (catalogue is not null) { PublishUse(reviewed, catalogue, enabled: true); }
        }
    }

    public IReadOnlyList<string> SuspendReviewedUse(Action<IReadOnlyList<string>>? publish = null)
    {
        lock (gate)
        {
            var names = preparedReviews.Keys.Union(enabledForRun.Keys, StringComparer.Ordinal)
                .Union(explicitHolds.Keys, StringComparer.Ordinal).ToArray();
            foreach (var name in observedRevisions.Keys) { RetireEpoch(name); }
            preparedReviews.Clear();
            enabledForRun.Clear();
            explicitHolds.Clear();
            publish?.Invoke(names);
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

    /// <summary>Checks current catalogue authority for an already admitted exact owned revision.
    /// The owning turn must establish capture separately; this does not admit queued use.</summary>
    public bool IsCapturedUseAllowed(OwnedInstructionSnapshot snapshot)
    {
        RequireKind(snapshot);
        if (snapshot.Document.Definition.Source is not "kora-profile")
        { throw new InvalidDataException("Captured use requires an admitted Kora-owned source snapshot."); }
        lock (gate)
        {
            if (unavailable) { return false; }
            return IsEnabled(snapshot)
                || string.Equals(explicitHolds.GetValueOrDefault(snapshot.Name), snapshot.Revision, StringComparison.Ordinal);
        }
    }

    public void MarkUnavailable()
    {
        lock (gate)
        {
            unavailable = true;
            foreach (var name in observedRevisions.Keys) { RetireEpoch(name); }
            preparedReviews.Clear();
            enabledForRun.Clear();
            explicitHolds.Clear();
            var state = Load();
            if (state is null) { return; }
            foreach (var name in state.Keys.ToArray()) { state[name] = null; }
            Save(state);
        }
    }

    private Dictionary<string, string?>? Load()
    {
        try { return LoadCore(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            unavailable = true;
            preparedReviews.Clear();
            enabledForRun.Clear();
            explicitHolds.Clear();
            foreach (var name in observedRevisions.Keys) { RetireEpoch(name); }
            throw;
        }
    }

    private Dictionary<string, string?>? LoadCore()
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

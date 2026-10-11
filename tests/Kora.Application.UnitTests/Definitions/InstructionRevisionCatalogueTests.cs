using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Application.Configuration;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Artifacts;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Definitions;

public sealed class InstructionRevisionCatalogueTests
{
    private static OwnedInstructionSnapshot Snapshot(bool native = true, string body = "Body", ArtifactKind kind = ArtifactKind.Instruction) =>
        new("example", new string('a', 48), new string('b', 48),
            ArtifactFileDocument.Parse(ArtifactFileDocument.Encode($"---\n{(native ? "kora-format: 1\n" : "")}name: example\ndescription: Test\n---\n{body}", false),
                kind, "kora-profile", "example"));
    private static ArtifactDefinition Candidate(OwnedInstructionSnapshot snapshot) =>
        snapshot.Document.Definition with { OwnedInstructionRevision = snapshot.Revision };

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public void NativeRevisionsRemainDisabledAcrossReloadRestartChangeDeletionAndRestoration(ArtifactKind kind)
    {
        var store = new LocalSharedSkillPreferencesTests.Store();
        var registry = new InstructionRevisionCatalogue(store, kind);
        var snapshot = Snapshot(kind: kind);
        registry.Reconcile([Candidate(snapshot)]).Single().IsUseEnabled.Should().BeFalse();
        registry.PrepareActivation(snapshot);
        registry.CompleteActivation(snapshot);
        registry.Reconcile([Candidate(snapshot)]).Single().IsUseEnabled.Should().BeTrue();
        new InstructionRevisionCatalogue(store, kind).Reconcile([Candidate(snapshot)]).Single().IsUseEnabled.Should().BeFalse();
        var changed = Snapshot(body: "Changed", kind: kind);
        registry.Reconcile([Candidate(changed)]).Single().IsUseEnabled.Should().BeFalse();
        registry.Reconcile([Candidate(snapshot)]).Single().IsUseEnabled.Should().BeFalse();
        registry.PrepareActivation(snapshot);
        registry.CompleteActivation(snapshot);
        registry.Reconcile([]).Should().BeEmpty();
        registry.Reconcile([Candidate(snapshot)]).Single().IsUseEnabled.Should().BeFalse();
        registry.MarkUnavailable();
        registry.Reconcile([Candidate(snapshot)]).Single().IsUseEnabled.Should().BeFalse();
    }

    [Fact]
    public void IdenticalOwnedNamesHaveSeparateKindStateAndInstructionV1BytesAreNotMigrated()
    {
        var store = new KindStore();
        var instructions = new InstructionRevisionCatalogue(store);
        var prompts = new InstructionRevisionCatalogue(store, ArtifactKind.Prompt);
        var instruction = Snapshot(native: false);
        var prompt = Snapshot(kind: ArtifactKind.Prompt);
        var first = instructions.Reconcile([Candidate(instruction), Candidate(prompt)]);
        var originalState = store.Text["instruction-revisions.json"];
        var both = prompts.Reconcile(first);
        both[0].IsUseEnabled.Should().BeTrue();
        both[1].IsUseEnabled.Should().BeFalse();
        store.Text["instruction-revisions.json"].Should().Be(originalState);
        prompts.PrepareActivation(prompt);
        prompts.CompleteActivation(prompt);
        prompts.IsEnabled(prompt).Should().BeTrue();
        instructions.IsEnabled(instruction).Should().BeTrue();
        prompts.Hold("example");
        prompts.IsEnabled(prompt).Should().BeFalse();
        instructions.IsEnabled(instruction).Should().BeTrue();
        ((Action)(() => prompts.PrepareActivation(instruction))).Should().Throw<InvalidDataException>();
        ((Action)(() => prompts.CompleteActivation(instruction))).Should().Throw<InvalidDataException>();
        ((Action)(() => prompts.IsEnabled(instruction))).Should().Throw<InvalidDataException>();
        var restarted = new InstructionRevisionCatalogue(store, ArtifactKind.Prompt);
        restarted.Reconcile(both)[1].IsUseEnabled.Should().BeFalse();
        store.Text.Remove("prompt-revisions.json");
        ((Action)(() => restarted.Reconcile(both))).Should().Throw<InvalidDataException>();
    }

    private sealed class KindStore : IPreferenceStore
    {
        internal readonly Dictionary<string, string> Text = new(StringComparer.Ordinal);
        public string? ReadText(string fileName) => Text.GetValueOrDefault(fileName);
        public void WriteText(string fileName, string contents) => Text[fileName] = contents;
        public string[]? ReadLines(string fileName) => throw new NotSupportedException();
        public void WriteLines(string fileName, IEnumerable<string> contents) => throw new NotSupportedException();
        public void Delete(string fileName) => throw new NotSupportedException();
    }

    [Fact]
    public void UnchangedLegacyAndBundledBehaviorArePreservedButReplacedFileIdentityIsWithheld()
    {
        var store = new LocalSharedSkillPreferencesTests.Store();
        var registry = new InstructionRevisionCatalogue(store);
        var legacy = Snapshot(native: false);
        var bundled = new ArtifactDefinition("bundled.test", ArtifactKind.Prompt, "Bundled", "Test", "test",
            ["test"], "bundled", "1", new string('a', 64), "Body");
        registry.Reconcile([Candidate(legacy), bundled]).Should().OnlyContain(item => item.IsUseEnabled);
        registry.Reconcile([Candidate(legacy with { FileIdentity = new string('c', 48) }), bundled])
            [0].IsUseEnabled.Should().BeFalse();
        registry.Hold("example");
        registry.Reconcile([Candidate(legacy), bundled])[0].IsUseEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"Version\":2,\"Revisions\":{}}")]
    [InlineData("{\"Version\":1,\"Extra\":true,\"Revisions\":{}}")]
    [InlineData("{\"Version\":1,\"Version\":1,\"Revisions\":{}}")]
    [InlineData("{\"Version\":1,\"Revisions\":{\"bad\\\\name\":null}}")]
    [InlineData("{\"Version\":1,\"Revisions\":{\"example\":\"bad\"}}")]
    [InlineData("{\"Version\":1,\"Revisions\":{\"example\":true}}")]
    [InlineData("{\"Version\":1,\"Revisions\":[]}")]
    public void CorruptRevisionStateDoesNotFallBackOrReactivateLegacy(string text)
    {
        var store = new LocalSharedSkillPreferencesTests.Store { Text = text };
        var registry = new InstructionRevisionCatalogue(store);
        ((Action)(() => registry.Reconcile([Candidate(Snapshot(native: false))])))
            .Should().Throw<InvalidDataException>();
        store.Writes.Should().Be(0);
    }

    [Fact]
    public void MissingObservationUnavailableStateAndFailedReadbackCannotAuthorizeActivation()
    {
        var store = new LocalSharedSkillPreferencesTests.Store();
        var registry = new InstructionRevisionCatalogue(store);
        ((Action)(() => registry.PrepareActivation(Snapshot()))).Should().Throw<InvalidDataException>();
        registry.MarkUnavailable();
        registry.Reconcile([Candidate(Snapshot(native: false))]).Single().IsUseEnabled.Should().BeFalse();
        registry.Reconcile([]).Should().BeEmpty();
        ((Action)(() => registry.PrepareActivation(Snapshot() with { Name = "other" }))).Should().Throw<InvalidOperationException>();
        store.AfterWrite = () => store.Text = "{\"Version\":1,\"Revisions\":{}}";
        ((Action)(() => registry.Hold("example"))).Should().Throw<IOException>();
    }

    [Fact]
    public void EmptyStateCanBeHeldAndDataPathsConstructorOwnsOnlyPreferenceStorage()
    {
        _ = new InstructionRevisionCatalogue(new Paths());
        var store = new LocalSharedSkillPreferencesTests.Store();
        var registry = new InstructionRevisionCatalogue(store);
        registry.Hold("example");
        registry.Reconcile([Snapshot().Document.Definition]).Single().IsUseEnabled.Should().BeFalse();
    }

    [Fact]
    public void MalformedBoundedIdentityDigestDuplicateAndCountStateIsRejected()
    {
        var invalid = new[]
        {
            "{\"Version\":1,\"Revisions\":{\"\":null}}",
            "{\"Version\":1,\"Revisions\":{\"" + new string('a', 129) + "\":null}}",
            "{\"Version\":1,\"Revisions\":{\"example\":\"" + new string('a', 257) + "\"}}",
            "{\"Version\":1,\"Revisions\":{\"example\":\"" + new string('g', 64) + "\"}}",
            "{\"Version\":1,\"Revisions\":{\"example\":null,\"example\":null}}",
            "{\"Version\":1,\"Revisions\":{" + string.Join(",", Enumerable.Range(0, 129).Select(i => $"\"item-{i}\":null")) + "}}",
        };
        foreach (var text in invalid)
        {
            var registry = new InstructionRevisionCatalogue(new LocalSharedSkillPreferencesTests.Store { Text = text });
            ((Action)(() => registry.Reconcile([Candidate(Snapshot())]))).Should().Throw<InvalidDataException>();
        }
    }

    [Fact]
    public void MissingObservedStateAndFailedWritesNeverRestorePreviousLegacyActivation()
    {
            var store = new LocalSharedSkillPreferencesTests.Store();
            var registry = new InstructionRevisionCatalogue(store);
            var legacy = Candidate(Snapshot(native: false));
            registry.Reconcile([legacy]).Single().IsUseEnabled.Should().BeTrue();
            store.FailWrite = true;
            ((Action)(() => registry.Hold("example"))).Should().Throw<IOException>();
            store.FailWrite = false;
            registry.Reconcile([legacy]).Single().IsUseEnabled.Should().BeFalse();
            store.Text = null;
            ((Action)(() => registry.Reconcile([legacy]))).Should().Throw<InvalidDataException>();
    }

    private sealed class Paths : IApplicationDataPaths
    {
        public string LocalRoot => "unused-local";
        public string RoamingRoot => "unused-roaming";
    }

    [Fact]
    public void PrivacyClosureRetiresPreparedAndEnabledRunRevisionsWithoutRevivingSavedAuthority()
    {
        var registry = new InstructionRevisionCatalogue(new LocalSharedSkillPreferencesTests.Store());
        var snapshot = Snapshot();
        registry.Reconcile([Candidate(snapshot)]);
        registry.PrepareActivation(snapshot);
        registry.SuspendReviewedUse().Should().Equal("example");
        ((Action)(() => registry.CompleteActivation(snapshot))).Should().Throw<InvalidOperationException>();
        registry.PrepareActivation(snapshot);
        registry.CompleteActivation(snapshot);
        registry.SuspendReviewedUse().Should().Equal("example");
        registry.Reconcile([Candidate(snapshot)]).Single().IsUseEnabled.Should().BeFalse();
        registry.PrepareActivation(snapshot);
        registry.MarkUnavailable();
        ((Action)(() => registry.CompleteActivation(snapshot))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PersistedNameBoundsCannotBeExceededAndDeletedRowsNeedNoAuthorityTombstone()
    {
        var store = new LocalSharedSkillPreferencesTests.Store();
        var registry = new InstructionRevisionCatalogue(store);
        for (var index = 0; index < 128; index++) { registry.Hold("item-" + index); }
        ((Action)(() => registry.Hold("item-128"))).Should().Throw<InvalidDataException>();
        var legacy = Snapshot(native: false) with { Name = "Legacy.Name" };
        var candidate = Candidate(legacy) with { OwnedInstructionName = legacy.Name };
        registry.Reconcile([candidate]).Single().IsUseEnabled.Should().BeFalse();
        registry.Reconcile([]).Should().BeEmpty();
        registry.Reconcile([candidate]).Single().IsUseEnabled.Should().BeFalse();
    }

    [Fact]
    public void SerializedMetadataCapacityFailsBeforeReplacingSavedState()
    {
        var names = Enumerable.Range(0, 128).Select(i => new string('界', 127) + (char)(0x4e00 + i)).ToArray();
        var original = "{\"Version\":1,\"Revisions\":{" + string.Join(",", names.Select(name => $"\"{name}\":\"{new string('a', 256)}\"")) + "}}";
        var store = new LocalSharedSkillPreferencesTests.Store { Text = original };
        var registry = new InstructionRevisionCatalogue(store);
        ((Action)(() => registry.Hold(names[0]))).Should().Throw<InvalidDataException>();
        store.Text.Should().Be(original);
        store.Writes.Should().Be(0);
    }

    [Fact]
    public void FreshnessRequiresAnActuallyEnabledCurrentRunOrUnchangedLegacyRevision()
    {
        var registry = new InstructionRevisionCatalogue(new LocalSharedSkillPreferencesTests.Store());
        var legacy = Snapshot(native: false);
        registry.IsEnabled(legacy).Should().BeFalse();
        registry.Reconcile([Candidate(legacy)]);
        registry.IsEnabled(legacy).Should().BeTrue();
        registry.Hold(legacy.Name);
        registry.IsEnabled(legacy).Should().BeFalse();
        registry.PrepareActivation(legacy);
        registry.CompleteActivation(legacy);
        registry.IsEnabled(legacy).Should().BeTrue();
        registry.MarkUnavailable();
        registry.IsEnabled(legacy).Should().BeFalse();
    }
}

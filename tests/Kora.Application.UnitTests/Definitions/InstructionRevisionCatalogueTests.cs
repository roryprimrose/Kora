using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Application.Configuration;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Artifacts;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Definitions;

public sealed class InstructionRevisionCatalogueTests
{
    [Fact]
    public void ExactDisableSeparatesSameFilenameKindsAndLeavesUnrelatedLegacyAndBundledStateUnchanged()
    {
        var store = new KindStore();
        var instructions = new InstructionRevisionCatalogue(store);
        var prompts = new InstructionRevisionCatalogue(store, ArtifactKind.Prompt);
        var instruction = Snapshot();
        var prompt = new OwnedInstructionSnapshot("example", new string('c', 48), new string('d', 48),
            ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                "---\nkora-format: 1\nname: prompt-example\ndescription: Test\n---\nLiteral request", true),
                ArtifactKind.Prompt, "kora-profile", "example"));
        var legacy = new OwnedInstructionSnapshot("legacy", new string('a', 48), new string('e', 48),
            ArtifactFileDocument.Parse(ArtifactFileDocument.Encode("---\nname: legacy\ndescription: Test\n---\nLegacy content", false),
                ArtifactKind.Instruction, "kora-profile", "legacy"));
        var bundled = new ArtifactDefinition("bundled.test", ArtifactKind.Skill, "Bundled", "Test", "bundled",
            ["bundled"], "bundled", "1", new string('f', 64), "Body");
        var catalogue = new ArtifactCatalogue(prompts.Reconcile(instructions.Reconcile(
            [Candidate(instruction), Candidate(prompt), Candidate(legacy), bundled])));
        instructions.CompleteActivation(instruction, instructions.PrepareActivation(instruction), catalogue);
        prompts.CompleteActivation(prompt, prompts.PrepareActivation(prompt), catalogue);
        var promptState = store.Text["prompt-revisions.json"];
        var legacyState = instructions.IsEnabled(legacy);
        instructions.DisableExact(instruction, catalogue).Should().BeTrue();
        instructions.IsEnabled(instruction).Should().BeFalse();
        prompts.IsEnabled(prompt).Should().BeTrue();
        store.Text["prompt-revisions.json"].Should().Be(promptState);
        instructions.IsEnabled(legacy).Should().Be(legacyState).And.BeTrue();
        catalogue.Artifacts.Should().Contain(bundled);
        catalogue.Candidates.Single(item => item.Kind == ArtifactKind.Prompt).IsUseEnabled.Should().BeTrue();
        prompts.HoldCandidate(Candidate(instruction), catalogue);
        prompts.HoldCandidate(bundled, catalogue);
        prompts.IsEnabled(prompt).Should().BeTrue();
        ((Action)(() => instructions.DisableExact(prompt, catalogue))).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public void AbsentNativeCopyHoldsCannotConsumeSlotsOrRetireExistingReviewedOriginals(ArtifactKind kind)
    {
        var store = new LocalSharedSkillPreferencesTests.Store();
        var registry = new InstructionRevisionCatalogue(store, kind);
        var original = Snapshot(kind: kind);
        registry.Reconcile([Candidate(original)]);
        registry.CompleteActivation(original, registry.PrepareActivation(original));
        var bytes = store.Text;
        for (var index = 0; index < 129; index++) { registry.Hold("new-" + index, existingOnly: true); }
        store.Text.Should().Be(bytes);
        registry.IsEnabled(original).Should().BeTrue();
        registry.Hold(original.Name, existingOnly: true);
        registry.IsEnabled(original).Should().BeFalse();
        for (var index = 1; index < 128; index++) { registry.Hold("item-" + index); }
        bytes = store.Text;
        registry.Hold("not-in-inventory", existingOnly: true);
        store.Text.Should().Be(bytes);
        registry.Reconcile([Candidate(original)]).Single().IsUseEnabled.Should().BeFalse();
    }

    private static OwnedInstructionSnapshot Snapshot(bool native = true, string body = "Body", ArtifactKind kind = ArtifactKind.Instruction) =>
        new("example", new string('a', 48), new string('b', 48),
            ArtifactFileDocument.Parse(ArtifactFileDocument.Encode($"---\n{(native ? "kora-format: 1\n" : "")}name: example\ndescription: Test\n---\n{body}", false),
                kind, "kora-profile", "example"));
    private static ArtifactDefinition Candidate(OwnedInstructionSnapshot snapshot) =>
        snapshot.Document.Definition with { OwnedInstructionRevision = snapshot.Revision };

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public void CapturedUseRequiresExactEnabledOrAuthoritativeExplicitHoldRatherThanSnapshotFlags(ArtifactKind kind)
    {
        var registry = new InstructionRevisionCatalogue(new LocalSharedSkillPreferencesTests.Store(), kind);
        var snapshot = Snapshot(kind: kind);
        var advertised = Candidate(snapshot) with { IsUseEnabled = true, PreservesCapturedUse = true };
        var catalogue = new ArtifactCatalogue(registry.Reconcile([advertised]));
        var epoch = registry.CaptureReviewEpoch(snapshot);
        registry.IsCapturedUseAllowed(snapshot, epoch).Should().BeFalse();
        var prepared = registry.PrepareActivation(snapshot);
        registry.IsCapturedUseAllowed(snapshot, epoch).Should().BeFalse();
        registry.CompleteActivation(snapshot, prepared, catalogue);
        registry.IsCapturedUseAllowed(snapshot, epoch).Should().BeTrue();
        registry.IsCapturedUseAllowed(snapshot, Guid.NewGuid()).Should().BeFalse();
        registry.IsCapturedUseAllowed(snapshot, Guid.Empty).Should().BeFalse();
        registry.DisableExact(snapshot, catalogue).Should().BeTrue();
        registry.IsEnabled(snapshot).Should().BeFalse();
        registry.IsCapturedUseAllowed(snapshot, epoch).Should().BeTrue();
        registry.IsCapturedUseAllowed(snapshot, registry.CaptureReviewEpoch(snapshot)).Should().BeFalse();
        registry.IsCapturedUseAllowed(snapshot with { FileIdentity = new string('c', 48) }, epoch).Should().BeFalse();
        registry.IsCapturedUseAllowed(snapshot with { DirectoryIdentity = new string('d', 48) }, epoch).Should().BeFalse();
        registry.IsCapturedUseAllowed(Snapshot(body: "Changed", kind: kind), epoch).Should().BeFalse();
        catalogue.Publish([advertised with { IsUseEnabled = false, PreservesCapturedUse = false }]);
        registry.IsCapturedUseAllowed(snapshot, epoch).Should().BeTrue();
        registry.Hold(snapshot.Name, existingOnly: true);
        catalogue.Publish([advertised]);
        registry.IsCapturedUseAllowed(snapshot, epoch).Should().BeFalse();
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("replaced")]
    [InlineData("deleted")]
    [InlineData("suspend")]
    [InlineData("prepare")]
    [InlineData("hold")]
    [InlineData("unavailable")]
    public void CapturedUseIsRetiredBySourceOrAuthorityChangesAndCannotReviveFromStaleFlags(string change)
    {
        var registry = new InstructionRevisionCatalogue(new LocalSharedSkillPreferencesTests.Store());
        var snapshot = Snapshot();
        var catalogue = new ArtifactCatalogue(registry.Reconcile([Candidate(snapshot)]));
        registry.CompleteActivation(snapshot, registry.PrepareActivation(snapshot), catalogue);
        var epoch = registry.CaptureReviewEpoch(snapshot);
        registry.DisableExact(snapshot, catalogue);
        var stale = catalogue.Candidates.Single();
        stale.PreservesCapturedUse.Should().BeTrue();
        registry.Reconcile([stale]).Single().PreservesCapturedUse.Should().BeTrue();
        registry.IsCapturedUseAllowed(snapshot, epoch).Should().BeTrue();
        switch (change)
        {
            case "changed": registry.Reconcile([Candidate(Snapshot(body: "Changed"))]); break;
            case "replaced": registry.Reconcile([Candidate(snapshot with { FileIdentity = new string('c', 48) })]); break;
            case "deleted": registry.Reconcile([]); break;
            case "suspend": registry.SuspendReviewedUse(); break;
            case "prepare": registry.PrepareActivation(snapshot); break;
            case "hold": registry.Hold(snapshot.Name); break;
            case "unavailable": registry.MarkUnavailable(); break;
        }
        registry.IsCapturedUseAllowed(snapshot, epoch).Should().BeFalse();
        registry.Reconcile([stale]).Single().PreservesCapturedUse.Should().BeFalse();
        registry.IsCapturedUseAllowed(snapshot, epoch).Should().BeFalse();
    }

    [Theory]
    [InlineData("hold")]
    [InlineData("disable")]
    [InlineData("unavailable")]
    [InlineData("reload")]
    [InlineData("readback")]
    public void FailedAuthorityMutationsDenyCapturedUseEvenWhenOldSnapshotPreservesIt(string operation)
    {
        var store = new LocalSharedSkillPreferencesTests.Store();
        var registry = new InstructionRevisionCatalogue(store);
        var snapshot = Snapshot();
        var catalogue = new ArtifactCatalogue(registry.Reconcile([Candidate(snapshot)]));
        registry.CompleteActivation(snapshot, registry.PrepareActivation(snapshot), catalogue);
        var epoch = registry.CaptureReviewEpoch(snapshot);
        registry.DisableExact(snapshot, catalogue);
        var stale = catalogue.Candidates.Single();
        store.FailWrite = operation is not "readback";
        var saved = store.Text;
        if (operation is "readback") { store.AfterWrite = () => store.Text = "{\"Version\":1,\"Revisions\":{}}"; }
        Action fail = operation switch
        {
            "hold" => () => registry.Hold(snapshot.Name),
            "disable" => () => registry.DisableExact(snapshot, catalogue),
            "unavailable" => registry.MarkUnavailable,
            _ => () => registry.Reconcile([stale]),
        };
        fail.Should().Throw<IOException>();
        registry.IsCapturedUseAllowed(snapshot, epoch).Should().BeFalse();
        store.FailWrite = false;
        store.AfterWrite = null;
        if (operation is "readback") { store.Text = saved; }
        registry.Reconcile([stale]).Single().PreservesCapturedUse.Should().BeFalse();
        registry.IsCapturedUseAllowed(snapshot, epoch).Should().BeFalse();
    }

    [Theory]
    [InlineData("read")]
    [InlineData("malformed")]
    [InlineData("missing")]
    public void CapturedUsePreferenceFailureIsExplicitAndCannotFallBackToAnInMemoryHold(string failure)
    {
        var store = new LocalSharedSkillPreferencesTests.Store();
        var registry = new InstructionRevisionCatalogue(store);
        var snapshot = Snapshot();
        var catalogue = new ArtifactCatalogue(registry.Reconcile([Candidate(snapshot)]));
        var epoch = registry.CaptureReviewEpoch(snapshot);
        registry.DisableExact(snapshot, catalogue);
        if (failure is "read") { store.FailRead = true; }
        if (failure is "malformed") { store.Text = "malformed"; }
        if (failure is "missing") { store.Text = null; }
        ((Action)(() => registry.IsCapturedUseAllowed(snapshot, epoch))).Should().Throw<Exception>();
        registry.IsCapturedUseAllowed(snapshot, epoch).Should().BeFalse();
    }

    [Fact]
    public void CapturedUseRejectsWrongKindAndExternalSourceWithoutGrantingNewAuthority()
    {
        var registry = new InstructionRevisionCatalogue(new LocalSharedSkillPreferencesTests.Store());
        var epoch = Guid.NewGuid();
        ((Action)(() => registry.IsCapturedUseAllowed(Snapshot(kind: ArtifactKind.Prompt), epoch))).Should().Throw<InvalidDataException>();
        var source = ArtifactFileDocument.Parse(Snapshot().Document.CopyBytes(), ArtifactKind.Instruction, "profile-instruction", "example");
        ((Action)(() => registry.IsCapturedUseAllowed(Snapshot() with { Document = source }, epoch))).Should().Throw<InvalidDataException>();
        var legacy = Snapshot(native: false);
        registry.Reconcile([Candidate(legacy)]);
        epoch = registry.CaptureReviewEpoch(legacy);
        registry.IsCapturedUseAllowed(legacy, epoch).Should().BeTrue();
        registry.Hold(legacy.Name, existingOnly: true);
        registry.IsCapturedUseAllowed(legacy, epoch).Should().BeFalse();
    }

    [Fact]
    public void CapturedHoldDoesNotSurviveRestartOrAuthorizeQueuedUseAndUnavailableRecoveryRetiresReviewEpoch()
    {
        var store = new LocalSharedSkillPreferencesTests.Store();
        var registry = new InstructionRevisionCatalogue(store);
        var snapshot = Snapshot();
        var catalogue = new ArtifactCatalogue(registry.Reconcile([Candidate(snapshot)]));
        registry.CompleteActivation(snapshot, registry.PrepareActivation(snapshot), catalogue);
        var epoch = registry.CaptureReviewEpoch(snapshot);
        registry.DisableExact(snapshot, catalogue);
        var reviewEpoch = registry.CaptureReviewEpoch(snapshot);
        registry.IsEnabled(snapshot).Should().BeFalse("queued use still requires new-use authority");
        registry.IsCapturedUseAllowed(snapshot, epoch).Should().BeTrue("only an independently captured consumer may use this predicate");
        new InstructionRevisionCatalogue(store).IsCapturedUseAllowed(snapshot, epoch).Should().BeFalse();
        store.FailWrite = true;
        ((Action)(() => registry.Reconcile(catalogue.Candidates))).Should().Throw<IOException>();
        store.FailWrite = false;
        registry.Reconcile(catalogue.Candidates);
        registry.IsCapturedUseAllowed(snapshot, epoch).Should().BeFalse();
        ((Action)(() => registry.PrepareActivation(snapshot, reviewEpoch))).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public void RepeatedDisablePreservesOnlyOriginalCapturedEpochUntilFreshActivation(ArtifactKind kind)
    {
        var registry = new InstructionRevisionCatalogue(new LocalSharedSkillPreferencesTests.Store(), kind);
        var snapshot = Snapshot(kind: kind);
        var catalogue = new ArtifactCatalogue(registry.Reconcile([Candidate(snapshot)]));
        registry.CompleteActivation(snapshot, registry.PrepareActivation(snapshot), catalogue);
        var capturedEpoch = registry.CaptureReviewEpoch(snapshot);
        registry.DisableExact(snapshot, catalogue).Should().BeTrue();
        var afterFirstDisable = registry.CaptureReviewEpoch(snapshot);
        afterFirstDisable.Should().NotBe(capturedEpoch);
        registry.PublishReconciled(catalogue.Candidates, catalogue.Publish);
        registry.DisableExact(snapshot, catalogue).Should().BeFalse();
        var afterSecondDisable = registry.CaptureReviewEpoch(snapshot);
        afterSecondDisable.Should().NotBe(afterFirstDisable);
        registry.IsCapturedUseAllowed(snapshot, capturedEpoch).Should().BeTrue();
        registry.IsCapturedUseAllowed(snapshot, afterFirstDisable).Should().BeFalse();
        registry.IsCapturedUseAllowed(snapshot, afterSecondDisable).Should().BeFalse();
        registry.IsEnabled(snapshot).Should().BeFalse();
        var activation = registry.PrepareActivation(snapshot, afterSecondDisable);
        registry.IsCapturedUseAllowed(snapshot, capturedEpoch).Should().BeFalse();
        registry.CompleteActivation(snapshot, activation, catalogue);
        registry.IsCapturedUseAllowed(snapshot, capturedEpoch).Should().BeFalse();
        registry.IsCapturedUseAllowed(snapshot, afterFirstDisable).Should().BeFalse();
        registry.IsCapturedUseAllowed(snapshot, afterSecondDisable).Should().BeTrue();
        registry.DisableExact(snapshot, catalogue).Should().BeTrue();
        registry.IsCapturedUseAllowed(snapshot, capturedEpoch).Should().BeFalse();
        registry.IsCapturedUseAllowed(snapshot, afterSecondDisable).Should().BeTrue();
    }

    [Theory]
    [InlineData("hold")]
    [InlineData("hold-existing")]
    [InlineData("unavailable")]
    [InlineData("failed-reload")]
    [InlineData("failed-disable")]
    public void LaterDisableCannotRecreateRevokedCapturedEpochFromRestoredIdenticalBytes(string revocation)
    {
        var store = new LocalSharedSkillPreferencesTests.Store();
        var registry = new InstructionRevisionCatalogue(store);
        var snapshot = Snapshot();
        var catalogue = new ArtifactCatalogue(registry.Reconcile([Candidate(snapshot)]));
        registry.CompleteActivation(snapshot, registry.PrepareActivation(snapshot), catalogue);
        var capturedEpoch = registry.CaptureReviewEpoch(snapshot);
        registry.DisableExact(snapshot, catalogue);
        var stale = catalogue.Candidates.Single();
        switch (revocation)
        {
            case "hold": registry.Hold(snapshot.Name); break;
            case "hold-existing": registry.Hold(snapshot.Name, existingOnly: true); break;
            case "unavailable": registry.MarkUnavailable(); break;
            default:
                store.FailWrite = true;
                Action failure = revocation is "failed-reload"
                    ? () => registry.Reconcile([stale])
                    : () => registry.DisableExact(snapshot, catalogue);
                failure.Should().Throw<IOException>();
                registry.IsCapturedUseAllowed(snapshot, capturedEpoch).Should().BeFalse();
                store.FailWrite = false;
                break;
        }
        registry.PublishReconciled([stale], catalogue.Publish);
        var recoveryEpoch = registry.CaptureReviewEpoch(snapshot);
        registry.DisableExact(snapshot, catalogue).Should().BeFalse();
        registry.IsCapturedUseAllowed(snapshot, capturedEpoch).Should().BeFalse();
        registry.IsEnabled(snapshot).Should().BeFalse();
        registry.IsCapturedUseAllowed(snapshot, recoveryEpoch).Should().BeTrue(
            "the predicate does not attest capture; the consumer must still refuse queued or unadmitted requests");
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public void NativeRevisionsRemainDisabledAcrossReloadRestartChangeDeletionAndRestoration(ArtifactKind kind)
    {
        var store = new LocalSharedSkillPreferencesTests.Store();
        var registry = new InstructionRevisionCatalogue(store, kind);
        var snapshot = Snapshot(kind: kind);
        registry.Reconcile([Candidate(snapshot)]).Single().IsUseEnabled.Should().BeFalse();
        registry.CompleteActivation(snapshot, registry.PrepareActivation(snapshot));
        registry.Reconcile([Candidate(snapshot)]).Single().IsUseEnabled.Should().BeTrue();
        new InstructionRevisionCatalogue(store, kind).Reconcile([Candidate(snapshot)]).Single().IsUseEnabled.Should().BeFalse();
        var changed = Snapshot(body: "Changed", kind: kind);
        registry.Reconcile([Candidate(changed)]).Single().IsUseEnabled.Should().BeFalse();
        registry.Reconcile([Candidate(snapshot)]).Single().IsUseEnabled.Should().BeFalse();
        registry.CompleteActivation(snapshot, registry.PrepareActivation(snapshot));
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
        prompts.CompleteActivation(prompt, prompts.PrepareActivation(prompt));
        prompts.IsEnabled(prompt).Should().BeTrue();
        instructions.IsEnabled(instruction).Should().BeTrue();
        prompts.Hold("example");
        prompts.IsEnabled(prompt).Should().BeFalse();
        instructions.IsEnabled(instruction).Should().BeTrue();
        ((Action)(() => prompts.PrepareActivation(instruction))).Should().Throw<InvalidDataException>();
        ((Action)(() => prompts.CompleteActivation(instruction, Guid.NewGuid()))).Should().Throw<InvalidDataException>();
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
        var receipt = registry.PrepareActivation(snapshot);
        registry.SuspendReviewedUse().Should().Equal("example");
        ((Action)(() => registry.CompleteActivation(snapshot, receipt))).Should().Throw<InvalidOperationException>();
        registry.CompleteActivation(snapshot, registry.PrepareActivation(snapshot));
        registry.SuspendReviewedUse().Should().Equal("example");
        registry.Reconcile([Candidate(snapshot)]).Single().IsUseEnabled.Should().BeFalse();
        receipt = registry.PrepareActivation(snapshot);
        registry.MarkUnavailable();
        ((Action)(() => registry.CompleteActivation(snapshot, receipt))).Should().Throw<InvalidOperationException>();
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
        registry.CompleteActivation(legacy, registry.PrepareActivation(legacy));
        registry.IsEnabled(legacy).Should().BeTrue();
        registry.MarkUnavailable();
        registry.IsEnabled(legacy).Should().BeFalse();
    }
}

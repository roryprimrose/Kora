using System.Diagnostics;
using System.Text;

using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Core.Artifacts;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Definitions;

public sealed partial class InstructionEditorTests
{
    [Theory]
    [InlineData(ArtifactKind.Instruction, false, false)]
    [InlineData(ArtifactKind.Prompt, false, true)]
    [InlineData(ArtifactKind.Instruction, true, true)]
    [InlineData(ArtifactKind.Prompt, true, false)]
    public async Task ObservationPreparesExactlyTheSeparatelyConfirmedSaveWithoutWritingOrChangingUse(ArtifactKind kind, bool native, bool changed)
    {
        await using var f = new Fixture(kind);
        var text = $"---\r\n{(native ? "kora-format: 1\r\n" : "")}name: 'example'\r\ndescription: Test\r\nattribution: Own text\r\n---\r\nPrivate body.\r\n";
        var original = new OwnedInstructionSnapshot("example", new string('a', 48), new string('b', 48),
            ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(text, true), kind, "kora-profile", "example"));
        f.Files.Current = original;
        f.Catalogue.Publish(f.Revisions.Reconcile([Candidate(original)]));
        var catalogue = f.Catalogue.Candidates;
        var preferenceName = kind == ArtifactKind.Instruction ? "instruction-revisions.json" : "prompt-revisions.json";
        var preferences = f.State.ReadText(preferenceName);
        var draft = changed ? text + "Private edited body." : text;
        var observation = await f.Actions.ValidateDraft("example", original, draft, NullLogger.Instance, f.Eligible, CancellationToken.None);
        observation.Supported.Should().BeTrue();
        observation.HasBom.Should().BeTrue();
        f.Actions.IsDraftValidationCurrent(observation).Should().BeTrue();
        observation.Summary.Should().Contain("nothing saved/enabled").And.Contain("not approved")
            .And.Contain(kind == ArtifactKind.Prompt ? "literal user request" : "instruction guidance")
            .And.NotContain("Private body").And.NotContain("Own text");
        Encoding.UTF8.GetByteCount(observation.Summary).Should().BeLessThan(2048);
        f.Files.Reads.Should().Be(0);
        f.Files.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
        f.Catalogue.Candidates.Should().BeSameAs(catalogue);
        f.State.ReadText(preferenceName).Should().Be(preferences);
        f.Files.Current.Should().BeSameAs(original);
        var saved = await f.Workflow.Save("example", original, draft, false, new Review(), f.Eligible, CancellationToken.None);
        saved.Current!.Document.Definition.Digest.Should().Be(observation.Digest);
        saved.Current.Document.ByteCount.Should().Be(observation.ProposedByteCount);
        saved.Current.Document.CopyBytes().Should().Equal(ArtifactFileDocument.PrepareOwnedSave(kind, "example", original, draft).CopyBytes());
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction, "---\nkora-format: 2\nname: example\ndescription: Test\n---\nPRIVATE_BODY", "Unsupported metadata")]
    [InlineData(ArtifactKind.Prompt, "---\nkora-format: 1\nname: example\ndescription: Test\n---\nPRIVATE_BODY ${secret}", "substitution")]
    [InlineData(ArtifactKind.Prompt, "---\nname: example\ndescription: Test\ntools: secret\n---\nPRIVATE_BODY", "Unsupported metadata")]
    [InlineData(ArtifactKind.Instruction, "---\nname: instructions\ndescription: Test\n---\nPRIVATE_BODY", "reserved control")]
    [InlineData(ArtifactKind.Instruction, "---\nname: local models\ndescription: Test\n---\nPRIVATE_BODY", "built-in command")]
    [InlineData(ArtifactKind.Instruction, "unsafe-unicode", "Unicode")]
    [InlineData(ArtifactKind.Instruction, "---\nname: example\nname: example\ndescription: Test\n---\nPRIVATE_BODY", "Duplicate")]
    public async Task InvalidDraftProducesBoundedNonContentDiagnosticsAndCorrelatedSourceGeneratedLog(ArtifactKind kind, string draft, string reason)
    {
        await using var f = new Fixture(kind);
        if (reason is "Unicode") { draft = "\ud800PRIVATE_BODY"; }
        var logs = new ValidationLogger();
        var observation = await f.Actions.ValidateDraft("example", f.Files.Current, draft, logs, f.Eligible, CancellationToken.None);
        observation.Supported.Should().BeFalse();
        observation.ProposedByteCount.Should().BeNull();
        observation.Digest.Should().BeNull();
        observation.NativeFormat.Should().BeNull();
        observation.Summary.Should().Contain(reason).And.NotContain("PRIVATE_BODY").And.NotContain("secret");
        Encoding.UTF8.GetByteCount(observation.Summary).Should().BeLessThan(2048);
        logs.Events.Should().ContainSingle().Which.Should().Be("DraftRejected");
        logs.Contexts.Should().OnlyContain(context => context != null && context.Request.SessionId.Value != Guid.Empty
            && context.Activity != null && context.Activity.TraceId != default);
        logs.Messages.Should().NotContain(message => message.Contains("PRIVATE_BODY", StringComparison.Ordinal) || message.Contains("secret", StringComparison.Ordinal));
        f.Files.Reads.Should().Be(0);
        f.Files.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task CollisionDiagnosticsDoNotEchoDeclaredNamesAndNewDraftHasExactProposalMetadata()
    {
        await using var f = new Fixture();
        const string privateName = "private-declared-name";
        f.Catalogue.Publish([new ArtifactDefinition("disk.other.private", ArtifactKind.Prompt, privateName, "private description",
            privateName, [privateName], "other", "1", new string('a', 64), "private content")]);
        var draft = $"---\nname: {privateName}\ndescription: Test\n---\nPRIVATE_BODY";
        var rejected = await f.Actions.ValidateDraft("example", null, draft, NullLogger.Instance, f.Eligible, CancellationToken.None);
        rejected.Supported.Should().BeFalse();
        rejected.Summary.Should().Contain("collides").And.NotContain(privateName);
        var accepted = await f.Actions.ValidateDraft("new", null, "---\nname: new\ndescription: Test\n---\nPRIVATE_BODY",
            NullLogger.Instance, f.Eligible, CancellationToken.None);
        accepted.Supported.Should().BeTrue();
        accepted.HasBom.Should().BeFalse();
        accepted.NativeFormat.Should().BeTrue();
        accepted.Digest.Should().Be(f.Actions.PrepareSave("new", null, "---\nname: new\ndescription: Test\n---\nPRIVATE_BODY").Definition.Digest);
        f.Files.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("publication")]
    [InlineData("failure")]
    [InlineData("unavailable")]
    [InlineData("kind")]
    public async Task CatalogueIdentityFailureStateAndKindInvalidateHistoricalObservationsWithoutReconciliation(string change)
    {
        await using var f = new Fixture();
        if (change is "unavailable") { f.Catalogue.ReportReloadFailure(new IOException("private failure")); }
        var observed = await f.Actions.ValidateDraft("example", f.Files.Current, f.Files.Current!.Document.Text,
            NullLogger.Instance, f.Eligible, CancellationToken.None);
        f.Actions.IsSourceUnavailable.Should().Be(change is "unavailable");
        f.Actions.IsDraftValidationCurrent(observed).Should().BeTrue();
        if (change is "unavailable")
        {
            observed.Summary.Should().Contain("Source/catalogue unavailable");
            f.Catalogue.Publish(f.Catalogue.Candidates);
        }
        if (change is "publication") { f.Catalogue.Publish(f.Catalogue.Candidates); }
        if (change is "failure") { f.Catalogue.ReportReloadFailure(new IOException("private failure")); }
        if (change is "kind")
        {
            await using var prompt = new Fixture(ArtifactKind.Prompt);
            prompt.Actions.IsDraftValidationCurrent(observed).Should().BeFalse();
            return;
        }
        f.Actions.IsDraftValidationCurrent(observed).Should().BeFalse();
        f.Files.Reads.Should().Be(0);
        f.Files.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("generation")]
    [InlineData("cancel")]
    [InlineData("catalogue")]
    [InlineData("failure")]
    [InlineData("availability")]
    public async Task AdmissionChangesCannotDeliverAValidationResultOrAlterHeldSource(string change)
    {
        await using var f = new Fixture();
        if (change is "availability") { f.Catalogue.ReportReloadFailure(new IOException("Unavailable")); }
        using var cancellation = new CancellationTokenSource();
        f.Control.BeforeOperation = () =>
        {
            if (change is "host") { f.Open = false; }
            if (change is "generation") { f.Control.Authority = f.Control.Authority! with { Generation = new(2) }; }
            if (change is "cancel") { cancellation.Cancel(); }
            if (change is "catalogue") { f.Catalogue.Publish(f.Catalogue.Candidates); }
            if (change is "failure") { f.Catalogue.ReportReloadFailure(new IOException("Unavailable")); }
            if (change is "availability") { f.Catalogue.Publish(f.Catalogue.Candidates); }
        };
        var validation = () => f.Actions.ValidateDraft("example", f.Files.Current, f.Files.Current!.Document.Text,
            NullLogger.Instance, f.Eligible, cancellation.Token);
        await validation.Should().ThrowAsync<Exception>();
        f.Files.Reads.Should().Be(0);
        f.Files.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task NonUserOriginCannotValidateUnderSyntheticAuthority()
    {
        await using var f = new Fixture();
        using var incoming = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request);
        var validation = () => f.Actions.ValidateDraft("example", f.Files.Current, f.Files.Current!.Document.Text,
            NullLogger.Instance, f.Eligible, CancellationToken.None);
        await validation.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("enabled", false)]
    [InlineData("enabled", true)]
    [InlineData("held", false)]
    [InlineData("held", true)]
    [InlineData("source-failed", false)]
    [InlineData("source-failed", true)]
    public async Task ValidationNeverChangesExistingEnablementOrUncertainPublicationHolds(string state, bool invalid)
    {
        await using var f = new Fixture();
        var original = f.Files.Current!;
        await f.Actions.Activate(original, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        if (state is "held")
        { await f.Actions.Disable(original, _ => Task.FromResult(true), f.Eligible, CancellationToken.None); }
        if (state is "source-failed")
        {
            f.Files.FailWrite = true;
            var save = () => f.Workflow.Save("example", original, original.Document.Text + "Changed",
                false, new Review(), f.Eligible, CancellationToken.None);
            await save.Should().ThrowAsync<IOException>();
            f.Catalogue.ReportReloadFailure(new IOException("Source unconfirmed"));
        }
        var reads = f.Files.Reads;
        var writes = f.Files.Writes;
        var auditCount = f.Audit.Events.Count;
        var preferences = f.State.Text;
        var preferenceWrites = f.State.Writes;
        var catalogue = f.Catalogue.Candidates;
        var observation = await f.Actions.ValidateDraft("example", original,
            invalid ? "---\nkora-format: 99\ndescription: Test\n---\nBody" : original.Document.Text,
            NullLogger.Instance, f.Eligible, CancellationToken.None);
        observation.Supported.Should().Be(!invalid);
        f.Catalogue.Candidates.Should().BeSameAs(catalogue);
        f.Catalogue.Artifacts.Count.Should().Be(state is "enabled" ? 1 : 0);
        f.Revisions.IsEnabled(original).Should().Be(state is "enabled");
        f.Files.Reads.Should().Be(reads);
        f.Files.Writes.Should().Be(writes);
        f.Audit.Events.Count.Should().Be(auditCount);
        f.State.Text.Should().Be(preferences);
        f.State.Writes.Should().Be(preferenceWrites);
    }

    private sealed class ValidationLogger : ILogger
    {
        internal readonly List<string?> Events = [];
        internal readonly List<string> Messages = [];
        internal readonly List<HostActivity?> Contexts = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { Events.Add(eventId.Name); Messages.Add(formatter(state, exception)); Contexts.Add(HostActivity.Current); }
    }
}

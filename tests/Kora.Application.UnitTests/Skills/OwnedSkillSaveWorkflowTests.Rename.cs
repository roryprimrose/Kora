using AwesomeAssertions;
using Kora.Application.Skills;
using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Skills;

namespace Kora.Application.UnitTests.Skills;

public sealed partial class OwnedSkillSaveWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MarkdownRenameRequestsOnlyPrimaryDeclarationAndRejectsBeforeReviewsAuditOrEffects(bool complete)
    {
        var package = OwnedDeclarativeSkillDocument.CreatePackage("example", "1.0.0", "Example", "Description",
            "Literal", new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["notes.md"] = "Private reference"u8.ToArray() });
        var document = complete ? package : OwnedDeclarativeSkillDocument.Parse(package.ManifestBytes.Span, package.InstructionBytes.Span);
        var sessions = new Sessions();
        await using var admission = new OwnedSkillControlAdmission(sessions, sessions, new(sessions));
        var store = new RenameStore { Original = new(document, "d", "m", "i") };
        var audit = new Audit();
        var review = new RenameReview();
        var flow = new OwnedSkillRenameWorkflow(store, store, admission, audit, TimeProvider.System);
        var run = () => flow.Rename("example", "1.0.0", "renamed", false,
            new(RequestOrigin.LocalUi, static () => true), static () => true, review, Token);
        await run.Should().ThrowAsync<InvalidDataException>();
        store.ReadScope.Should().Be(OwnedSkillReadScope.PrimaryDeclaration);
        review.Calls.Should().BeEmpty();
        store.Writes.Should().Be(0);
        audit.Events.Should().BeEmpty();
        store.Original.Document.Should().BeSameAs(document);
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.ActivatedVoice, false)]
    [InlineData(RequestOrigin.LocalUi, true)]
    [InlineData(RequestOrigin.ActivatedVoice, true)]
    public async Task RenameAndRecoveryRequireBothExactReviewsAndPreserveOriginalOrigin(RequestOrigin origin, bool recovery)
    {
        var sessions = new Sessions();
        await using var admission = new OwnedSkillControlAdmission(sessions, sessions, new(sessions));
        var store = new RenameStore();
        var audit = new Audit();
        var flow = new OwnedSkillRenameWorkflow(store, store, admission, audit, TimeProvider.System);
        var authority = new OwnedSkillAuthoringAuthority(origin, static () => true);
        (await flow.Pending(authority, Token)).Should().ContainSingle();
        var review = new RenameReview();
        var result = await flow.Rename("example", "1.0.0", recovery ? null : "renamed", recovery,
            authority, static () => true, review, Token);
        result!.Document.Id.Should().Be("renamed");
        result.Document.IsAvailableForInvocation.Should().BeFalse();
        review.Calls.Should().Equal("review", "confirm");
        review.Recovery.Should().Be(recovery);
        store.Writes.Should().Be(1);
        audit.Events.Select(e => e.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        audit.Events.Should().OnlyContain(e => e.Initiator == (origin == RequestOrigin.ActivatedVoice
            ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser));
    }

    [Theory]
    [InlineData("review")]
    [InlineData("confirm")]
    [InlineData("input-before")]
    [InlineData("input-after")]
    [InlineData("expiry")]
    [InlineData("authority")]
    [InlineData("cancel")]
    public async Task UnconfirmedStaleExpiredOrWithdrawnRenameNeverWrites(string boundary)
    {
        var sessions = new Sessions();
        await using var admission = new OwnedSkillControlAdmission(sessions, sessions, new(sessions));
        var store = new RenameStore();
        var audit = new Audit();
        var clock = new RenameClock();
        var flow = new OwnedSkillRenameWorkflow(store, store, admission, audit, clock);
        var authority = new OwnedSkillAuthoringAuthority(RequestOrigin.LocalUi, static () => true);
        var exact = boundary is not "input-before";
        using var cancel = new CancellationTokenSource();
        var review = new RenameReview
        {
            First = boundary is not "review", Second = boundary is not "confirm",
            AfterReview = () =>
            {
                if (boundary is "input-after") { exact = false; }
                if (boundary is "expiry") { clock.Ticks = TimeSpan.FromMinutes(2).Ticks; }
                if (boundary is "authority") { authority.Retire(); }
                if (boundary is "cancel") { cancel.Cancel(); }
            },
        };
        var run = () => flow.Rename("example", "1.0.0", "renamed", false, authority, () => exact, review, cancel.Token);
        if (boundary is "cancel") { await run.Should().ThrowAsync<OperationCanceledException>(); }
        else { (await run()).Should().BeNull(); }
        store.Writes.Should().Be(0);
        audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("new-id")]
    [InlineData("invalid-id")]
    [InlineData("recovery-missing")]
    [InlineData("recovery-other-version")]
    public async Task MissingOrInvalidExactIdentityNeverWrites(string boundary)
    {
        var sessions = new Sessions();
        await using var admission = new OwnedSkillControlAdmission(sessions, sessions, new(sessions));
        var store = new RenameStore { Missing = boundary is "missing" };
        var audit = new Audit();
        var flow = new OwnedSkillRenameWorkflow(store, store, admission, audit, TimeProvider.System);
        var run = () => flow.Rename(boundary is "invalid-id" ? "../x" : boundary is "recovery-missing" ? "other" : "example",
            boundary is "recovery-other-version" ? "2.0.0" : "1.0.0", boundary is "new-id" ? null : "renamed",
            boundary.StartsWith("recovery", StringComparison.Ordinal), new(RequestOrigin.LocalUi, static () => true),
            static () => true, new RenameReview(), Token);
        if (boundary.StartsWith("recovery", StringComparison.Ordinal))
        { await run.Should().ThrowAsync<InvalidOperationException>(); }
        else { await run.Should().ThrowAsync<InvalidDataException>(); }
        store.Writes.Should().Be(0);
        audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("uncertain")]
    [InlineData("cancel")]
    [InlineData("terminal")]
    public async Task NativeOrPostEffectFailuresNeverClaimSuccessAndKeepTruthfulAudit(string boundary)
    {
        var sessions = new Sessions();
        await using var admission = new OwnedSkillControlAdmission(sessions, sessions, new(sessions));
        var authority = new OwnedSkillAuthoringAuthority(RequestOrigin.LocalUi, static () => true);
        var store = new RenameStore
        {
            Failure = boundary switch
            {
                "failure" => new IOException("injected"),
                "uncertain" => new OwnedSkillRenameUnconfirmedException(OwnedSkillRenameEffect.IntentMayExist, new IOException("injected")),
                "cancel" => new OperationCanceledException(),
                _ => null,
            },
            After = boundary is "terminal" ? authority.Retire : null,
        };
        var audit = new Audit();
        var flow = new OwnedSkillRenameWorkflow(store, store, admission, audit, TimeProvider.System);
        var run = () => flow.Rename("example", "1.0.0", "renamed", false, authority, static () => true, new RenameReview(), Token);
        if (boundary is "terminal" or "uncertain") { await run.Should().ThrowAsync<OwnedSkillRenameUnconfirmedException>(); }
        else if (boundary is "cancel") { await run.Should().ThrowAsync<OperationCanceledException>(); }
        else { await run.Should().ThrowAsync<IOException>(); }
        audit.Events.Select(e => e.Outcome).Should().Equal(SecurityAuditOutcome.Requested,
            boundary is "uncertain" or "terminal" ? SecurityAuditOutcome.Unknown
            : boundary is "cancel" ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed);
    }

    [Theory]
    [InlineData("owned skill rename example 1.0.0 renamed", "rename")]
    [InlineData("Nova, owned skill recover-rename example 1.0.0", "recover-rename")]
    [InlineData("owned skill rename example 1.0.0 CON", "Invalid")]
    [InlineData("owned skill rename ../x 1.0.0 renamed", "Invalid")]
    [InlineData("owned skill recover-rename example 01.0.0", "Invalid")]
    [InlineData("owned skill rename example 1.0.0 renamed\n", "Invalid")]
    [InlineData("owned skill recover-rename example 1.0.0\n", "Invalid")]
    [InlineData("owned skill rename example 1.0.0", "Invalid")]
    [InlineData("owned skill recover-rename example 1.0.0 extra", "Invalid")]
    public void RenameGrammarRequiresExactCompleteOriginalIdentity(string text, string operation) =>
        OwnedSkillAuthoringCommand.Parse(text, "Nova")!.Operation.Should().Be(operation);

    private sealed class RenameClock : TimeProvider
    {
        internal long Ticks { get; set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Ticks;
    }

    private sealed class RenameReview : IOwnedSkillRenameReview
    {
        internal bool First { get; init; } = true;
        internal bool Second { get; init; } = true;
        internal Action? AfterReview { get; init; }
        internal List<string> Calls { get; } = [];
        internal bool Recovery { get; private set; }
        public Task<bool> ReviewRename(OwnedSkillRename proposal, string source, string destination, bool recovery, CancellationToken token)
        {
            proposal.Renamed.Id.Should().Be("renamed");
            source.Should().Be("example");
            destination.Should().Be("renamed");
            Calls.Add("review");
            Recovery = recovery;
            AfterReview?.Invoke();
            return Task.FromResult(First);
        }
        public Task<bool> ConfirmRename(OwnedSkillRename proposal, string source, string destination, bool recovery, CancellationToken token)
        { Calls.Add("confirm"); return Task.FromResult(Second); }
    }

    private sealed class RenameStore : IOwnedDeclarativeSkillStore, IOwnedSkillRenameStore
    {
        internal OwnedDeclarativeSkillSnapshot Original { get; init; } = new(
            OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "Literal"), "d", "m", "i");
        internal OwnedSkillReadScope? ReadScope { get; private set; }
        internal int Writes { get; private set; }
        internal bool Missing { get; init; }
        internal Exception? Failure { get; init; }
        internal Action? After { get; init; }
        public string Destination(OwnedDeclarativeSkillDocument document) => document.Id;
        public Task<IReadOnlyList<OwnedSkillRename>> PendingRenames(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<OwnedSkillRename>>([new(Original, "renamed")]);
        public Task<OwnedDeclarativeSkillSnapshot> Rename(OwnedSkillRename proposal, bool recovery, Func<bool> eligible,
            Func<Func<OwnedDeclarativeSkillSnapshot>, Task<OwnedDeclarativeSkillSnapshot>> commit, CancellationToken token) =>
            commit(() =>
            {
                eligible().Should().BeTrue();
                if (Failure is not null) { throw Failure; }
                Writes++;
                After?.Invoke();
                return new(proposal.Renamed, "new-d", "new-m", "new-i");
            });
        public Task<OwnedDeclarativeSkillSnapshot?> Read(string id, string version, CancellationToken token,
            OwnedSkillReadScope scope = OwnedSkillReadScope.CompletePackage, OwnedDeclarativeSkillSnapshot? expectedPrimary = null)
        {
            ReadScope = scope;
            return Task.FromResult(Missing ? null : Original);
        }
        public Task<IReadOnlyList<OwnedDeclarativeSkillSnapshot>> List(CancellationToken token) => throw new NotSupportedException();
        public Task<OwnedDeclarativeSkillSnapshot> Publish(OwnedDeclarativeSkillDocument document, Func<bool> eligible,
            Func<Func<OwnedDeclarativeSkillSnapshot>, Task<OwnedDeclarativeSkillSnapshot>> commit, CancellationToken token,
            OwnedDeclarativeSkillSnapshot? expectedSource = null) =>
            throw new NotSupportedException();
    }
}

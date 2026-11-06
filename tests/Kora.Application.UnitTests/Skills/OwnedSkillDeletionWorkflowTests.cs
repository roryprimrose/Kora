using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Skills;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Skills;

[Collection("Host tracing")]
public sealed class OwnedSkillDeletionWorkflowTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public OwnedSkillDeletionWorkflowTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task SeparateExactReviewsDeleteThroughOriginalAdmittedCommit(RequestOrigin origin)
    {
        var sessions = new Sessions();
        await using var admission = new OwnedSkillControlAdmission(sessions, sessions, new(sessions));
        var store = new Store();
        var audit = new Audit();
        var review = new Review();
        var flow = new OwnedSkillDeletionWorkflow(store, admission, audit, TimeProvider.System);
        (await flow.Delete("example", "1.0.0", new(origin, static () => true), static () => true, review, Token)).Should().BeTrue();
        review.Calls.Should().Equal("review", "confirm");
        review.Source.Should().BeSameAs(store.Source);
        store.Deletions.Should().Be(1);
        store.Validations.Should().Be(1);
        audit.Events.Select(e => e.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        audit.Events.Should().OnlyContain(e => string.Equals(e.ActionId, "definition.owned-skill.delete", StringComparison.Ordinal)
            && e.Initiator == (origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser));
    }

    [Theory]
    [InlineData("review")]
    [InlineData("confirm")]
    [InlineData("original")]
    [InlineData("draft")]
    [InlineData("expired")]
    [InlineData("confirm-draft")]
    [InlineData("cancel")]
    [InlineData("source")]
    [InlineData("prepare")]
    [InlineData("pre-admission")]
    public async Task DeniedOrStaleInputCannotDeleteOrIssueSuccessAudit(string scenario)
    {
        var sessions = new Sessions();
        await using var admission = new OwnedSkillControlAdmission(sessions, sessions, new(sessions));
        var time = new Clock();
        var store = new Store
        {
            PrepareFailure = scenario is "prepare" ? new InvalidDataException("unsupported source") : null,
            ValidationFailure = scenario is "source" ? new InvalidDataException("changed source") : null,
        };
        var audit = new Audit();
        var authority = new OwnedSkillAuthoringAuthority(RequestOrigin.ActivatedVoice, static () => true);
        var exact = scenario is not "pre-admission";
        using var cancel = new CancellationTokenSource();
        var review = new Review
        {
            First = scenario is not "review",
            Second = scenario is not "confirm",
            AfterReview = () =>
            {
                if (scenario is "original") { authority.Retire(); }
                if (scenario is "draft") { exact = false; }
                if (scenario is "expired") { time.Now = time.Now.AddMinutes(2); }
                if (scenario is "cancel") { cancel.Cancel(); }
            },
            AfterConfirm = () => { if (scenario is "confirm-draft") { exact = false; } },
        };
        var flow = new OwnedSkillDeletionWorkflow(store, admission, audit, time);
        var operation = () => flow.Delete("example", "1.0.0", authority, () => exact, review, cancel.Token);
        if (scenario is "cancel") { await operation.Should().ThrowAsync<OperationCanceledException>(); }
        else if (scenario is "source" or "prepare") { await operation.Should().ThrowAsync<InvalidDataException>(); }
        else if (scenario is "confirm-draft" or "pre-admission") { await operation.Should().ThrowAsync<InvalidOperationException>(); }
        else { (await operation()).Should().BeFalse(); }
        store.Deletions.Should().Be(0);
        audit.Events.Should().BeEmpty();
        if (scenario is "pre-admission") { store.Preparations.Should().Be(0); }
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("cancelled")]
    [InlineData("uncertain")]
    [InlineData("requested-audit")]
    [InlineData("terminal-audit")]
    [InlineData("unknown-audit")]
    [InlineData("failed-audit")]
    [InlineData("terminal-task")]
    [InlineData("late-authority")]
    public async Task FailedAndUncertainEffectsNeverReturnConfirmedDeletion(string scenario)
    {
        var sessions = new Sessions();
        await using var admission = new OwnedSkillControlAdmission(sessions, sessions, new(sessions));
        var authority = new OwnedSkillAuthoringAuthority(RequestOrigin.LocalUi, static () => true);
        var store = new Store
        {
            Failure = scenario switch
            {
                "failed" or "failed-audit" => new IOException("before effect"),
                "cancelled" => new OperationCanceledException(),
                "uncertain" or "unknown-audit" => new OwnedSkillDeletionUnconfirmedException(new IOException("partial deletion")),
                _ => null,
            },
            AfterDeletion = () =>
            {
                if (scenario is "terminal-task") { sessions.FailTerminal = true; }
                if (scenario is "late-authority") { authority.Retire(); }
            },
        };
        var audit = new Audit { Failure = scenario is "requested-audit" ? SecurityAuditOutcome.Requested
            : scenario is "terminal-audit" ? SecurityAuditOutcome.Succeeded
            : scenario is "unknown-audit" ? SecurityAuditOutcome.Unknown
            : scenario is "failed-audit" ? SecurityAuditOutcome.Failed : null };
        var flow = new OwnedSkillDeletionWorkflow(store, admission, audit, TimeProvider.System);
        var operation = () => flow.Delete("example", "1.0.0", authority, static () => true, new Review(), Token);
        if (scenario is "cancelled") { await operation.Should().ThrowAsync<OperationCanceledException>(); }
        else if (scenario is "uncertain" or "terminal-audit" or "terminal-task" or "late-authority" or "unknown-audit")
        { await operation.Should().ThrowAsync<OwnedSkillDeletionUnconfirmedException>(); }
        else { await operation.Should().ThrowAsync<IOException>(); }
        if (scenario is "failed") { audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed); }
        if (scenario is "cancelled") { audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Cancelled); }
        if (scenario is "uncertain" or "terminal-audit" or "late-authority")
        { audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Unknown); }
        if (scenario is "requested-audit") { store.Deletions.Should().Be(0); }
    }

    [Theory]
    [InlineData("owned skill delete example 1.0.0", "delete")]
    [InlineData("Nova, owned skill delete example 1.0.0", "delete")]
    [InlineData("owned skill delete ..\\outside 1.0.0", "Invalid")]
    [InlineData("owned skill delete example 1.0.0\n", "Invalid")]
    [InlineData("owned skill delete example 01.0.0", "Invalid")]
    [InlineData("owned skill delete", "Invalid")]
    public void DeletionRouteRequiresOneExactOwnedIdentity(string command, string operation) =>
        OwnedSkillAuthoringCommand.Parse(command, "Nova")!.Operation.Should().Be(operation);

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Sessions : AudioControlTestStore, IOwnedSkillControlSessionStore
    {
        public ValueTask<WorkSessionAuthorization> CreateOwnedSkillSession(HostRequest request, Func<bool> admitted, CancellationToken token) =>
            CreateAudioControlSessionAsync(request, admitted, token);
        public ValueTask<T> WithOwnedSkillSession<T>(HostRequest request, HostRevision generation, Func<T> operation, CancellationToken token) =>
            WithAudioControlSessionAsync(request, generation, operation, token);
    }
    private sealed class Audit : ISecurityAuditLog
    {
        internal SecurityAuditOutcome? Failure { get; init; }
        internal List<SecurityAuditEvent> Events { get; } = [];
        public void Write(SecurityAuditEvent auditEvent)
        {
            if (auditEvent.Outcome == Failure) { throw new IOException("audit delivery failed"); }
            Events.Add(auditEvent);
        }
    }
    private sealed class Review : IOwnedSkillDeletionReview
    {
        internal bool First { get; init; } = true;
        internal bool Second { get; init; } = true;
        internal Action? AfterReview { get; init; }
        internal Action? AfterConfirm { get; init; }
        internal OwnedSkillDeletionSnapshot? Source { get; private set; }
        internal List<string> Calls { get; } = [];
        public Task<bool> ReviewDeletion(OwnedSkillDeletionSnapshot source, CancellationToken token)
        { Source = source; Calls.Add("review"); AfterReview?.Invoke(); return Task.FromResult(First); }
        public Task<bool> ConfirmDeletion(OwnedSkillDeletionSnapshot source, CancellationToken token)
        { source.Should().BeSameAs(Source); Calls.Add("confirm"); AfterConfirm?.Invoke(); return Task.FromResult(Second); }
    }
    private sealed class Store : IOwnedSkillDeletionStore
    {
        internal OwnedSkillDeletionSnapshot Source { get; } = new(
            new(OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "literal"), "directory", "manifest", "guidance"),
            "fixed-owned-destination", "root", "parent", "catalogue");
        internal int Preparations { get; private set; }
        internal int Validations { get; private set; }
        internal int Deletions { get; private set; }
        internal Exception? PrepareFailure { get; init; }
        internal Exception? ValidationFailure { get; init; }
        internal Exception? Failure { get; init; }
        internal Action? AfterDeletion { get; init; }
        public Task<OwnedSkillDeletionSnapshot> PrepareDeletion(string id, string version, CancellationToken token)
        {
            Preparations++;
            if (PrepareFailure is not null) { throw PrepareFailure; }
            return Task.FromResult(Source);
        }
        public Task ValidateDeletion(OwnedSkillDeletionSnapshot expected, CancellationToken token)
        {
            Validations++;
            expected.Should().BeSameAs(Source);
            if (ValidationFailure is not null) { throw ValidationFailure; }
            return Task.CompletedTask;
        }
        public async Task Delete(OwnedSkillDeletionSnapshot expected, Func<bool> eligible, Func<Func<bool>, Task<bool>> commit, CancellationToken token)
        {
            expected.Should().BeSameAs(Source);
            await commit(() =>
            {
                eligible().Should().BeTrue();
                if (Failure is not null) { throw Failure; }
                Deletions++;
                AfterDeletion?.Invoke();
                return true;
            });
        }
    }
}

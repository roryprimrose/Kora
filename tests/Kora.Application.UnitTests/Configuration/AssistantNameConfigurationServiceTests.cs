using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Neovolve.Logging.Xunit;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class AssistantNameConfigurationServiceTests(ITestOutputHelper output)
    : LoggingTestsBase<AssistantNameConfigurationService>(output)
{
    [Fact]
    public async Task Defaults_legacy_normalization_reset_and_notifications_preserve_provenance()
    {
        using var fixture = new Fixture();
        var initial = fixture.Service.Get();
        initial.Name.Should().Be("Kora");
        initial.IsSaved.Should().BeFalse();
        initial.IsAvailable.Should().BeTrue();
        fixture.Service.Reload();
        fixture.Service.Get().Should().Be(initial);
        var notifications = 0;
        fixture.Service.Changed += (_, _) => notifications++;
        var proposal = fixture.Propose("  ノヴァ\tPrime ");
        proposal.Value.Should().Be("  ノヴァ\tPrime ");
        proposal.Revision.Should().Be(initial.Revision);
        proposal.CallRevision.Should().Be(0);
        proposal.Origin.Should().Be(RequestOrigin.LocalUi);
        proposal.Initiator.Should().Be(SecurityAuditInitiator.TypedCommand);
        var result = await fixture.ApplyAsync(proposal);
        result.Succeeded.Should().BeTrue();
        result.Changed.Should().BeTrue();
        result.State.Name.Should().Be("ノヴァ Prime");
        result.State.IsSaved.Should().BeTrue();
        fixture.Preferences.Name.Should().Be("ノヴァ Prime");
        using var restarted = fixture.CreateService();
        restarted.Get().Name.Should().Be("ノヴァ Prime");
        (await fixture.ApplyAsync(fixture.Propose("ノヴァ Prime"))).Changed.Should().BeFalse();
        fixture.Audit.Events.Last().ReasonCode.Should().Be("no-change");
        var reset = fixture.Service.ProposeReset(fixture.Service.Get().Revision, 0,
            RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser);
        (await fixture.ApplyAsync(reset)).State.Name.Should().Be("Kora");
        notifications.Should().Be(2);
        fixture.Retirements.Should().Be(2);
        fixture.Preferences.Writes.Should().Be(2);
        fixture.Audit.Events.Chunk(2).Should().OnlyContain(pair =>
            pair[0].CorrelationId == pair[1].CorrelationId && pair[0].Outcome == SecurityAuditOutcome.Requested);
        fixture.Service.Reload();
        notifications.Should().Be(2);
    }

    [Fact]
    public async Task Existing_exact_limits_and_Unicode_semantics_are_not_reinterpreted()
    {
        using var fixture = new Fixture();
        var at = new string('é', AssistantNameRules.MaximumLength);
        (await fixture.ApplyAsync(fixture.Propose(" \t" + at + " "))).Succeeded.Should().BeTrue();
        foreach (var invalid in new[] { at + "é", "one two three four", "e\u0301", "😀", "!!!", "---", "", " ", "Supported Commands" })
        {
            var result = await fixture.ApplyAsync(fixture.Propose(invalid));
            result.Succeeded.Should().BeFalse();
            result.Reason.Should().Be("invalid-name");
            result.Error.Should().NotBeNullOrEmpty();
            result.State.Name.Should().Be(at);
        }
        fixture.Preferences.Writes.Should().Be(1);
        (await fixture.ApplyAsync(fixture.Propose("one two three"))).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Invalid_saved_state_is_unavailable_and_explicit_reset_recovers(int failure)
    {
        using var fixture = new Fixture(Logger);
        fixture.Preferences.Name = failure == 0 ? "Supported Commands" : "Nova";
        fixture.Preferences.LoadFailure = failure switch
        {
            1 => new InvalidDataException("unknown format"),
            2 => new IOException("disk"),
            3 => new UnauthorizedAccessException("access"),
            4 => new ArgumentException("invalid domain"),
            _ => null,
        };
        fixture.Service.Get().IsAvailable.Should().BeFalse();
        fixture.Service.Reload();
        var bad = fixture.Service.Get();
        bad.Name.Should().BeNull();
        bad.IsAvailable.Should().BeFalse();
        bad.IsSaved.Should().BeTrue();
        bad.Recovery.Should().Contain("explicitly set/reset");
        fixture.Preferences.Writes.Should().Be(0);
        var result = await fixture.ApplyAsync(fixture.Service.ProposeReset(bad.Revision, 0,
            RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser));
        result.Succeeded.Should().BeTrue();
        result.State.Name.Should().Be("Kora");
        result.State.Revision.Should().BeGreaterThan(bad.Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_storage_keeps_prior_revision_and_truthful_terminal_outcome(bool access)
    {
        using var fixture = new Fixture(Logger);
        var proposal = fixture.Propose("Nova");
        var before = fixture.Service.Get();
        fixture.Preferences.SaveFailure = access ? new UnauthorizedAccessException("denied") : new IOException("disk");
        var result = await fixture.ApplyAsync(proposal);
        result.Succeeded.Should().BeFalse();
        result.State.Should().Be(before);
        result.Reason.Should().Be(access ? "access-denied" : "io-error");
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed);
        fixture.Retirements.Should().Be(1);
    }

    [Theory]
    [InlineData(RequestOrigin.ActivatedVoice, SecurityAuditInitiator.TypedCommand, CallState.Active, false)]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.VoiceCommand, CallState.Unknown, false)]
    [InlineData(RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand, CallState.Clear, true)]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser, CallState.Unknown, true)]
    [InlineData(RequestOrigin.HostSystem, SecurityAuditInitiator.LocalUser, CallState.Clear, false)]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.ModelSuggestion, CallState.Clear, false)]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.System, CallState.Clear, false)]
    public async Task All_mutations_revalidate_original_origin_and_call_policy(
        RequestOrigin origin, SecurityAuditInitiator initiator, CallState call, bool allowed)
    {
        using var fixture = new Fixture();
        fixture.Automatic.Set(call);
        var proposal = fixture.Service.Propose("Nova", fixture.Service.Get().Revision,
            fixture.Policy.Current.Revision, origin, initiator);
        (await fixture.ApplyAsync(proposal)).Succeeded.Should().Be(allowed);
        fixture.Preferences.Writes.Should().Be(allowed ? 1 : 0);
    }

    [Fact]
    public async Task Revision_ownership_host_and_call_races_never_save()
    {
        using var fixture = new Fixture();
        var stale = fixture.Propose("Nova");
        await fixture.ApplyAsync(fixture.Propose("Atlas"));
        (await fixture.ApplyAsync(stale)).Reason.Should().Be("stale");
        using var foreign = fixture.CreateService();
        var other = foreign.Propose("Nova", 0, 0, RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser);
        var wrong = () => fixture.ApplyAsync(other);
        await wrong.Should().ThrowAsync<ArgumentException>();
        var nullProposal = () => fixture.ApplyAsync(null!);
        await nullProposal.Should().ThrowAsync<ArgumentNullException>();
        var invalidInitiator = () => fixture.Service.Propose("Nova", 0, 0,
            RequestOrigin.LocalUi, (SecurityAuditInitiator)999);
        invalidInitiator.Should().Throw<ArgumentOutOfRangeException>();
        fixture.HostEligible = false;
        (await fixture.ApplyAsync(fixture.Propose("Nova"))).Reason.Should().Be("hostunavailable");
        fixture.HostEligible = true;
        fixture.BeforeRetire = () => fixture.HostEligible = false;
        (await fixture.ApplyAsync(fixture.Propose("Nova"))).Reason.Should().Be("hostunavailable");
        fixture.HostEligible = true;
        fixture.BeforeRetire = () => fixture.Automatic.Set(CallState.Active);
        (await fixture.ApplyAsync(fixture.Propose("Nova"))).Reason.Should().Be("staleobservation");
        fixture.BeforeRetire = () =>
        {
            fixture.Preferences.Name = "Changed";
            fixture.Service.Reload();
        };
        (await fixture.ApplyAsync(fixture.Propose("Nova"))).Reason.Should().Be("stale");
        fixture.Preferences.Writes.Should().Be(1);
    }

    [Fact]
    public async Task Host_context_is_bound_and_voice_cannot_be_relabelled_by_a_later_button()
    {
        using var fixture = new Fixture();
        AssistantNameProposal proposal;
        using (var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request))
        {
            proposal = fixture.Propose("Nova");
            proposal.Origin.Should().Be(RequestOrigin.ActivatedVoice);
            fixture.Automatic.Set(CallState.Active);
            (await fixture.ApplyAsync(proposal)).Succeeded.Should().BeFalse();
        }
        using var other = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        (await fixture.ApplyAsync(proposal)).Reason.Should().Be("host-context-changed");
        var sameRequest = new HostRequest(proposal.Request.RequestId,
            new(Guid.NewGuid()), proposal.Request.TaskId, RequestOrigin.LocalUi);
        using var changedSession = HostActivity.BeginRoot(sameRequest, HostActivityLayer.Application, HostOperation.Request);
        (await fixture.ApplyAsync(proposal)).Reason.Should().Be("host-context-changed");
        using var changedTask = HostActivity.BeginRoot(new HostRequest(proposal.Request.RequestId,
            proposal.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        (await fixture.ApplyAsync(proposal)).Reason.Should().Be("host-context-changed");
    }

    [Fact]
    public async Task Cancellation_before_request_during_retirement_and_after_atomic_commit_is_truthful()
    {
        using var fixture = new Fixture();
        using var early = new CancellationTokenSource();
        early.Cancel();
        var proposal = fixture.Propose("Nova");
        var before = () => fixture.ApplyWithCancellationAsync(proposal, early.Token);
        await before.Should().ThrowAsync<OperationCanceledException>();
        fixture.Audit.Events.Should().BeEmpty();
        using var requested = new CancellationTokenSource();
        fixture.Audit.BeforeRequested = requested.Cancel;
        var admission = () => fixture.ApplyWithCancellationAsync(proposal, requested.Token);
        await admission.Should().ThrowAsync<OperationCanceledException>();
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Cancelled);
        fixture.Audit.BeforeRequested = null;
        using var retiring = new CancellationTokenSource();
        fixture.BeforeRetire = retiring.Cancel;
        var retirement = () => fixture.ApplyWithCancellationAsync(proposal, retiring.Token);
        await retirement.Should().ThrowAsync<OperationCanceledException>();
        fixture.Preferences.Writes.Should().Be(0);
        fixture.BeforeRetire = null;
        using var committing = new CancellationTokenSource();
        fixture.Preferences.AfterWrite = committing.Cancel;
        (await fixture.ApplyWithCancellationAsync(proposal, committing.Token)).Succeeded.Should().BeTrue();
        fixture.Preferences.Name.Should().Be("Nova");
    }

    [Fact]
    public async Task Audit_failures_never_publish_unaudited_authority_and_capture_failure_never_commits()
    {
        using var fixture = new Fixture(Logger);
        var proposal = fixture.Propose("Nova");
        fixture.Audit.FailureOutcome = SecurityAuditOutcome.Requested;
        var request = () => fixture.ApplyAsync(proposal);
        await request.Should().ThrowAsync<InvalidOperationException>();
        fixture.Retirements.Should().Be(0);
        fixture.Audit.FailureOutcome = null;
        fixture.BeforeRetire = () => throw new InvalidOperationException("not quiescent");
        var stopped = await fixture.ApplyAsync(proposal);
        stopped.Reason.Should().Be("capture-stop-failed");
        fixture.Preferences.Writes.Should().Be(0);
        fixture.BeforeRetire = null;
        fixture.Audit.FailureOutcome = SecurityAuditOutcome.Succeeded;
        var terminal = () => fixture.ApplyAsync(proposal);
        await terminal.Should().ThrowAsync<InvalidOperationException>();
        fixture.Preferences.Name.Should().Be("Nova");
        fixture.Service.Get().IsAvailable.Should().BeFalse();
        fixture.Service.Get().Recovery.Should().Contain("not confirmed");
        fixture.Audit.FailureOutcome = null;
        fixture.Service.Reload();
        fixture.Service.Get().Name.Should().Be("Nova");
    }

    [Fact]
    public async Task Concurrent_proposals_have_one_winner_and_reentrant_notifications_cannot_mutate()
    {
        using var fixture = new Fixture();
        var first = fixture.Propose("Nova");
        var second = fixture.Propose("Atlas");
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var one = fixture.Service.ApplyAsync(first, fixture.Policy, static () => true, async () =>
        {
            stopped.SetResult();
            await release.Task;
        }, TestContext.Current.CancellationToken);
        await stopped.Task;
        var two = fixture.ApplyAsync(second);
        Task<AssistantNameApplyResult>? reentrant = null;
        fixture.Service.Changed += (_, _) =>
        {
            var reload = fixture.Service.Reload;
            reload.Should().Throw<InvalidOperationException>();
            reentrant = fixture.ApplyAsync(second);
        };
        release.SetResult();
        (await one).Succeeded.Should().BeTrue();
        (await two).Reason.Should().Be("stale");
        var nested = () => reentrant!;
        await nested.Should().ThrowAsync<InvalidOperationException>();
        fixture.Preferences.Writes.Should().Be(1);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ILogger<AssistantNameConfigurationService> logger;
        private readonly ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        public Preferences Preferences { get; } = new();
        public Audit Audit { get; } = new();
        public Automatic Automatic { get; } = new();
        public CallCommunicationPolicy Policy { get; }
        public AssistantNameConfigurationService Service { get; }
        public bool HostEligible { get; set; } = true;
        public Action? BeforeRetire { get; set; }
        public int Retirements { get; private set; }
        public Fixture(ILogger<AssistantNameConfigurationService>? logger = null)
        {
            this.logger = logger ?? NullLogger<AssistantNameConfigurationService>.Instance;
            ActivitySource.AddActivityListener(listener);
            Policy = new(Automatic);
            Service = CreateService();
        }
        public AssistantNameConfigurationService CreateService() =>
            new(Preferences, new BuiltInCommandCatalog(), Audit, logger);
        public AssistantNameProposal Propose(string value) => Service.Propose(value,
            Service.Get().Revision, Policy.Current.Revision, RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand);
        public Task<AssistantNameApplyResult> ApplyAsync(AssistantNameProposal proposal) =>
            ApplyWithCancellationAsync(proposal, TestContext.Current.CancellationToken);
        public Task<AssistantNameApplyResult> ApplyWithCancellationAsync(AssistantNameProposal proposal, CancellationToken token) =>
            Service.ApplyAsync(proposal, Policy, () => HostEligible, () =>
            {
                Retirements++;
                BeforeRetire?.Invoke();
                return Task.CompletedTask;
            }, token);
        public void Dispose() { Service.Dispose(); Policy.Dispose(); listener.Dispose(); }
    }

    private sealed class Preferences : IAssistantNamePreferences
    {
        public string? Name { get; set; }
        public Exception? LoadFailure { get; set; }
        public Exception? SaveFailure { get; set; }
        public Action? AfterWrite { get; set; }
        public int Writes { get; private set; }
        public string? LoadName() => LoadFailure is { } failure ? throw failure : Name;
        public void SaveName(string name)
        {
            if (SaveFailure is not null) { throw SaveFailure; }
            Name = name;
            LoadFailure = null;
            Writes++;
            AfterWrite?.Invoke();
        }
    }

    private sealed class Audit : ISecurityAuditLog
    {
        public List<SecurityAuditEvent> Events { get; } = [];
        public Action? BeforeRequested { get; set; }
        public SecurityAuditOutcome? FailureOutcome { get; set; }
        public void Write(SecurityAuditEvent audit)
        {
            if (audit.Outcome == FailureOutcome) { throw new InvalidOperationException("audit unavailable"); }
            Events.Add(audit);
            if (audit.Outcome == SecurityAuditOutcome.Requested) { BeforeRequested?.Invoke(); }
        }
    }

    private sealed class Automatic : ICallStateService
    {
        public CallState CurrentState { get; private set; } = CallState.Clear;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
        public void Set(CallState state)
        {
            CurrentState = state;
            StateChanged?.Invoke(this, new(state));
        }
    }
}

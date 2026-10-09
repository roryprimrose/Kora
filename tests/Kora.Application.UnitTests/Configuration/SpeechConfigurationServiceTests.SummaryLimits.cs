using AwesomeAssertions;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Configuration;

public sealed partial class SpeechConfigurationServiceTests
{
    [Fact]
    public void Limits_share_revision_audit_and_notifications_but_never_change_provider_or_companion()
    {
        using var fixture = new Fixture();
        var initial = fixture.Service.Get();
        initial.SummaryLimits.Should().Be(SpokenSummaryLimits.Default);
        initial.AreSummaryLimitsSaved.Should().BeFalse();
        var notified = 0;
        fixture.Service.Changed += (_, _) => notified++;
        fixture.Apply(fixture.Propose(SpeechOption.SummaryWords, "40")).Succeeded.Should().BeTrue();
        fixture.Apply(fixture.Propose(SpeechOption.SummarySentences, "1")).Succeeded.Should().BeTrue();
        fixture.Service.Get().SummaryLimits.Should().Be(new SpokenSummaryLimits(1, 40));
        fixture.Service.Get().Selection.Should().Be(initial.Selection);
        fixture.Service.Get().IsSaved.Should().BeFalse();
        fixture.CreateService().Get().AreSummaryLimitsSaved.Should().BeTrue();
        fixture.CreateService().Get().SummaryLimits.Should().Be(new SpokenSummaryLimits(1, 40));
        var writes = fixture.Preferences.Writes;
        fixture.Apply(fixture.Propose(SpeechOption.SummaryWords, "40")).Succeeded.Should().BeTrue();
        fixture.Preferences.Writes.Should().Be(writes);
        fixture.Apply(fixture.Reset(SpeechOption.SummarySentences)).State.SummaryLimits.Should().Be(new SpokenSummaryLimits(3, 40));
        fixture.Apply(fixture.Reset(SpeechOption.SummaryWords)).State.SummaryLimits.Should().Be(SpokenSummaryLimits.Default);
        notified.Should().Be(4);
        fixture.Audit.Events.Chunk(2).Should().OnlyContain(pair =>
            pair[0].Outcome == SecurityAuditOutcome.Requested && pair[1].Outcome == SecurityAuditOutcome.Succeeded
            && pair[0].CorrelationId == pair[1].CorrelationId
            && pair[0].ActionId.StartsWith("configuration.speech-summary-", StringComparison.Ordinal));
    }

    [Fact]
    public void Limits_can_be_set_without_assets_or_valid_selection_and_do_not_repair_either()
    {
        using var fixture = new Fixture();
        fixture.Preferences.LoadFailure = new InvalidDataException("selection");
        fixture.Catalog.Providers = [];
        var before = fixture.Service.Get();
        fixture.Apply(fixture.Propose(SpeechOption.SummaryWords, "20")).Succeeded.Should().BeTrue();
        fixture.Service.Get().Selection.Should().BeNull();
        fixture.Service.Get().IsAvailable.Should().BeFalse();
        fixture.Service.Get().Recovery.Should().Be(before.Recovery);
        fixture.Preferences.Selection.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_or_unreadable_limits_preserve_selection_and_refuse_ordinary_speech(bool unreadable)
    {
        using var fixture = new Fixture();
        fixture.Preferences.LimitsLoadFailure = unreadable ? new UnauthorizedAccessException("access") : new InvalidDataException("limits");
        var current = fixture.Service.Get();
        current.IsAvailable.Should().BeTrue();
        current.SummaryLimits.Should().BeNull();
        var propose = () => fixture.Reset(SpeechOption.SummaryWords);
        propose.Should().Throw<InvalidOperationException>();
        var started = false;
        var recovery = await fixture.Service.StartSummarySpeechAsync("Short.", fixture.Policy, static () => true,
            () => { started = true; return Task.CompletedTask; }, TestContext.Current.CancellationToken);
        started.Should().BeFalse();
        recovery.Should().Contain("repair");
        fixture.Preferences.LimitsLoadFailure = null;
        fixture.Preferences.Limits = new(4, 80);
        fixture.Service.Reload();
        fixture.Service.Get().SummaryLimits.Should().BeNull();
        fixture.Preferences.Limits = new(2, 40);
        fixture.Service.Reload();
        fixture.Service.Get().SummaryLimits.Should().Be(new SpokenSummaryLimits(2, 40));
    }

    [Fact]
    public void Provider_mutation_and_runtime_hold_preserve_summary_state_and_hold()
    {
        using var fixture = new Fixture();
        fixture.Apply(fixture.Propose(SpeechOption.SummaryWords, "40"));
        fixture.Apply(fixture.Propose(SpeechOption.Provider, "kokoro"));
        fixture.Service.Get().SummaryLimits.Should().Be(new SpokenSummaryLimits(3, 40));
        fixture.Service.HoldUnavailable("runtime unavailable");
        fixture.Apply(fixture.Propose(SpeechOption.SummarySentences, "2")).Succeeded.Should().BeTrue();
        fixture.Service.Get().Recovery.Should().Be("runtime unavailable");
        fixture.Apply(fixture.Propose(SpeechOption.SummaryWords, "30")).State.Recovery.Should().Be("runtime unavailable");
    }

    [Theory]
    [InlineData(SpeechOption.SummaryWords, "81")]
    [InlineData(SpeechOption.SummarySentences, "4")]
    [InlineData(SpeechOption.SummaryWords, "forty")]
    public void Invalid_proposals_have_no_write_or_audit(SpeechOption option, string value)
    {
        using var fixture = new Fixture();
        var invalid = () => fixture.Propose(option, value);
        invalid.Should().Throw<ArgumentOutOfRangeException>();
        fixture.Preferences.Writes.Should().Be(0);
        fixture.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SpeechOption.SummarySentences, "1")]
    [InlineData(SpeechOption.SummaryWords, "40")]
    public void Original_channel_staleness_host_audit_and_cancel_races_gate_each_limit(SpeechOption option, string value)
    {
        using var fixture = new Fixture();
        var before = fixture.Service.Get();
        var proposal = fixture.Propose(option, value);
        fixture.Audit.BeforeRequested = () => fixture.Preferences.Limits = new(2, 50);
        fixture.Apply(proposal).Succeeded.Should().BeFalse();
        fixture.Preferences.Writes.Should().Be(0);
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
        fixture.Audit.BeforeRequested = null;
        fixture.Policy.SetManual(true, RequestOrigin.LocalUi, fixture.Policy.Current.Revision, static () => true);
        var voice = fixture.Service.Propose(option, value, fixture.Service.Get().Revision, fixture.Policy.Current.Revision,
            RequestOrigin.LocalUi, SecurityAuditInitiator.VoiceCommand);
        fixture.Apply(voice).Succeeded.Should().BeFalse();
        fixture.Audit.Events.Last().Initiator.Should().Be(SecurityAuditInitiator.VoiceCommand);
        var typed = fixture.Propose(option, value);
        fixture.Service.Apply(typed, fixture.Policy, static () => false, TestContext.Current.CancellationToken).Succeeded.Should().BeFalse();
        fixture.Preferences.Failure = new IOException("atomic failure");
        fixture.Apply(typed).Succeeded.Should().BeFalse();
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed);
        fixture.Preferences.Failure = null;
        using var cancellation = new CancellationTokenSource();
        fixture.Audit.BeforeRequested = cancellation.Cancel;
        var cancelled = () => fixture.Service.Apply(typed, fixture.Policy, static () => true, cancellation.Token);
        cancelled.Should().Throw<OperationCanceledException>();
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Cancelled);
        fixture.Audit.BeforeRequested = null;
        using var committed = new CancellationTokenSource();
        fixture.Preferences.AfterWrite = committed.Cancel;
        fixture.Service.Apply(typed, fixture.Policy, static () => true, committed.Token).Succeeded.Should().BeTrue();
        fixture.Service.Get().Revision.Should().BeGreaterThan(before.Revision);
        var stale = fixture.Apply(typed);
        stale.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Presentation_admits_both_exact_thresholds_and_checks_live_policy_and_cancellation()
    {
        using var fixture = new Fixture();
        var starts = 0;
        Task Start() { starts++; return Task.CompletedTask; }
        var text = string.Join(' ', Enumerable.Repeat("word", 78)) + ". Two. Three.";
        (await fixture.Service.StartSummarySpeechAsync(text, fixture.Policy, static () => true, Start,
            TestContext.Current.CancellationToken)).Should().BeNull();
        (await fixture.Service.StartSummarySpeechAsync(text + " extra", fixture.Policy, static () => true, Start,
            TestContext.Current.CancellationToken)).Should().Contain("81 words");
        (await fixture.Service.StartSummarySpeechAsync("One. Two. Three. Four.", fixture.Policy, static () => true, Start,
            TestContext.Current.CancellationToken)).Should().Contain("4 sentences");
        await fixture.Service.StartSummarySpeechAsync("Short.", fixture.Policy, static () => false, Start, TestContext.Current.CancellationToken);
        fixture.Policy.SetManual(true, RequestOrigin.LocalUi, 0, static () => true);
        await fixture.Service.StartSummarySpeechAsync("Short.", fixture.Policy, static () => true, Start, TestContext.Current.CancellationToken);
        starts.Should().Be(1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var early = () => fixture.Service.StartSummarySpeechAsync(text, fixture.Policy, static () => true, Start, cancellation.Token);
        await early.Should().ThrowAsync<OperationCanceledException>();
        fixture.Policy.SetManual(false, RequestOrigin.LocalUi, fixture.Policy.Current.Revision, static () => true);
        using var final = new CancellationTokenSource();
        var late = () => fixture.Service.StartSummarySpeechAsync("Short.", fixture.Policy, () => { final.Cancel(); return true; },
            Start, final.Token);
        await late.Should().ThrowAsync<OperationCanceledException>();
        fixture.Policy.Dispose();
        await fixture.Service.StartSummarySpeechAsync("Short.", fixture.Policy, static () => true, Start, TestContext.Current.CancellationToken);
        starts.Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_limit_proposals_foreign_owners_and_reentrant_notifications_do_not_gain_authority()
    {
        using var fixture = new Fixture();
        var proposal = fixture.Propose(SpeechOption.SummaryWords, "40");
        var foreign = () => fixture.CreateService().Apply(proposal, fixture.Policy, static () => true, TestContext.Current.CancellationToken);
        foreign.Should().Throw<ArgumentException>();
        fixture.Service.Changed += (_, _) =>
        {
            var reentrant = () => fixture.Apply(fixture.Reset(SpeechOption.SummarySentences));
            reentrant.Should().Throw<InvalidOperationException>();
        };
        var proposals = Enumerable.Range(0, 8).Select(_ => fixture.Propose(SpeechOption.SummaryWords, "40")).ToArray();
        var results = await Task.WhenAll(proposals.Select(item =>
            Task.Run(() => fixture.Apply(item), TestContext.Current.CancellationToken)));
        results.Count(result => result.Succeeded).Should().Be(1);
        fixture.Preferences.Writes.Should().Be(1);
        fixture.Audit.Events.Count(item => item.Outcome == SecurityAuditOutcome.Denied).Should().Be(7);
    }

    [Theory]
    [InlineData(SecurityAuditOutcome.Requested)]
    [InlineData(SecurityAuditOutcome.Succeeded)]
    public void Audit_failure_never_activates_or_notifies_an_unaudited_cap(SecurityAuditOutcome failed)
    {
        using var fixture = new Fixture();
        var proposal = fixture.Propose(SpeechOption.SummaryWords, "40");
        var before = fixture.Service.Get();
        var notifications = 0;
        fixture.Service.Changed += (_, _) => notifications++;
        fixture.Audit.FailureOutcome = failed;
        var apply = () => fixture.Apply(proposal);
        apply.Should().Throw<InvalidOperationException>();
        fixture.Service.Get().Should().Be(before);
        notifications.Should().Be(0);
        fixture.Preferences.Writes.Should().Be(failed == SecurityAuditOutcome.Requested ? 0 : 1);
    }

    [Theory]
    [InlineData(SpeechOption.SummarySentences, "1")]
    [InlineData(SpeechOption.SummaryWords, "40")]
    public void Live_call_revision_change_after_audit_admission_denies_each_cap(SpeechOption option, string value)
    {
        using var fixture = new Fixture();
        var proposal = fixture.Propose(option, value);
        fixture.Audit.BeforeRequested = () => fixture.Policy.SetManual(true, RequestOrigin.LocalUi, 0, static () => true);
        fixture.Apply(proposal).Succeeded.Should().BeFalse();
        fixture.Preferences.Writes.Should().Be(0);
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
    }
}

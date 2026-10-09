using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging.Abstractions;
using Neovolve.Logging.Xunit;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed partial class SpeechConfigurationServiceTests(ITestOutputHelper output)
    : LoggingTestsBase<SpeechConfigurationService>(output)
{
    [Fact]
    public void Atomic_choice_reset_restart_and_notifications_share_one_revision()
    {
        using var fixture = new Fixture();
        var initial = fixture.Service.Get();
        initial.Selection.Should().Be(SpeechSelection.Default);
        initial.IsSaved.Should().BeFalse();
        initial.IsAvailable.Should().BeTrue();
        initial.EffectiveVoice!.Id.Should().Be("default");
        var notifications = 0;
        fixture.Service.Changed += (_, _) => notifications++;
        var provider = fixture.Propose(SpeechOption.Provider, SpeechProviderIds.Kokoro);
        provider.Option.Should().Be(SpeechOption.Provider);
        provider.Selection.Should().Be(new SpeechSelection(SpeechProviderIds.Kokoro, null));
        provider.Revision.Should().Be(initial.Revision);
        provider.CallRevision.Should().Be(0);
        provider.Origin.Should().Be(RequestOrigin.LocalUi);
        provider.Initiator.Should().Be(SecurityAuditInitiator.TypedCommand);
        var first = fixture.Apply(provider);
        first.Succeeded.Should().BeTrue();
        first.State.EffectiveVoice!.ProviderId.Should().Be(SpeechProviderIds.Kokoro);
        fixture.Apply(fixture.Propose(SpeechOption.Voice, "second")).Succeeded.Should().BeTrue();
        fixture.Preferences.Selection.Should().Be(new SpeechSelection(SpeechProviderIds.Kokoro, "second"));
        fixture.CreateService().Get().Selection.Should().Be(fixture.Preferences.Selection);
        var writes = fixture.Preferences.Writes;
        fixture.Apply(fixture.Propose(SpeechOption.Voice, "second")).Succeeded.Should().BeTrue();
        fixture.Preferences.Writes.Should().Be(writes);
        fixture.Apply(fixture.Reset(SpeechOption.Voice)).State.Selection.Should().Be(new SpeechSelection(SpeechProviderIds.Kokoro, null));
        fixture.Apply(fixture.Reset(SpeechOption.Provider)).State.Selection.Should().Be(SpeechSelection.Default);
        notifications.Should().Be(4);
        fixture.Audit.Events.Should().HaveCount(10);
        fixture.Audit.Events.Chunk(2).Should().OnlyContain(pair =>
            pair[0].Outcome == SecurityAuditOutcome.Requested && pair[1].Outcome == SecurityAuditOutcome.Succeeded
            && pair[0].CorrelationId == pair[1].CorrelationId);
        fixture.Service.Reload();
        notifications.Should().Be(4);
    }

    [Fact]
    public async Task Concurrent_owned_proposals_have_only_one_winner()
    {
        using var fixture = new Fixture();
        var proposals = Enumerable.Range(0, 10).Select(_ => fixture.Propose(SpeechOption.Provider, SpeechProviderIds.Kokoro)).ToArray();
        var results = await Task.WhenAll(proposals.Select(proposal => Task.Run(() => fixture.Apply(proposal), TestContext.Current.CancellationToken)));
        results.Count(result => result.Succeeded).Should().Be(1);
        fixture.Preferences.Writes.Should().Be(1);
        fixture.Audit.Events.Count(item => item.Outcome == SecurityAuditOutcome.Denied).Should().Be(9);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_persistence_retains_old_state_and_has_terminal_audit(bool access)
    {
        using var fixture = new Fixture();
        var proposal = fixture.Propose(SpeechOption.Provider, SpeechProviderIds.Kokoro);
        var before = fixture.Service.Get();
        fixture.Preferences.Failure = access ? new UnauthorizedAccessException("denied") : new IOException("disk");
        var result = fixture.Apply(proposal);
        result.Succeeded.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.State.Should().Be(before);
        fixture.Preferences.Selection.Should().BeNull();
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed);
    }

    [Fact]
    public void Cancellation_before_admission_and_after_requested_and_after_commit_are_truthful()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var proposal = fixture.Propose(SpeechOption.Provider, SpeechProviderIds.Kokoro);
        cancellation.Cancel();
        var early = () => fixture.Service.Apply(proposal, fixture.Policy, static () => true, cancellation.Token);
        early.Should().Throw<OperationCanceledException>();
        fixture.Audit.Events.Should().BeEmpty();
        using var pending = new CancellationTokenSource();
        fixture.Audit.BeforeRequested = pending.Cancel;
        var admitted = () => fixture.Service.Apply(proposal, fixture.Policy, static () => true, pending.Token);
        admitted.Should().Throw<OperationCanceledException>();
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Cancelled);
        fixture.Preferences.Writes.Should().Be(0);
        fixture.Audit.BeforeRequested = null;
        using var committed = new CancellationTokenSource();
        fixture.Preferences.AfterWrite = committed.Cancel;
        fixture.Service.Apply(proposal, fixture.Policy, static () => true, committed.Token).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Missing_assets_and_malformed_saved_state_require_explicit_recovery_without_substitution()
    {
        using var fixture = new Fixture();
        fixture.Preferences.Selection = new(SpeechProviderIds.Kokoro, "removed");
        fixture.Service.Reload();
        fixture.Service.Get().IsAvailable.Should().BeFalse();
        fixture.Service.Get().Selection!.VoiceId.Should().Be("removed");
        fixture.Service.Get().Recovery.Should().Contain("Nothing is downloaded or substituted");
        var missing = () => fixture.Propose(SpeechOption.Voice, "missing");
        missing.Should().Throw<ArgumentOutOfRangeException>();
        fixture.Apply(fixture.Propose(SpeechOption.Voice, "second")).Succeeded.Should().BeTrue();
        fixture.Catalog.Providers = fixture.Catalog.Providers.Select(item => item with { IsInstalled = false }).ToArray();
        fixture.Service.Reload();
        fixture.Service.Get().Providers.Should().BeEmpty();
        fixture.Service.Get().Voices.Should().BeEmpty();
        fixture.Apply(fixture.Reset(SpeechOption.Provider)).Succeeded.Should().BeFalse();
        fixture.Catalog.Providers = Fixture.Providers;
        fixture.Preferences.LoadFailure = new InvalidDataException("unknown format");
        fixture.Service.Reload();
        fixture.Service.Get().Selection.Should().BeNull();
        fixture.Service.Get().Recovery.Should().Contain("invalid");
        var voice = () => fixture.Reset(SpeechOption.Voice);
        voice.Should().Throw<InvalidOperationException>();
        fixture.Apply(fixture.Reset(SpeechOption.Provider)).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(RequestOrigin.ActivatedVoice, SecurityAuditInitiator.TypedCommand, true, false)]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.VoiceCommand, true, false)]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser, true, true)]
    [InlineData(RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand, false, true)]
    [InlineData(RequestOrigin.HostSystem, SecurityAuditInitiator.LocalUser, false, false)]
    public void Original_channel_and_protected_call_gate_all_set_and_reset_routes(
        RequestOrigin origin, SecurityAuditInitiator initiator, bool active, bool allowed)
    {
        using var fixture = new Fixture();
        fixture.Policy.SetManual(active, RequestOrigin.LocalUi, 0, static () => true);
        foreach (var option in SpeechOptionRegistry.Options)
        {
            var proposal = fixture.Service.ProposeReset(option.Option, fixture.Service.Get().Revision,
                fixture.Policy.Current.Revision, origin, initiator);
            fixture.Apply(proposal).Succeeded.Should().Be(allowed);
        }
        if (!allowed) { fixture.Preferences.Writes.Should().Be(0); }
    }

    [Fact]
    public void Ownership_privacy_call_revision_catalogue_and_lineage_are_revalidated()
    {
        using var fixture = new Fixture();
        var proposal = fixture.Propose(SpeechOption.Provider, SpeechProviderIds.Kokoro);
        fixture.Service.Apply(proposal, fixture.Policy, static () => false, TestContext.Current.CancellationToken).Succeeded.Should().BeFalse();
        fixture.Policy.SetManual(true, RequestOrigin.LocalUi, 0, static () => true);
        fixture.Apply(proposal).Succeeded.Should().BeFalse();
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Policy);
        var relabelled = fixture.Propose(SpeechOption.Provider, SpeechProviderIds.Kokoro);
        relabelled.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        fixture.Apply(relabelled).Succeeded.Should().BeFalse();
        fixture.Audit.Events.Last().Initiator.Should().Be(SecurityAuditInitiator.VoiceCommand);
        fixture.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public void Foreign_invalid_proposals_and_notification_reentrancy_are_rejected()
    {
        using var fixture = new Fixture();
        var foreign = fixture.CreateService().ProposeReset(SpeechOption.Provider, 0, 0,
            RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser);
        var apply = () => fixture.Apply(foreign);
        apply.Should().Throw<ArgumentException>();
        var nullApply = () => fixture.Service.Apply(null!, fixture.Policy, static () => true, TestContext.Current.CancellationToken);
        nullApply.Should().Throw<ArgumentNullException>();
        var invalidInitiator = () => fixture.Service.Propose(SpeechOption.Provider, SpeechProviderIds.Windows,
            0, 0, RequestOrigin.LocalUi, SecurityAuditInitiator.System);
        invalidInitiator.Should().Throw<ArgumentOutOfRangeException>();
        var invalidValue = () => fixture.Propose(SpeechOption.Provider, "unknown");
        invalidValue.Should().Throw<ArgumentOutOfRangeException>();
        var proposal = fixture.Propose(SpeechOption.Provider, SpeechProviderIds.Kokoro);
        fixture.Service.Changed += (_, _) =>
        {
            var reload = fixture.Service.Reload;
            reload.Should().Throw<InvalidOperationException>();
            var reentrant = () => fixture.Apply(proposal);
            reentrant.Should().Throw<InvalidOperationException>();
        };
        fixture.Apply(proposal).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Enabled_structured_diagnostics_cover_invalid_state_denial_and_storage_failure()
    {
        using var fixture = new Fixture();
        var service = new SpeechConfigurationService(fixture.Preferences, fixture.Catalog, fixture.Audit, Logger);
        fixture.Preferences.LoadFailure = new InvalidDataException("unknown");
        service.Reload();
        var proposal = service.ProposeReset(SpeechOption.Provider, service.Get().Revision, 0,
            RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser);
        service.Apply(proposal, fixture.Policy, static () => false, TestContext.Current.CancellationToken).Succeeded.Should().BeFalse();
        fixture.Preferences.Failure = new IOException("disk");
        service.Apply(proposal, fixture.Policy, static () => true, TestContext.Current.CancellationToken).Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Catalogue_loss_after_request_invalidates_current_state_and_denies_stale_choice()
    {
        using var fixture = new Fixture();
        var proposal = fixture.Propose(SpeechOption.Voice, "windows-sapi / default");
        fixture.Audit.BeforeRequested = () => fixture.Catalog.Providers = [];
        var result = fixture.Apply(proposal);
        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("changed");
        result.State.IsAvailable.Should().BeFalse();
        fixture.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public void Cancellation_at_policy_commit_is_audited_but_no_write_occurs()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var proposal = fixture.Reset(SpeechOption.Provider);
        var apply = () => fixture.Service.Apply(proposal, fixture.Policy, () =>
        {
            cancellation.Cancel();
            return true;
        }, cancellation.Token);
        apply.Should().Throw<OperationCanceledException>();
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Cancelled);
        fixture.Preferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unreadable_preferences_and_failed_catalogue_are_unavailable_not_defaults(bool enabledLogging)
    {
        using var fixture = new Fixture();
        var service = new SpeechConfigurationService(fixture.Preferences, fixture.Catalog, fixture.Audit,
            enabledLogging ? Logger : NullLogger<SpeechConfigurationService>.Instance);
        fixture.Catalog.Failure = new IOException("initial discovery");
        service.Get().IsAvailable.Should().BeFalse();
        fixture.Catalog.Failure = null;
        foreach (var failure in new Exception[] { new IOException("read"), new UnauthorizedAccessException("read") })
        {
            fixture.Preferences.LoadFailure = failure;
            service.Reload();
            service.Get().Selection.Should().BeNull();
            service.Get().Recovery.Should().Contain("could not be read");
        }
        fixture.Preferences.LoadFailure = null;
        foreach (var failure in new Exception[] { new IOException("catalogue"), new UnauthorizedAccessException("catalogue"), new InvalidOperationException("catalogue") })
        {
            fixture.Catalog.Failure = failure;
            service.Reload();
            var before = service.Get();
            before.IsAvailable.Should().BeFalse();
            before.Recovery.Should().Contain("discovery failed");
            var proposal = service.ProposeReset(SpeechOption.Provider, before.Revision, 0,
                RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser);
            service.Apply(proposal, fixture.Policy, static () => true, TestContext.Current.CancellationToken)
                .Succeeded.Should().BeFalse();
            fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed);
        }
        fixture.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public void Run_only_failure_holds_do_not_default_or_lose_saved_choices_and_explicit_reset_recovers()
    {
        using var fixture = new Fixture();
        fixture.Service.HoldUnavailable("native speech unavailable");
        fixture.Service.Get().Selection.Should().BeNull();
        fixture.Service.Get().IsAvailable.Should().BeFalse();
        fixture.Service.Reload();
        fixture.Apply(fixture.Propose(SpeechOption.Provider, SpeechProviderIds.Kokoro));
        fixture.Service.HoldUnavailable("native speech failed");
        fixture.Service.Get().Selection!.ProviderId.Should().Be(SpeechProviderIds.Kokoro);
        fixture.Apply(fixture.Reset(SpeechOption.Voice)).Succeeded.Should().BeTrue();
        fixture.Service.Get().IsAvailable.Should().BeTrue();
    }

    [Theory]
    [InlineData(SecurityAuditOutcome.Requested)]
    [InlineData(SecurityAuditOutcome.Succeeded)]
    public void Audit_failures_never_produce_success_or_activate_unlogged_choices(SecurityAuditOutcome fail)
    {
        using var fixture = new Fixture();
        var before = fixture.Service.Get();
        var proposal = fixture.Propose(SpeechOption.Provider, SpeechProviderIds.Kokoro);
        fixture.Audit.FailureOutcome = fail;
        var apply = () => fixture.Apply(proposal);
        apply.Should().Throw<InvalidOperationException>();
        fixture.Service.Get().Should().Be(before);
        fixture.Preferences.Writes.Should().Be(fail == SecurityAuditOutcome.Requested ? 0 : 1);
    }

    [Fact]
    public void Explicit_qualified_voice_recovers_provider_without_default_and_rejects_ambiguous_ids()
    {
        using var fixture = new Fixture();
        fixture.Catalog.Providers = Fixture.Providers.Select(provider => provider with { DefaultVoiceId = null }).ToArray();
        fixture.Service.Get().IsAvailable.Should().BeFalse();
        var ambiguous = () => fixture.Propose(SpeechOption.Voice, "default");
        ambiguous.Should().Throw<ArgumentOutOfRangeException>();
        fixture.Apply(fixture.Propose(SpeechOption.Voice, "kokoro / default")).Succeeded.Should().BeTrue();
        fixture.Preferences.Selection.Should().Be(new SpeechSelection(SpeechProviderIds.Kokoro, "default"));
        fixture.Apply(fixture.Reset(SpeechOption.Voice)).Succeeded.Should().BeFalse();
        fixture.Preferences.Selection.Should().Be(new SpeechSelection(SpeechProviderIds.Kokoro, "default"));
        fixture.Apply(fixture.Propose(SpeechOption.Voice, "windows-sapi / default")).Succeeded.Should().BeTrue();
        fixture.Service.Get().EffectiveVoice!.ProviderId.Should().Be(SpeechProviderIds.Windows);
    }

    [Fact]
    public void Exact_installed_voice_at_limit_round_trips_and_over_limit_or_controls_do_not_save()
    {
        using var fixture = new Fixture();
        var id = new string('v', SpeechSelection.MaximumVoiceIdLength);
        fixture.Catalog.Voices = [new(id, "Bounded", "en-US", SpeechVoiceGender.Unknown)];
        fixture.Apply(fixture.Propose(SpeechOption.Voice, "windows-sapi / " + id)).Succeeded.Should().BeTrue();
        fixture.CreateService().Get().Selection!.VoiceId.Should().Be(id);
        foreach (var invalid in new[] { id + "v", "padded ", "bad\nvoice" })
        {
            fixture.Catalog.Voices = [new(invalid, "Invalid", "en-US", SpeechVoiceGender.Unknown)];
            fixture.Service.Reload();
            var propose = () => fixture.Propose(SpeechOption.Voice, "windows-sapi / " + invalid);
            propose.Should().Throw<ArgumentOutOfRangeException>();
        }
        fixture.Preferences.Writes.Should().Be(1);
    }

    [Fact]
    public void Asset_loss_during_live_host_admission_cannot_commit_or_substitute_a_choice()
    {
        using var fixture = new Fixture();
        var proposal = fixture.Propose(SpeechOption.Provider, SpeechProviderIds.Kokoro);
        var result = fixture.Service.Apply(proposal, fixture.Policy, () =>
        {
            fixture.Catalog.Providers = [];
            return true;
        }, TestContext.Current.CancellationToken);
        result.Succeeded.Should().BeFalse();
        result.State.IsAvailable.Should().BeFalse();
        fixture.Preferences.Writes.Should().Be(0);
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
    }

    private sealed class Fixture : IDisposable
    {
        public static IReadOnlyList<SpeechProvider> Providers { get; } =
        [
            new(SpeechProviderIds.Windows, "Windows", "Built-in", true, true, null, "default"),
            new(SpeechProviderIds.Kokoro, "Kokoro", "Installed", true, false, 1, "default"),
            new("unadmitted", "Other", "Unknown", true, false, null, "other"),
        ];
        private readonly ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        public Preferences Preferences { get; } = new();
        public Catalog Catalog { get; } = new();
        public Audit Audit { get; } = new();
        public CallCommunicationPolicy Policy { get; } = new(new Automatic());
        public SpeechConfigurationService Service { get; }
        public Fixture()
        {
            ActivitySource.AddActivityListener(listener);
            Service = CreateService();
        }
        public SpeechConfigurationService CreateService() => new(Preferences, Catalog, Audit, NullLogger<SpeechConfigurationService>.Instance);
        public SpeechProposal Propose(SpeechOption option, string? value) => Service.Propose(option, value,
            Service.Get().Revision, Policy.Current.Revision, RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand);
        public SpeechProposal Reset(SpeechOption option) => Service.ProposeReset(option, Service.Get().Revision,
            Policy.Current.Revision, RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser);
        public SpeechApplyResult Apply(SpeechProposal proposal) => Service.Apply(proposal, Policy, static () => true, TestContext.Current.CancellationToken);
        public void Dispose() { Policy.Dispose(); listener.Dispose(); }
    }

    private sealed class Catalog : ISpeechCatalog
    {
        public IReadOnlyList<SpeechProvider> Providers { get; set; } = Fixture.Providers;
        public Exception? Failure { get; set; }
        public IReadOnlyList<SpeechProvider> GetProviders() => Failure is { } exception ? throw exception : Providers;
        public IReadOnlyList<SpeechVoice> Voices { get; set; } =
        [
            new("default", "Windows", "en-US", SpeechVoiceGender.Female),
            new SpeechVoice("default", "Kokoro", "en-US", SpeechVoiceGender.Female) { ProviderId = SpeechProviderIds.Kokoro },
            new SpeechVoice("second", "Kokoro 2", "en-US", SpeechVoiceGender.Male) { ProviderId = SpeechProviderIds.Kokoro },
            new SpeechVoice("other", "Other", "en-US", SpeechVoiceGender.Neutral) { ProviderId = "unadmitted" },
        ];
        public IReadOnlyList<SpeechVoice> GetVoices() => Voices;
        public SpeechVoice? GetDefaultVoice() => throw new NotSupportedException();
    }

    private sealed class Preferences : ITextToSpeechPreferences
    {
        public SpokenSummaryLimits? Limits { get; set; }
        public Exception? LimitsLoadFailure { get; set; }
        public SpokenSummaryLimits? LoadSummaryLimits() => LimitsLoadFailure is { } exception ? throw exception : Limits;
        public void SaveSummaryLimits(SpokenSummaryLimits limits)
        {
            if (Failure is not null) { throw Failure; }
            Limits = limits;
            Writes++;
            AfterWrite?.Invoke();
        }
        public SpeechSelection? Selection { get; set; }
        public Exception? Failure { get; set; }
        public Exception? LoadFailure { get; set; }
        public Action? AfterWrite { get; set; }
        public int Writes { get; private set; }
        public SpeechSelection? LoadSelection() => LoadFailure is { } exception ? throw exception : Selection;
        public void SaveSelection(SpeechSelection selection)
        {
            if (Failure is not null) { throw Failure; }
            Selection = selection;
            LoadFailure = null;
            Writes++;
            AfterWrite?.Invoke();
        }
        public string? LoadProviderId() => throw new NotSupportedException();
        public string? LoadVoiceId() => throw new NotSupportedException();
        public void SaveProviderId(string providerId) => throw new NotSupportedException();
        public void SaveVoiceId(string voiceId) => throw new NotSupportedException();
    }

    private sealed class Audit : ISecurityAuditLog
    {
        public List<SecurityAuditEvent> Events { get; } = [];
        public Action? BeforeRequested { get; set; }
        public SecurityAuditOutcome? FailureOutcome { get; set; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            if (auditEvent.Outcome == FailureOutcome) { throw new InvalidOperationException("audit evidence unavailable"); }
            Events.Add(auditEvent);
            if (auditEvent.Outcome == SecurityAuditOutcome.Requested) { BeforeRequested?.Invoke(); }
        }
    }

    private sealed class Automatic : ICallStateService
    {
        public CallState CurrentState => CallState.Clear;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged { add { } remove { } }
    }
}

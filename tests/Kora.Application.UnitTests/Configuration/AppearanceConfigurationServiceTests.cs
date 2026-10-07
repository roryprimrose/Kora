using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Configuration;

public sealed class AppearanceConfigurationServiceTests
{
    [Fact]
    public void Every_option_round_trips_resets_only_itself_and_keeps_existing_formats()
    {
        var fixture = new Fixture();
        var notifications = 0;
        fixture.Service.Changed += (_, _) => notifications++;
        foreach (var descriptor in AppearanceOptionRegistry.Options)
        {
            var before = fixture.Service.Get(descriptor.Option);
            before.Value.Should().Be(descriptor.Default);
            before.IsSaved.Should().BeFalse();
            var changedValue = Alternative(descriptor);
            var proposal = fixture.Service.Propose(descriptor.Option, changedValue, before.Revision, SecurityAuditInitiator.LocalUser);
            proposal.Option.Should().Be(descriptor.Option);
            proposal.Value.Should().Be(changedValue);
            proposal.Revision.Should().Be(before.Revision);
            proposal.Initiator.Should().Be(SecurityAuditInitiator.LocalUser);
            var result = fixture.Service.Apply(proposal, TestContext.Current.CancellationToken);
            result.Succeeded.Should().BeTrue();
            result.Outcome.Should().Be(AppearanceApplyOutcome.Applied);
            result.State.Revision.Should().Be(before.Revision + 1);
            result.State.Value.Should().Be(changedValue);
            result.State.IsSaved.Should().BeTrue();
            fixture.CreateService().Get(descriptor.Option).Value.Should().Be(changedValue);
            var writes = fixture.Store.Writes;
            fixture.Service.Apply(fixture.Service.Propose(descriptor.Option, changedValue,
                result.State.Revision, SecurityAuditInitiator.LocalUser), TestContext.Current.CancellationToken).Outcome.Should().Be(AppearanceApplyOutcome.Unchanged);
            fixture.Store.Writes.Should().Be(writes);
            var reset = fixture.Service.Apply(fixture.Service.ProposeReset(descriptor.Option,
                result.State.Revision, SecurityAuditInitiator.TypedCommand), TestContext.Current.CancellationToken);
            reset.State.Value.Should().Be(descriptor.Default);
            foreach (var other in AppearanceOptionRegistry.Options)
                fixture.Service.Get(other.Option).Value.Should().Be(other.Default);
        }
        notifications.Should().Be(18);
        fixture.Audit.Events.Should().HaveCount(36);
        fixture.Store.Text["appearance-theme.txt"].Should().Be("System");
        fixture.Store.Text["presence-speech-scaling-enabled.txt"].Should().Be("True");
        fixture.Store.Text.Should().NotContainKey("response-window.txt").And.NotContainKey("presence-position.txt");
    }

    [Fact]
    public void Reset_and_update_detect_stale_global_revision_before_any_write()
    {
        var fixture = new Fixture();
        var before = fixture.Service.Get(AppearanceOption.Theme);
        var reset = fixture.Service.ProposeReset(AppearanceOption.Theme, before.Revision, SecurityAuditInitiator.LocalUser);
        var update = fixture.Service.Propose(AppearanceOption.Theme, new AppearanceValue.Theme(ApplicationThemeMode.Light),
            before.Revision, SecurityAuditInitiator.VoiceCommand);
        fixture.Service.Apply(fixture.Service.Propose(AppearanceOption.DotSize, new AppearanceValue.Number(120),
            before.Revision, SecurityAuditInitiator.LocalUser), TestContext.Current.CancellationToken).Succeeded.Should().BeTrue();
        fixture.Service.Apply(reset, TestContext.Current.CancellationToken).Outcome.Should().Be(AppearanceApplyOutcome.Stale);
        var stale = fixture.Service.Apply(update, TestContext.Current.CancellationToken);
        stale.Outcome.Should().Be(AppearanceApplyOutcome.Stale);
        stale.Succeeded.Should().BeFalse();
        stale.Error.Should().Contain("submit a new proposal");
        fixture.Store.Writes.Should().Be(1);
        fixture.Audit.Events.Should().HaveCount(2);
    }

    [Fact]
    public async Task Concurrent_proposals_have_exactly_one_committed_winner()
    {
        var fixture = new Fixture();
        var before = fixture.Service.Get(AppearanceOption.DotDensity);
        var proposals = Enumerable.Range(25, 20).Select(value =>
            fixture.Service.Propose(AppearanceOption.DotDensity, new AppearanceValue.Number(value),
                before.Revision, SecurityAuditInitiator.TypedCommand)).ToArray();
        var results = await Task.WhenAll(proposals.Select(proposal => fixture.Service.ApplyAsync(proposal, TestContext.Current.CancellationToken)));
        results.Count(item => item.Outcome == AppearanceApplyOutcome.Applied).Should().Be(1);
        results.Count(item => item.Outcome == AppearanceApplyOutcome.Stale).Should().Be(19);
        fixture.Store.Writes.Should().Be(1);
        fixture.Service.Get(AppearanceOption.DotDensity).Revision.Should().Be(before.Revision + 1);
        fixture.CreateService().Get(AppearanceOption.DotDensity).Value.Should().Be(fixture.Service.Get(AppearanceOption.DotDensity).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_save_retains_old_state_revision_and_disk_and_emits_failed_audit(bool accessDenied)
    {
        var fixture = new Fixture();
        var before = fixture.Service.Get(AppearanceOption.Theme);
        var notifications = 0;
        fixture.Service.Changed += (_, _) => notifications++;
        fixture.Store.Failure = accessDenied ? new UnauthorizedAccessException("fixture access denied") : new IOException("fixture disk full");
        var result = fixture.Service.Apply(fixture.Service.Propose(AppearanceOption.Theme,
            new AppearanceValue.Theme(ApplicationThemeMode.Dark), before.Revision, SecurityAuditInitiator.VoiceCommand), TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(AppearanceApplyOutcome.Failed);
        result.Succeeded.Should().BeFalse();
        result.State.Should().Be(before);
        result.Error.Should().Contain("fixture");
        notifications.Should().Be(0);
        fixture.Store.Text.Should().BeEmpty();
        fixture.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Failed);
        fixture.Audit.Events[1].ReasonCode.Should().Be(accessDenied ? "access-denied" : "io-error");
        fixture.Audit.Events.Should().OnlyContain(item => item.Initiator == SecurityAuditInitiator.VoiceCommand);
        fixture.Audit.Events[0].CorrelationId.Should().Be(fixture.Audit.Events[1].CorrelationId);
    }

    [Fact]
    public async Task Cancellation_before_admission_and_before_write_has_no_mutation_but_late_cancellation_reports_commit()
    {
        var fixture = new Fixture();
        var before = fixture.Service.Get(AppearanceOption.Theme);
        var proposal = fixture.Service.Propose(AppearanceOption.Theme, new AppearanceValue.Theme(ApplicationThemeMode.Dark),
            before.Revision, SecurityAuditInitiator.LocalUser);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var early = () => fixture.Service.Apply(proposal, cancelled.Token);
        early.Should().Throw<OperationCanceledException>();
        var scheduled = () => fixture.Service.ApplyAsync(proposal, cancelled.Token);
        await scheduled.Should().ThrowAsync<OperationCanceledException>();
        fixture.Audit.Events.Should().BeEmpty();
        using var atWrite = new CancellationTokenSource();
        fixture.Audit.BeforeRequested = atWrite.Cancel;
        var duringAdmission = () => fixture.Service.Apply(proposal, atWrite.Token);
        duringAdmission.Should().Throw<OperationCanceledException>();
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Cancelled);
        fixture.Service.Get(AppearanceOption.Theme).Should().Be(before);
        fixture.Store.Writes.Should().Be(0);
        fixture.Audit.BeforeRequested = null;
        using var late = new CancellationTokenSource();
        fixture.Store.BeforeWrite = late.Cancel;
        fixture.Service.Apply(proposal, late.Token).Outcome.Should().Be(AppearanceApplyOutcome.Applied);
        fixture.Service.Get(AppearanceOption.Theme).Value.Should().Be(proposal.Value);
    }

    [Fact]
    public void Foreign_proposals_invented_origins_and_tampered_values_fail_closed()
    {
        var fixture = new Fixture();
        var before = fixture.Service.Get(AppearanceOption.Theme);
        var proposal = fixture.Service.ProposeReset(AppearanceOption.Theme, before.Revision, SecurityAuditInitiator.LocalUser);
        var foreign = () => fixture.CreateService().Apply(proposal);
        foreign.Should().Throw<ArgumentException>();
        foreach (var initiator in new[] { SecurityAuditInitiator.System, SecurityAuditInitiator.ModelSuggestion, (SecurityAuditInitiator)999 })
        {
            var origin = () => fixture.Service.ProposeReset(AppearanceOption.Theme, before.Revision, initiator);
            origin.Should().Throw<ArgumentOutOfRangeException>();
        }
        var tampered = new AppearanceProposal(proposal.Owner, AppearanceOption.Theme, new AppearanceValue.Number(5),
            proposal.Revision, proposal.Initiator);
        var apply = () => fixture.Service.Apply(tampered);
        apply.Should().Throw<ArgumentOutOfRangeException>();
        var missing = () => fixture.Service.Apply(null!);
        missing.Should().Throw<ArgumentNullException>();
        fixture.Store.Writes.Should().Be(0);
    }

    [Fact]
    public void Cold_apply_loads_saved_state_before_rechecking_revision()
    {
        var fixture = new Fixture();
        fixture.Store.Text["appearance-theme.txt"] = "Dark";
        var result = fixture.Service.Apply(fixture.Service.ProposeReset(AppearanceOption.Theme, 0,
            SecurityAuditInitiator.LocalUser), TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(AppearanceApplyOutcome.Stale);
        fixture.Store.Text["appearance-theme.txt"].Should().Be("Dark");
        fixture.Store.Writes.Should().Be(0);
    }

    [Fact]
    public void Reload_invalidates_proposals_notifies_once_and_rejects_corruption_without_partial_state()
    {
        var fixture = new Fixture();
        var before = fixture.Service.Get(AppearanceOption.Theme);
        var proposal = fixture.Service.ProposeReset(AppearanceOption.Theme, before.Revision, SecurityAuditInitiator.LocalUser);
        fixture.Store.Text["appearance-theme.txt"] = "Light";
        var notifications = 0;
        fixture.Service.Changed += (_, _) => notifications++;
        fixture.Service.Reload();
        fixture.Service.Reload();
        notifications.Should().Be(1);
        fixture.Service.Apply(proposal, TestContext.Current.CancellationToken).Outcome.Should().Be(AppearanceApplyOutcome.Stale);
        var current = fixture.Service.Get(AppearanceOption.Theme);
        fixture.Store.Text["appearance-theme.txt"] = "Dark";
        fixture.Store.Text["presence-dot-density-percent.txt"] = "999";
        var corrupt = fixture.Service.Reload;
        corrupt.Should().Throw<InvalidDataException>();
        fixture.Service.Get(AppearanceOption.Theme).Should().Be(current);
        notifications.Should().Be(1);
    }

    [Fact]
    public void Persisted_default_and_absent_default_have_distinct_provenance_and_revisions()
    {
        var fixture = new Fixture();
        var before = fixture.Service.Get(AppearanceOption.Theme);
        before.IsSaved.Should().BeFalse();
        fixture.Store.Text["appearance-theme.txt"] = "System";
        fixture.Service.Reload();
        var after = fixture.Service.Get(AppearanceOption.Theme);
        after.Value.Should().Be(before.Value);
        after.IsSaved.Should().BeTrue();
        after.Revision.Should().Be(before.Revision + 1);
        fixture.Service.Apply(fixture.Service.ProposeReset(AppearanceOption.Theme, after.Revision,
            SecurityAuditInitiator.LocalUser), TestContext.Current.CancellationToken).Outcome.Should().Be(AppearanceApplyOutcome.Unchanged);
        fixture.Store.Writes.Should().Be(0);
    }

    [Fact]
    public void Notifications_support_inspection_but_reentrant_mutation_is_rejected()
    {
        var fixture = new Fixture();
        var before = fixture.Service.Get(AppearanceOption.Theme);
        fixture.Service.Changed += (_, _) =>
        {
            var current = fixture.Service.Get(AppearanceOption.Theme);
            current.Value.Should().Be(new AppearanceValue.Theme(ApplicationThemeMode.Dark));
            var reload = fixture.Service.Reload;
            reload.Should().Throw<InvalidOperationException>();
        };
        fixture.Service.Apply(fixture.Service.Propose(AppearanceOption.Theme,
            new AppearanceValue.Theme(ApplicationThemeMode.Dark), before.Revision, SecurityAuditInitiator.LocalUser), TestContext.Current.CancellationToken);
        fixture.Service.Reload();
    }

    [Fact]
    public async Task Persistence_activity_preserves_host_parentage_and_truthful_terminal_status()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        var fixture = new Fixture();
        var before = fixture.Service.Get(AppearanceOption.Theme);
        using var request = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request);
        Activity? stored = null;
        fixture.Store.BeforeWrite = () => stored = Activity.Current;
        var result = await fixture.Service.ApplyAsync(fixture.Service.Propose(AppearanceOption.Theme,
            new AppearanceValue.Theme(ApplicationThemeMode.Dark), before.Revision, SecurityAuditInitiator.VoiceCommand),
            TestContext.Current.CancellationToken);
        result.Succeeded.Should().BeTrue();
        stored.Should().NotBeNull();
        stored!.ParentSpanId.Should().Be(request.Activity!.SpanId);
        stored.TraceId.Should().Be(request.Activity.TraceId);
        stored.Status.Should().Be(ActivityStatusCode.Ok);
        Activity.Current.Should().BeSameAs(request.Activity);
        request.Complete(HostOperationOutcome.Completed);
    }

    private static AppearanceValue Alternative(AppearanceOptionDescriptor descriptor) => descriptor.Default switch
    {
        AppearanceValue.Theme => new AppearanceValue.Theme(ApplicationThemeMode.Dark),
        AppearanceValue.Toggle boolean => new AppearanceValue.Toggle(!boolean.Value),
        AppearanceValue.Number => new AppearanceValue.Number(descriptor.Maximum!.Value),
        _ => throw new InvalidOperationException(),
    };

    private sealed class Fixture
    {
        public Store Store { get; } = new();
        public Audit Audit { get; } = new();
        public RecordingLogger Logger { get; } = new();
        public AppearanceConfigurationService Service { get; }
        public Fixture() => Service = CreateService();
        public AppearanceConfigurationService CreateService() =>
            new(new LocalAppearancePreferences(Store, NullLogger<LocalAppearancePreferences>.Instance),
                Audit, Logger);
    }

    private sealed class Store : IPreferenceStore
    {
        public Dictionary<string, string> Text { get; } = new(StringComparer.Ordinal);
        public int Writes { get; private set; }
        public Exception? Failure { get; set; }
        public Action? BeforeWrite { get; set; }
        public string? ReadText(string fileName) => Text.GetValueOrDefault(fileName);
        public string[]? ReadLines(string fileName) => null;
        public void WriteText(string fileName, string contents)
        {
            BeforeWrite?.Invoke();
            if (Failure is not null) throw Failure;
            Text[fileName] = contents;
            Writes++;
        }
        public void WriteLines(string fileName, IEnumerable<string> contents) => throw new NotSupportedException();
        public void Delete(string fileName) => throw new NotSupportedException();
    }

    private sealed class Audit : ISecurityAuditLog
    {
        public List<SecurityAuditEvent> Events { get; } = [];
        public Action? BeforeRequested { get; set; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            Events.Add(auditEvent);
            if (auditEvent.Outcome == SecurityAuditOutcome.Requested) BeforeRequested?.Invoke();
        }
    }

    private sealed class RecordingLogger : ILogger<AppearanceConfigurationService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            state.Should().BeAssignableTo<IEnumerable<KeyValuePair<string, object?>>>();
            var properties = state as IEnumerable<KeyValuePair<string, object?>>
                ?? throw new InvalidOperationException("Structured logging is required.");
            properties.Should().Contain(item => string.Equals(item.Key, "OptionId", StringComparison.Ordinal));
        }
    }
}

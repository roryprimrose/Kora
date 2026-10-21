using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class DiagnosticImpactPreviewServiceTests
{
    [Theory]
    [InlineData("idle")]
    [InlineData("edited")]
    [InlineData("expired")]
    [InlineData("source-retired")]
    [InlineData("disposed")]
    public async Task Source_changes_without_an_active_preview_do_not_queue_presentation_work(string phase)
    {
        await using var fixture = new Fixture();
        if (!string.Equals(phase, "idle", StringComparison.Ordinal)) { await fixture.Preview(); }
        if (string.Equals(phase, "edited", StringComparison.Ordinal)) { fixture.Service.Retire("Proposal edited."); }
        if (string.Equals(phase, "expired", StringComparison.Ordinal)) { fixture.Time.Advance(DiagnosticRetentionImpact.PreviewLifetime); }
        if (string.Equals(phase, "source-retired", StringComparison.Ordinal)) { fixture.Reader.Signal(); }
        if (string.Equals(phase, "disposed", StringComparison.Ordinal)) { fixture.Service.Dispose(); }
        var state = fixture.Service.Get();
        var notices = 0;
        fixture.Service.Changed += (_, _) => notices++;

        fixture.Reader.Signal();
        fixture.Reader.Signal();
        fixture.Configuration.Observe();

        notices.Should().Be(0);
        fixture.Service.Get().Should().BeSameAs(state);
        fixture.Reader.Requests.Should().HaveCount(string.Equals(phase, "idle", StringComparison.Ordinal) ? 0 : 1);
    }

    [Fact]
    public async Task Source_retirement_notifies_once_and_its_presentation_evidence_cannot_feed_back()
    {
        await using var fixture = new Fixture();
        await fixture.Preview();
        var presentation = new Queue<Action>();
        var notices = 0;
        fixture.Service.Changed += (_, _) =>
        {
            notices++;
            presentation.Enqueue(fixture.Reader.Signal);
        };

        fixture.Reader.Signal();
        for (var count = 0; count < 8 && presentation.TryDequeue(out var writePresentationEvidence); count++)
        {
            writePresentationEvidence();
        }

        fixture.Service.Get().Result.Should().BeNull();
        fixture.Service.Get().Status.Should().Contain("retired");
        notices.Should().Be(1);
        presentation.Should().BeEmpty();
        fixture.Reader.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Explicit_observation_binds_exact_value_fixed_cutoff_sources_and_never_creates_control_work()
    {
        await using var fixture = new Fixture();
        fixture.Service.Get().Result.Should().BeNull();
        fixture.Reader.Requests.Should().BeEmpty();
        await fixture.Preview();
        var result = fixture.Service.Get();
        result.ProposedDays.Should().Be(1);
        result.CutoffUtc.Should().Be(fixture.Time.Now);
        result.Result!.Logs.Aggregate!.NewlyDueCount.Should().Be(1);
        result.Result.Spans.Aggregate!.NewlyDueCount.Should().Be(1);
        result.Result.Links.Aggregate!.NewlyDueCount.Should().Be(0);
        fixture.Reader.Requests.Should().ContainSingle().Which.Origin.Should().Be(RequestOrigin.LocalUi);
        fixture.Preferences.Writes.Should().Be(0);
        fixture.Audit.Writes.Should().Be(0);
        fixture.Store.Tasks.Should().BeEmpty();
        fixture.Policy.Effective.Should().Be(DiagnosticRetentionDays.Default);
        fixture.Time.Advance(DiagnosticRetentionImpact.PreviewLifetime);
        fixture.Service.Get().Result.Should().BeNull();
        fixture.Service.Get().Status.Should().Contain("expired");
    }

    [Theory]
    [InlineData("parse")]
    [InlineData("not-shorter")]
    [InlineData("minimum")]
    [InlineData("host")]
    [InlineData("config")]
    [InlineData("policy")]
    [InlineData("saved")]
    [InlineData("readback")]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("corrupt")]
    public async Task Invalid_or_unknown_admission_never_starts_a_scan(string reason)
    {
        await using var fixture = new Fixture();
        var text = string.Equals(reason, "parse", StringComparison.Ordinal) ? "01"
            : string.Equals(reason, "not-shorter", StringComparison.Ordinal) ? "30" : "1";
        if (string.Equals(reason, "minimum", StringComparison.Ordinal)) { fixture.Preferences.Value = new(1); fixture.Configuration.Observe(); }
        if (string.Equals(reason, "host", StringComparison.Ordinal)) { fixture.Eligible = false; }
        if (string.Equals(reason, "config", StringComparison.Ordinal)) { fixture.Configuration.HoldUnavailable(); }
        if (string.Equals(reason, "policy", StringComparison.Ordinal)) { fixture.Policy.HoldUnavailable(); }
        if (string.Equals(reason, "saved", StringComparison.Ordinal)) { fixture.Preferences.Value = new(365); }
        if (string.Equals(reason, "readback", StringComparison.Ordinal)) { fixture.Preferences.ReadBackValue = new(365); }
        if (string.Equals(reason, "io", StringComparison.Ordinal)) { fixture.Preferences.Failure = new IOException(); }
        if (string.Equals(reason, "access", StringComparison.Ordinal)) { fixture.Preferences.Failure = new UnauthorizedAccessException(); }
        if (string.Equals(reason, "corrupt", StringComparison.Ordinal)) { fixture.Preferences.Failure = new InvalidDataException(); }
        await ((Func<Task>)(() => fixture.Service.PreviewAsync(text, () => fixture.Eligible)))
            .Should().ThrowAsync<Exception>();
        fixture.Service.Get().Result.Should().BeNull();
        fixture.Reader.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("saved")]
    [InlineData("readback")]
    [InlineData("policy-aba")]
    [InlineData("config-aba")]
    [InlineData("observation")]
    [InlineData("host")]
    [InlineData("source")]
    [InlineData("source-event")]
    [InlineData("io")]
    [InlineData("corrupt")]
    [InlineData("access")]
    public async Task Published_results_retire_and_never_revive_after_ABA_or_recovery(string reason)
    {
        await using var fixture = new Fixture();
        await fixture.Preview();
        if (string.Equals(reason, "saved", StringComparison.Ordinal)) { fixture.Preferences.Value = new(30); } // Same effective, different saved provenance.
        if (string.Equals(reason, "readback", StringComparison.Ordinal)) { fixture.Preferences.ReadBackValue = new(365); }
        if (string.Equals(reason, "policy-aba", StringComparison.Ordinal)) { fixture.Policy.HoldUnavailable(); fixture.Policy.Activate(new(30)); }
        if (string.Equals(reason, "config-aba", StringComparison.Ordinal))
        {
            fixture.Preferences.Value = new(365);
            fixture.Configuration.Observe();
            fixture.Preferences.Value = null;
            fixture.Configuration.Observe();
        }
        if (string.Equals(reason, "observation", StringComparison.Ordinal)) { fixture.Configuration.Observe(); }
        if (string.Equals(reason, "host", StringComparison.Ordinal)) { fixture.Eligible = false; }
        if (string.Equals(reason, "source", StringComparison.Ordinal)) { fixture.Reader.Current = false; }
        if (string.Equals(reason, "source-event", StringComparison.Ordinal)) { fixture.Reader.Signal(); }
        if (string.Equals(reason, "io", StringComparison.Ordinal)) { fixture.Preferences.Failure = new IOException(); }
        if (string.Equals(reason, "corrupt", StringComparison.Ordinal)) { fixture.Preferences.Failure = new InvalidDataException(); }
        if (string.Equals(reason, "access", StringComparison.Ordinal)) { fixture.Preferences.Failure = new UnauthorizedAccessException(); }
        fixture.Service.Get().Result.Should().BeNull();
        fixture.Eligible = true;
        fixture.Reader.Current = true;
        fixture.Preferences.Value = null;
        fixture.Preferences.ReadBackValue = null;
        fixture.Preferences.Failure = null;
        fixture.Service.Get().Result.Should().BeNull();
        fixture.Reader.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(DiagnosticImpactStatus.Complete)]
    [InlineData(DiagnosticImpactStatus.Incomplete)]
    [InlineData(DiagnosticImpactStatus.Unavailable)]
    public async Task Per_source_qualification_does_not_hide_unavailable_or_incomplete_behind_a_zero(DiagnosticImpactStatus status)
    {
        await using var fixture = new Fixture();
        fixture.Reader.LogStatus = status;
        await fixture.Preview();
        fixture.Service.Get().Result!.Logs.Status.Should().Be(status);
        if (status != DiagnosticImpactStatus.Complete)
        {
            fixture.Service.Get().Result!.Logs.Aggregate.Should().BeNull();
            fixture.Service.Get().Status.Should().Contain("Incomplete/unavailable");
        }
        fixture.Reader.AllUnavailable = true;
        await fixture.Preview();
        fixture.Service.Get().Result.Should().Be(DiagnosticImpactSnapshot.Unavailable);
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("dispose")]
    [InlineData("cancel")]
    [InlineData("timeout")]
    [InlineData("host")]
    [InlineData("source")]
    [InlineData("configuration")]
    [InlineData("failure")]
    [InlineData("malformed")]
    public async Task Inflight_late_receipts_never_publish_or_retire_a_newer_preview(string reason)
    {
        await using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var pending = new TaskCompletionSource<DiagnosticImpactSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Reader.Pending = pending.Task;
        var task = fixture.Service.PreviewAsync("1", () => fixture.Eligible, cancellation.Token);
        fixture.Service.Get().Scanning.Should().BeTrue();
        if (string.Equals(reason, "edit", StringComparison.Ordinal)) { fixture.Service.Retire("Proposal edited."); }
        if (string.Equals(reason, "dispose", StringComparison.Ordinal)) { fixture.Service.Dispose(); }
        if (string.Equals(reason, "cancel", StringComparison.Ordinal)) { await cancellation.CancelAsync(); }
        if (string.Equals(reason, "timeout", StringComparison.Ordinal)) { fixture.Time.Advance(DiagnosticRetentionImpact.ScanTimeout); }
        if (string.Equals(reason, "host", StringComparison.Ordinal)) { fixture.Eligible = false; }
        if (string.Equals(reason, "source", StringComparison.Ordinal)) { fixture.Reader.Current = false; }
        if (string.Equals(reason, "configuration", StringComparison.Ordinal)) { fixture.Configuration.Observe(); }
        if (string.Equals(reason, "failure", StringComparison.Ordinal)) { pending.SetException(new IOException("private sentinel")); }
        else
        {
            var result = fixture.Reader.Result(new(1), fixture.Time.Now);
            pending.SetResult(string.Equals(reason, "malformed", StringComparison.Ordinal)
                ? result with { Logs = result.Logs with { Aggregate = null } } : result);
        }
        await ((Func<Task>)(() => task)).Should().ThrowAsync<Exception>();
        fixture.Service.Get().Result.Should().BeNull();
        fixture.Service.Get().Status.Should().NotContain("private sentinel");
        if (string.Equals(reason, "dispose", StringComparison.Ordinal))
        {
            await ((Func<Task>)(() => fixture.Preview())).Should().ThrowAsync<ObjectDisposedException>();
            fixture.Reader.Signal();
            return;
        }
        fixture.Reader.Pending = null;
        fixture.Reader.Current = true;
        fixture.Eligible = true;
        await fixture.Preview();
        var oldTimer = fixture.Time.Timers.Last();
        fixture.Service.Retire("Clear.");
        await fixture.Preview();
        oldTimer.Fire();
        fixture.Service.Get().Result.Should().NotBeNull();
    }

    [Fact]
    public async Task Notification_reentrancy_and_source_currentness_callbacks_cannot_republish_retired_generations()
    {
        await using var fixture = new Fixture();
        Task? reentry = null;
        EventHandler onChanged = (_, _) =>
        {
            if (fixture.Service.Get().Scanning)
            {
                reentry = fixture.Preview();
                fixture.Service.Retire("Cleared during callback.");
            }
        };
        fixture.Service.Changed += onChanged;
        await ((Func<Task>)(() => fixture.Preview())).Should().ThrowAsync<OperationCanceledException>();
        await ((Func<Task>)(() => reentry!)).Should().ThrowAsync<InvalidOperationException>();
        fixture.Reader.Requests.Should().BeEmpty();
        fixture.Service.Changed -= onChanged;
        fixture.Reader.OnCurrent = () => fixture.Service.Retire("Source changed during currentness callback.");
        await ((Func<Task>)(() => fixture.Preview())).Should().ThrowAsync<InvalidOperationException>();
        fixture.Service.Get().Result.Should().BeNull();
        fixture.Reader.OnCurrent = null;
        await fixture.Preview();
        fixture.Reader.OnCurrent = () => fixture.Service.Retire("Private closure during getter.");
        fixture.Service.Get().Status.Should().Contain("Private closure");
    }

    [Fact]
    public async Task Content_free_root_observation_ignores_hostile_parent_and_uses_monotonic_lifetime()
    {
        var completed = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "Kora.Application", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => completed.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);
        await using var fixture = new Fixture();
        using var hostile = new Activity("hostile").SetIdFormat(ActivityIdFormat.W3C).Start();
        await fixture.Preview();
        var observation = completed.Single(item =>
            string.Equals(item.OperationName, "diagnostics.retention.preview", StringComparison.Ordinal));
        observation.ParentId.Should().BeNull();
        observation.Status.Should().Be(ActivityStatusCode.Ok);
        observation.TagObjects.Select(tag => tag.Key).Should().Equal("kora.request.id");
        observation.Source.Version.Should().NotBeNullOrWhiteSpace();
        observation.Baggage.Should().BeEmpty();
        Activity.Current.Should().BeSameAs(hostile);
        fixture.Time.Now = fixture.Time.Now.AddYears(-10);
        fixture.Time.ElapseWithoutTimers(DiagnosticRetentionImpact.PreviewLifetime);
        fixture.Service.Get().Result.Should().BeNull();
        fixture.Reader.Current = false;
        await ((Func<Task>)(() => fixture.Preview())).Should().ThrowAsync<InvalidOperationException>();
        completed.Last().Status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task Synchronous_lifetime_callback_disposes_its_timer_and_never_republishes_an_expired_result()
    {
        await using var fixture = new Fixture();
        fixture.Time.ExpireOnCreate = true;
        await ((Func<Task>)(() => fixture.Preview())).Should().ThrowAsync<InvalidOperationException>();
        fixture.Service.Get().Result.Should().BeNull();
        fixture.Time.Timers.Last().Disposed.Should().BeTrue();
        fixture.Service.Get().Status.Should().Contain("expired");
    }

    [Fact]
    public void Unknown_saved_provenance_has_no_default_fallback()
    {
        var observation = new DiagnosticRetentionObservation(new(new(30), new(30), "unknown", 1, null), 1);
        var read = () => observation.Saved;
        read.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task Exact_saved_maximum_observation_supports_diagnostic_not_audit_domain_and_never_uses_default_provenance()
    {
        await using var fixture = new Fixture();
        fixture.Preferences.Value = new(365);
        fixture.Configuration.Observe();
        var observation = fixture.Configuration.GetObservation();
        observation.Saved.Should().Be(new DiagnosticRetentionDays(365));
        fixture.Configuration.IsCurrentObservation(observation).Should().BeTrue();
        await fixture.Service.PreviewAsync("364", () => fixture.Eligible, TestContext.Current.CancellationToken);
        fixture.Service.Get().Result!.Logs.Aggregate!.NewlyDueCount.Should().Be(0);
        await ((Func<Task>)(() => fixture.Service.PreviewAsync("365", () => fixture.Eligible)))
            .Should().ThrowAsync<InvalidOperationException>();
        fixture.Preferences.Value = null;
        fixture.Configuration.Observe();
        fixture.Configuration.IsCurrentObservation(observation).Should().BeFalse();
        fixture.Configuration.GetObservation().Saved.Should().BeNull();
    }

    internal sealed class Reader : IDiagnosticRetentionImpactReader
    {
        internal List<HostRequest> Requests { get; } = [];
        internal bool Current { get; set; } = true;
        internal bool AllUnavailable { get; set; }
        internal DiagnosticImpactStatus LogStatus { get; set; } = DiagnosticImpactStatus.Complete;
        internal Task<DiagnosticImpactSnapshot>? Pending { get; set; }
        internal Action? OnCurrent { get; set; }
        public event EventHandler? Changed;
        internal void Signal() => Changed?.Invoke(this, EventArgs.Empty);
        public bool IsCurrent(DiagnosticImpactSnapshot snapshot) { OnCurrent?.Invoke(); return Current; }
        public ValueTask<DiagnosticImpactSnapshot> ReadAsync(DiagnosticRetentionDays proposed, DateTimeOffset cutoff,
            HostRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Pending is { } pending ? new(pending) : ValueTask.FromResult(Result(proposed, cutoff));
        }
        internal DiagnosticImpactSnapshot Result(DiagnosticRetentionDays proposed, DateTimeOffset cutoff)
        {
            if (AllUnavailable) { return DiagnosticImpactSnapshot.Unavailable; }
            var impact = new DiagnosticRetentionImpact(proposed, cutoff);
            impact.Add(cutoff.AddDays(-20), cutoff.AddDays(10));
            var zero = new DiagnosticRetentionImpact(proposed, cutoff);
            return new(LogStatus == DiagnosticImpactStatus.Complete ? impact.Finish(DiagnosticImpactSource.OrdinaryLogs)
                : new(DiagnosticImpactSource.OrdinaryLogs, LogStatus, null), impact.Finish(DiagnosticImpactSource.Spans),
                zero.Finish(DiagnosticImpactSource.OwnedLinks), "private-stamp");
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal Fixture()
        {
            Admission = new(Store, Store, new(Store));
            Configuration = new(Preferences, Policy, Admission, Audit);
            Configuration.Observe();
            Service = new(Reader, Preferences, Configuration, Time);
        }
        internal AudioControlTestStore Store { get; } = new();
        internal Preferences Preferences { get; } = new();
        internal Audit Audit { get; } = new();
        internal DiagnosticRetentionPolicy Policy { get; } = new();
        internal DiagnosticRetentionAdmission Admission { get; }
        internal DiagnosticRetentionConfigurationService Configuration { get; }
        internal Reader Reader { get; } = new();
        internal Clock Time { get; } = new();
        internal DiagnosticImpactPreviewService Service { get; }
        internal bool Eligible { get; set; } = true;
        internal Task Preview() => Service.PreviewAsync("1", () => Eligible, TestContext.Current.CancellationToken);
        public async ValueTask DisposeAsync() { Service.Dispose(); await Admission.DisposeAsync(); }
    }

    private sealed class Preferences : IDiagnosticRetentionPreferences
    {
        internal DiagnosticRetentionDays? Value { get; set; }
        internal DiagnosticRetentionDays? ReadBackValue { get; set; }
        internal Exception? Failure { get; set; }
        internal int Writes { get; private set; }
        public DiagnosticRetentionDays? Load() { if (Failure is { } failure) { throw failure; } return Value; }
        public DiagnosticRetentionDays? ReadBack() => ReadBackValue ?? Load();
        public void BeginWrite() => Writes++;
        public void ConfirmWrite() => Writes++;
        public void Save(DiagnosticRetentionDays days) => Writes++;
        public void Reset() => Writes++;
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal int Writes { get; private set; }
        public void Write(SecurityAuditEvent auditEvent) => Writes++;
    }

    private sealed class Clock : TimeProvider
    {
        private long timestamp;
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
        internal List<ManualTimer> Timers { get; } = [];
        internal bool ExpireOnCreate { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(() => callback(state), timestamp + dueTime.Ticks);
            Timers.Add(timer);
            if (ExpireOnCreate && dueTime == DiagnosticRetentionImpact.PreviewLifetime) { timer.Fire(); }
            return timer;
        }
        internal void ElapseWithoutTimers(TimeSpan duration) => timestamp += duration.Ticks;
        internal void Advance(TimeSpan duration)
        {
            Now += duration;
            ElapseWithoutTimers(duration);
            foreach (var timer in Timers.ToArray())
            {
                if (!timer.Disposed && timer.Due <= timestamp) { timer.Fire(); }
            }
        }
        internal sealed class ManualTimer(Action callback, long due) : ITimer
        {
            internal bool Disposed { get; private set; }
            internal long Due { get; } = due;
            internal void Fire() => callback();
            public bool Change(TimeSpan dueTime, TimeSpan period) => !Disposed;
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}

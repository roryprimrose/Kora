using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.UnitTests.Interaction;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class AuditImpactPreviewServiceTests
{
    [Fact]
    public async Task Observation_trace_is_content_free_rooted_and_never_adopts_hostile_authority()
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
        var preview = completed.Single(activity => string.Equals(activity.OperationName, "retention.preview", StringComparison.Ordinal));
        preview.ParentId.Should().BeNull();
        preview.Status.Should().Be(ActivityStatusCode.Ok);
        preview.Source.Version.Should().NotBeNullOrWhiteSpace();
        preview.TagObjects.Select(tag => tag.Key).Should().Equal("kora.request.id");
        preview.Baggage.Should().BeEmpty();
        Activity.Current.Should().BeSameAs(hostile);
        fixture.Reader.Current = false;
        await ((Func<Task>)(() => fixture.Preview())).Should().ThrowAsync<InvalidDataException>();
        completed.Last().Status.Should().Be(ActivityStatusCode.Error);
        fixture.Audit.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Confirmed_saved_preference_is_bound_exactly_and_expiry_needs_no_subscriber()
    {
        await using var fixture = new Fixture();
        fixture.Preferences.Value = new(90);
        fixture.Configuration.Observe();
        await fixture.Preview();
        fixture.Service.Get().RequiredAuthority.Should().NotBeNull();
        fixture.Time.Advance(AuditRetentionImpact.PreviewLifetime);
        fixture.Service.Get().RequiredAuthority.Should().BeNull();
        await fixture.Preview();
        fixture.Preferences.Value = null;
        fixture.Service.Get().RequiredAuthority.Should().BeNull();
    }

    [Fact]
    public async Task Exact_proposal_saved_readback_fixed_cutoff_sources_zero_and_nonzero_are_observation_only()
    {
        await using var fixture = new Fixture();
        var notices = 0;
        fixture.Service.Changed += (_, _) => notices++;
        fixture.Service.Get().RequiredAuthority.Should().BeNull();
        await fixture.Preview();
        var result = fixture.Service.Get();
        result.ProposedDays.Should().Be(30);
        result.CutoffUtc.Should().Be(fixture.Time.Now);
        result.RequiredAuthority!.Aggregate!.AffectedCount.Should().Be(1);
        result.DiagnosticProjection!.Aggregate!.AffectedCount.Should().Be(0);
        fixture.Reader.Requests.Select(r => r.Source).Should().Equal(AuditImpactSource.RequiredAuthority, AuditImpactSource.DiagnosticProjection);
        fixture.Reader.Requests.Select(r => r.Request.RequestId).Distinct().Should().ContainSingle();
        fixture.Store.Tasks.Should().BeEmpty();
        fixture.Audit.Writes.Should().Be(0);
        fixture.Preferences.Writes.Should().Be(0);
        fixture.Policy.Effective.Should().Be(AuditRetentionDays.Default);
        notices.Should().Be(2);
        fixture.Time.Advance(AuditRetentionImpact.PreviewLifetime);
        fixture.Service.Get().Status.Should().Contain("timed out");
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("not-shorter")]
    [InlineData("host")]
    [InlineData("unavailable")]
    [InlineData("policy")]
    [InlineData("saved")]
    [InlineData("readback")]
    [InlineData("corrupt")]
    [InlineData("io")]
    [InlineData("access")]
    public async Task Invalid_unknown_changed_and_corrupt_preferences_never_start_a_scan(string reason)
    {
        await using var fixture = new Fixture();
        var value = reason is "invalid" ? "029" : reason is "not-shorter" ? "90" : "30";
        if (reason is "host") { fixture.Eligible = false; }
        if (reason is "unavailable") { fixture.Configuration.HoldUnavailable(); }
        if (reason is "policy") { fixture.Policy.HoldUnavailable(); }
        if (reason is "saved") { fixture.Preferences.Value = new(90); }
        if (reason is "readback") { fixture.Preferences.ReadBackValue = new(365); }
        if (reason is "corrupt") { fixture.Preferences.Failure = new InvalidDataException(); }
        if (reason is "io") { fixture.Preferences.Failure = new IOException(); }
        if (reason is "access") { fixture.Preferences.Failure = new UnauthorizedAccessException(); }
        var action = () => fixture.Service.PreviewAsync(value, () => fixture.Eligible);
        await action.Should().ThrowAsync<Exception>();
        fixture.Reader.Requests.Should().BeEmpty();
        fixture.Service.Get().RequiredAuthority.Should().BeNull();
    }

    [Theory]
    [InlineData("host")]
    [InlineData("preference")]
    [InlineData("policy")]
    [InlineData("store")]
    [InlineData("readback")]
    [InlineData("corrupt")]
    [InlineData("reader-event")]
    [InlineData("config-event")]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("projection-store")]
    public async Task Published_preview_retires_instead_of_reviving_on_recovered_source_or_host(string reason)
    {
        await using var fixture = new Fixture();
        await fixture.Preview();
        if (reason is "host") { fixture.Eligible = false; }
        if (reason is "preference") { fixture.Preferences.Value = new(365); }
        if (reason is "policy") { fixture.Policy.HoldUnavailable(); }
        if (reason is "store") { fixture.Reader.Current = false; }
        if (reason is "readback") { fixture.Preferences.ReadBackValue = new(30); }
        if (reason is "corrupt") { fixture.Preferences.Failure = new InvalidDataException(); }
        if (reason is "reader-event") { fixture.Reader.Signal(); }
        if (reason is "config-event") { fixture.Configuration.HoldUnavailable(); }
        if (reason is "io") { fixture.Preferences.Failure = new IOException(); }
        if (reason is "access") { fixture.Preferences.Failure = new UnauthorizedAccessException(); }
        if (reason is "projection-store") { fixture.Reader.StaleSource = AuditImpactSource.DiagnosticProjection; }
        fixture.Service.Get().RequiredAuthority.Should().BeNull();
        fixture.Reader.Current = true;
        fixture.Eligible = true;
        fixture.Preferences.Value = null;
        fixture.Preferences.Failure = null;
        fixture.Preferences.ReadBackValue = null;
        fixture.Service.Get().RequiredAuthority.Should().BeNull();
    }

    [Theory]
    [InlineData(AuditImpactStatus.Incomplete)]
    [InlineData(AuditImpactStatus.Unavailable)]
    public async Task Independent_unqualified_source_never_appears_as_zero_or_complete_preview(AuditImpactStatus status)
    {
        await using var fixture = new Fixture();
        fixture.Reader.ProjectionStatus = status;
        await fixture.Preview();
        fixture.Service.Get().Status.Should().Contain("Incomplete/unavailable");
        fixture.Service.Get().RequiredAuthority!.Status.Should().Be(AuditImpactStatus.Complete);
        fixture.Service.Get().DiagnosticProjection!.Aggregate.Should().BeNull();
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("close")]
    [InlineData("cancel")]
    [InlineData("timeout")]
    [InlineData("host")]
    [InlineData("store")]
    [InlineData("failure")]
    [InlineData("malformed")]
    public async Task Cancellation_failure_timeout_and_late_receipts_cannot_publish_or_retire_a_new_preview(string reason)
    {
        await using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var pending = new TaskCompletionSource<AuditImpactResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Reader.Pending = pending.Task;
        var task = fixture.Service.PreviewAsync("30", () => fixture.Eligible, cancellation.Token);
        if (reason is "edit") { fixture.Service.Retire("Proposal edited."); }
        if (reason is "close") { fixture.Service.Dispose(); }
        if (reason is "cancel") { await cancellation.CancelAsync(); }
        if (reason is "timeout") { fixture.Time.Advance(AuditRetentionImpact.ScanTimeout); }
        if (reason is "host") { fixture.Eligible = false; }
        if (reason is "store") { fixture.Reader.Current = false; }
        if (reason is "failure") { pending.SetException(new IOException()); }
        else
        {
            pending.SetResult(reason is "malformed"
                ? new(AuditImpactSource.RequiredAuthority, AuditImpactStatus.Complete, null, "stamp")
                : fixture.Reader.Result(AuditImpactSource.RequiredAuthority, new(30), fixture.Time.Now));
        }
        await ((Func<Task>)(() => task)).Should().ThrowAsync<Exception>();
        fixture.Service.Get().RequiredAuthority.Should().BeNull();
        if (reason is "close")
        {
            await ((Func<Task>)(() => fixture.Preview())).Should().ThrowAsync<ObjectDisposedException>();
            return;
        }
        fixture.Reader.Pending = null;
        fixture.Reader.Current = true;
        fixture.Eligible = true;
        await fixture.Preview();
        var timer = fixture.Time.Timers.Last();
        fixture.Service.Retire("Refresh.");
        await fixture.Preview();
        timer.Tick();
        fixture.Service.Get().RequiredAuthority.Should().NotBeNull();
    }

    internal sealed class Reader : IAuditRetentionImpactReader
    {
        internal List<(AuditImpactSource Source, HostRequest Request)> Requests { get; } = [];
        internal bool Current { get; set; } = true;
        internal AuditImpactSource? StaleSource { get; set; }
        internal AuditImpactStatus ProjectionStatus { get; set; } = AuditImpactStatus.Complete;
        internal Task<AuditImpactResult>? Pending { get; set; }
        public event EventHandler? Changed;
        internal void Signal() => Changed?.Invoke(this, EventArgs.Empty);
        public bool IsCurrent(AuditImpactResult result) => Current && result.Source != StaleSource;
        public ValueTask<AuditImpactResult> ReadAsync(AuditImpactSource source, AuditRetentionDays proposed,
            DateTimeOffset cutoff, HostRequest request, CancellationToken cancellationToken)
        {
            Requests.Add((source, request));
            return Pending is { } pending && source == AuditImpactSource.RequiredAuthority
                ? new(pending) : ValueTask.FromResult(Result(source, proposed, cutoff));
        }
        internal AuditImpactResult Result(AuditImpactSource source, AuditRetentionDays proposed, DateTimeOffset cutoff)
        {
            if (source == AuditImpactSource.DiagnosticProjection && ProjectionStatus != AuditImpactStatus.Complete)
            {
                return new(source, ProjectionStatus, null, ProjectionStatus == AuditImpactStatus.Incomplete ? "stamp" : null);
            }
            var impact = new AuditRetentionImpact(proposed, cutoff);
            if (source == AuditImpactSource.RequiredAuthority) { impact.Add(cutoff.AddDays(-40), cutoff.AddDays(50), 10); }
            return impact.Finish(source, "stamp");
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
        internal AuditRetentionPolicy Policy { get; } = new();
        internal AuditRetentionAdmission Admission { get; }
        internal AuditRetentionConfigurationService Configuration { get; }
        internal Reader Reader { get; } = new();
        internal QuestionReplyFixture.ReplyTime Time { get; } = new() { Now = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero) };
        internal AuditImpactPreviewService Service { get; }
        internal bool Eligible { get; set; } = true;
        internal Task Preview() => Service.PreviewAsync("30", () => Eligible, TestContext.Current.CancellationToken);
        public async ValueTask DisposeAsync() { Service.Dispose(); await Admission.DisposeAsync(); }
    }

    private sealed class Preferences : IAuditRetentionPreferences
    {
        internal AuditRetentionDays? Value { get; set; }
        internal AuditRetentionDays? ReadBackValue { get; set; }
        internal Exception? Failure { get; set; }
        internal int Writes { get; private set; }
        public AuditRetentionDays? Load() { if (Failure is { } failure) { throw failure; } return Value; }
        public AuditRetentionDays? ReadBack() => ReadBackValue ?? Load();
        public void BeginWrite() => Writes++;
        public void ConfirmWrite() => Writes++;
        public void Save(AuditRetentionDays value) => Writes++;
        public void Reset() => Writes++;
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal int Writes { get; private set; }
        public void Write(SecurityAuditEvent auditEvent) => Writes++;
    }
}

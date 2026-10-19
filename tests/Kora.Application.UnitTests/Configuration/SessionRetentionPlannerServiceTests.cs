using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.UnitTests.Interaction;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class SessionRetentionPlannerServiceTests
{
    [Fact]
    public async Task ExplicitPreviewBindsOneCutoffProposalRevisionsAndSourceWithoutWritesOrHostAuthority()
    {
        await using var fixture = new Fixture();
        fixture.Service.Get().Result.Should().BeNull();
        await fixture.Preview();
        var state = fixture.Service.Get();
        state.Proposed.Should().Be(new SessionRetentionSettings(1, 3));
        state.CutoffUtc.Should().Be(fixture.Time.Now);
        state.ExpiresUtc.Should().Be(fixture.Time.Now.Add(SessionRetentionPlanner.PreviewLifetime));
        state.Preference.Should().Be(fixture.Configuration.GetObservation());
        state.Admission.Should().Be(fixture.AdmissionSnapshot);
        state.Result!.Aggregate!.NewlyDeleteDue.Should().Be(1);
        fixture.Reader.Cutoffs.Should().Equal(fixture.Time.Now);
        fixture.Reader.Requests.Should().OnlyContain(r => r.Origin == RequestOrigin.LocalUi && r.InvocationId == null);
        fixture.Store.Tasks.Should().BeEmpty();
        fixture.Preferences.Writes.Should().Be(0);
        fixture.Audit.Writes.Should().Be(0);
        fixture.Policy.Settings.Should().Be(SessionRetentionSettings.Default);
        fixture.Time.Advance(SessionRetentionPlanner.PreviewLifetime);
        fixture.Service.Get().Result.Should().BeNull();
    }

    [Theory]
    [InlineData("preference")]
    [InlineData("readback")]
    [InlineData("corrupt")]
    [InlineData("access")]
    [InlineData("io")]
    [InlineData("unobserved")]
    [InlineData("policy")]
    [InlineData("policy-aba")]
    [InlineData("host")]
    [InlineData("invalid")]
    public async Task InvalidOrUnconfirmedAdmissionCannotStartScanning(string reason)
    {
        await using var fixture = new Fixture(observe: reason is not "unobserved");
        if (reason is "preference") { fixture.Preferences.Value = new(2, 60); }
        if (reason is "readback") { fixture.Preferences.ReadBackValue = new(2, 60); }
        if (reason is "corrupt") { fixture.Preferences.Failure = new InvalidDataException(); }
        if (reason is "access") { fixture.Preferences.Failure = new UnauthorizedAccessException(); }
        if (reason is "io") { fixture.Preferences.Failure = new IOException(); }
        if (reason is "policy") { fixture.Policy.HoldUnavailable(); }
        if (reason is "host") { fixture.Eligible = false; }
        if (reason is "policy-aba")
        {
            fixture.Policy.HoldUnavailable();
            fixture.Policy.Activate(new(2, 60));
        }
        var action = () => fixture.Service.PreviewAsync(reason is "invalid" ? default : new(1, 3),
            fixture.AdmissionSnapshot, () => fixture.Eligible, TestContext.Current.CancellationToken);
        await action.Should().ThrowAsync<Exception>();
        fixture.Reader.Requests.Should().BeEmpty();
        fixture.Service.Get().Result.Should().BeNull();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("event-aba")]
    [InlineData("preference")]
    [InlineData("readback")]
    [InlineData("policy-aba")]
    [InlineData("refresh")]
    [InlineData("host")]
    [InlineData("corrupt")]
    [InlineData("edit")]
    [InlineData("close")]
    [InlineData("expiry")]
    public async Task PublishedResultsRetireWithoutImplicitReplayOrRevival(string reason)
    {
        await using var fixture = new Fixture();
        var notices = 0;
        fixture.Service.Changed += (_, _) => notices++;
        await fixture.Preview();
        var original = fixture.Service.Get().Preference!;
        if (reason is "source") { fixture.Reader.Current = false; }
        if (reason is "event-aba") { fixture.Reader.Signal(); }
        if (reason is "preference") { fixture.Preferences.Value = new(2, 60); }
        if (reason is "readback") { fixture.Preferences.ReadBackValue = new(2, 60); }
        if (reason is "policy-aba")
        {
            fixture.Policy.Activate(new(2, 60));
            fixture.Policy.Activate(SessionRetentionSettings.Default);
        }
        if (reason is "refresh") { fixture.Configuration.Observe(); }
        if (reason is "host") { fixture.Eligible = false; }
        if (reason is "corrupt") { fixture.Preferences.Failure = new InvalidDataException(); }
        if (reason is "edit") { fixture.Service.Retire("Proposal edited."); }
        if (reason is "close") { fixture.Service.Dispose(); }
        if (reason is "expiry") { fixture.Time.Advance(SessionRetentionPlanner.PreviewLifetime); }
        fixture.Service.Get().Result.Should().BeNull();
        fixture.Reader.Current = true;
        fixture.Eligible = true;
        fixture.Preferences.Value = null;
        fixture.Preferences.ReadBackValue = null;
        fixture.Preferences.Failure = null;
        fixture.Service.Get().Result.Should().BeNull();
        fixture.Reader.Requests.Should().HaveCount(1);
        notices.Should().BeGreaterThan(0);
        if (reason is "refresh")
        {
            fixture.Configuration.GetObservation().PreferenceRevision.Should().BeGreaterThan(original.PreferenceRevision);
        }
    }

    [Theory]
    [InlineData(SessionRetentionPlanStatus.Complete)]
    [InlineData(SessionRetentionPlanStatus.Incomplete)]
    [InlineData(SessionRetentionPlanStatus.Unavailable)]
    public async Task CompleteZeroIncompleteAndUnavailableRemainDistinct(SessionRetentionPlanStatus status)
    {
        await using var fixture = new Fixture();
        fixture.Reader.Result = status == SessionRetentionPlanStatus.Complete
            ? new(status, new(0, 0, 0, 0, 0, 0, 0, 0, 0), "stamp")
            : new(status, null, status == SessionRetentionPlanStatus.Incomplete ? "stamp" : null);
        await fixture.Preview();
        var result = fixture.Service.Get().Result!;
        result.Status.Should().Be(status);
        if (status == SessionRetentionPlanStatus.Complete) { result.Aggregate!.Sessions.Should().Be(0); }
        else { result.Aggregate.Should().BeNull(); }
    }

    [Theory]
    [InlineData("event")]
    [InlineData("refresh")]
    [InlineData("host")]
    [InlineData("stale")]
    [InlineData("cancel")]
    [InlineData("timeout")]
    [InlineData("close")]
    [InlineData("invalid-result")]
    [InlineData("throw")]
    public async Task InFlightFailuresAndStaleReceiptsNeverPublish(string reason)
    {
        await using var fixture = new Fixture();
        var notices = 0;
        fixture.Service.Changed += (_, _) => notices++;
        var pending = new TaskCompletionSource<SessionRetentionPlanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Reader.Pending = pending.Task;
        using var cancellation = new CancellationTokenSource();
        var preview = fixture.Service.PreviewAsync(new(1, 3), fixture.AdmissionSnapshot, () => fixture.Eligible, cancellation.Token);
        if (reason is "event") { fixture.Reader.Signal(); }
        if (reason is "refresh") { fixture.Configuration.Observe(); }
        if (reason is "host") { fixture.Eligible = false; }
        if (reason is "stale") { fixture.Reader.Current = false; }
        if (reason is "cancel") { await cancellation.CancelAsync(); }
        if (reason is "timeout") { fixture.Time.Advance(SessionRetentionPlanner.ScanTimeout); }
        if (reason is "close") { fixture.Service.Dispose(); }
        if (reason is "throw") { pending.SetException(new IOException("metadata unavailable")); }
        else
        {
            pending.SetResult(reason is "invalid-result"
                ? new(SessionRetentionPlanStatus.Complete, null, "stamp")
                : fixture.Reader.Result);
        }
        var action = () => preview;
        await action.Should().ThrowAsync<Exception>();
        fixture.Service.Get().Result.Should().BeNull();
        fixture.Reader.Cancellation.IsCancellationRequested.Should().BeTrue();
        fixture.Preferences.Writes.Should().Be(0);
        notices.Should().BeGreaterThan(0);
        if (reason is "close")
        {
            await ((Func<Task>)(() => fixture.Preview())).Should().ThrowAsync<ObjectDisposedException>();
        }
    }

    [Fact]
    public async Task SupersededCompletionAndOldExpiryCannotRetireNewPreview()
    {
        await using var fixture = new Fixture();
        var pending = new TaskCompletionSource<SessionRetentionPlanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Reader.Pending = pending.Task;
        var old = fixture.Preview();
        fixture.Service.Retire("Edit.");
        fixture.Reader.Pending = null;
        await fixture.Preview();
        pending.SetResult(fixture.Reader.Result);
        await ((Func<Task>)(() => old)).Should().ThrowAsync<OperationCanceledException>();
        fixture.Service.Get().Result.Should().NotBeNull();
        var timer = fixture.Time.Timers.Last();
        fixture.Service.Retire("Refresh.");
        await fixture.Preview();
        timer.Tick();
        fixture.Service.Get().Result.Should().NotBeNull();
    }

    [Fact]
    public async Task DisplayExpiryDoesNotDependOnTimerDispatchAndArgumentsDoNotStartReads()
    {
        await using var fixture = new Fixture();
        await fixture.Preview();
        fixture.Time.Now += SessionRetentionPlanner.PreviewLifetime;
        fixture.Service.Get().Result.Should().BeNull();
        var noGate = () => fixture.Service.PreviewAsync(new(1, 3), fixture.AdmissionSnapshot, null!);
        await noGate.Should().ThrowAsync<ArgumentNullException>();
        var noSnapshot = () => fixture.Service.PreviewAsync(new(1, 3), null!, static () => true);
        await noSnapshot.Should().ThrowAsync<ArgumentNullException>();
        fixture.Reader.Requests.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("refresh")]
    [InlineData("event")]
    [InlineData("cancel")]
    [InlineData("timeout")]
    public async Task ChangesDuringFinalSourceProbeCannotPublishPastAdmissionOrCancellation(string reason)
    {
        await using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Reader.OnCurrent = () =>
        {
            if (reason is "host") { fixture.Eligible = false; }
            if (reason is "refresh") { fixture.Configuration.Observe(); }
            if (reason is "event") { fixture.Reader.Signal(); }
            if (reason is "cancel") { cancellation.Cancel(); }
            if (reason is "timeout") { fixture.Time.Advance(SessionRetentionPlanner.ScanTimeout); }
        };
        var action = () => fixture.Service.PreviewAsync(new(1, 3), fixture.AdmissionSnapshot, () => fixture.Eligible, cancellation.Token);
        await action.Should().ThrowAsync<Exception>();
        fixture.Service.Get().Result.Should().BeNull();
    }

    [Fact]
    public async Task ExpiryDuringSourceProbeCannotEscapeTheDisplayFence()
    {
        await using var fixture = new Fixture();
        await fixture.Preview();
        fixture.Reader.OnCurrent = () => fixture.Time.Now += SessionRetentionPlanner.PreviewLifetime;
        fixture.Service.Get().Result.Should().BeNull();
    }

    [Fact]
    public async Task ObservationTraceIsContentFreeAndCannotAdoptIncomingAuthority()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "Kora.Application", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => stopped.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);
        await using var fixture = new Fixture();
        using var hostile = new Activity("hostile").SetIdFormat(ActivityIdFormat.W3C).Start();
        await fixture.Preview();
        var activity = stopped.Single(a => a.OperationName is "session.retention.preview");
        activity.ParentId.Should().BeNull();
        activity.Status.Should().Be(ActivityStatusCode.Ok);
        activity.Source.Version.Should().NotBeNullOrWhiteSpace();
        activity.TagObjects.Select(t => t.Key).Should().Equal("kora.request.id");
        activity.Baggage.Should().BeEmpty();
        Activity.Current.Should().BeSameAs(hostile);
        fixture.Reader.Result = new(SessionRetentionPlanStatus.Unavailable, null, null);
        await fixture.Preview();
        stopped.Last().Status.Should().Be(ActivityStatusCode.Error);
        fixture.Reader.Current = false;
        fixture.Reader.Result = new(SessionRetentionPlanStatus.Complete, new(0, 0, 0, 0, 0, 0, 0, 0, 0), "stamp");
        await ((Func<Task>)(() => fixture.Preview())).Should().ThrowAsync<InvalidDataException>();
        stopped.Last().Status.Should().Be(ActivityStatusCode.Error);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal Fixture(bool observe = true)
        {
            Admission = new(Store, Store, new(Store));
            Configuration = new(Preferences, Policy, Admission, Audit);
            if (observe) { Configuration.Observe(); }
            Service = new(Reader, Configuration, Time);
        }
        internal AudioControlTestStore Store { get; } = new();
        internal DiagnosticRetentionAdmission Admission { get; }
        internal Preferences Preferences { get; } = new();
        internal Audit Audit { get; } = new();
        internal SessionRetentionPolicy Policy { get; } = new();
        internal SessionRetentionConfigurationService Configuration { get; }
        internal Reader Reader { get; } = new();
        internal QuestionReplyFixture.ReplyTime Time { get; } = new() { Now = new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero) };
        internal SessionRetentionPlannerService Service { get; }
        internal SessionRetentionPlannerAdmission AdmissionSnapshot { get; } = new(2, 3, 4, 5);
        internal bool Eligible { get; set; } = true;
        internal Task Preview() => Service.PreviewAsync(new(1, 3), AdmissionSnapshot, () => Eligible, TestContext.Current.CancellationToken);
        public async ValueTask DisposeAsync() { Service.Dispose(); await Admission.DisposeAsync(); }
    }

    internal sealed class Reader : ISessionRetentionPlannerReader
    {
        internal SessionRetentionPlanResult Result { get; set; } =
            new(SessionRetentionPlanStatus.Complete, new(1, 0, 1, 0, 0, 0, 0, 0, 0), "stamp");
        internal bool Current { get; set; } = true;
        internal Task<SessionRetentionPlanResult>? Pending { get; set; }
        internal List<HostRequest> Requests { get; } = [];
        internal List<DateTimeOffset> Cutoffs { get; } = [];
        internal CancellationToken Cancellation { get; private set; }
        internal Action? OnCurrent { get; set; }
        public event EventHandler? Changed;
        internal void Signal() => Changed?.Invoke(this, EventArgs.Empty);
        public bool IsCurrent(SessionRetentionPlanResult result) { OnCurrent?.Invoke(); return Current; }
        public ValueTask<SessionRetentionPlanResult> ReadAsync(SessionRetentionSettings proposed, DateTimeOffset cutoff,
            HostRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Cutoffs.Add(cutoff);
            Cancellation = cancellationToken;
            return Pending is { } pending ? new(pending) : ValueTask.FromResult(Result);
        }
    }

    private sealed class Preferences : ISessionRetentionPreferences
    {
        internal SessionRetentionSettings? Value { get; set; }
        internal SessionRetentionSettings? ReadBackValue { get; set; }
        internal Exception? Failure { get; set; }
        internal int Writes { get; private set; }
        public SessionRetentionSettings? Load() { if (Failure is { } failure) { throw failure; } return Value; }
        public SessionRetentionSettings? ReadBack() => ReadBackValue ?? Load();
        public void BeginWrite() => Writes++;
        public void ConfirmWrite() => Writes++;
        public void Save(SessionRetentionSettings settings) => Writes++;
        public void Reset() => Writes++;
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal int Writes { get; private set; }
        public void Write(SecurityAuditEvent auditEvent) => Writes++;
    }
}

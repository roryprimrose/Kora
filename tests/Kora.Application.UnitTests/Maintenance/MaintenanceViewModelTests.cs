using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Maintenance;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Maintenance;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Maintenance;

[Collection("Host tracing")]
public sealed class MaintenanceViewModelTests
{
    [Fact]
    public async Task Native_check_review_open_and_snooze_are_composed_without_model_or_effect_authority()
    {
        using var f = new Fixture();
        f.State.CurrentVersion.Should().Be("1.0.0");
        f.State.Disclosure.Should().Contain("No updater");
        MaintenanceViewModel.Channels.Should().Contain(ReleaseChannel.Production).And.Contain(ReleaseChannel.Preview);
        await f.State.CheckCommand.ExecuteAsync();
        f.Client.Calls.Should().Be(0);
        await f.State.OpenCommand.ExecuteAsync();
        f.Opener.Versions.Should().BeEmpty();
        f.State.NetworkEnabled = true;
        f.State.NetworkEnabled = true;
        await f.State.CheckCommand.ExecuteAsync();
        f.State.Availability.Should().Be(ReleaseAvailability.Available);
        f.State.CanOpen.Should().BeTrue();
        f.State.ReleasePage.Should().Be("https://github.com/roryprimrose/Kora/releases/tag/v1.2.3");
        f.State.ReleaseDetails.Should().Contain("Expected SHA-256");
        f.State.VerificationStatus.Should().NotContain("stale");
        f.State.NextCheck.Should().Be(f.Time.Now.AddHours(6).AddMinutes(15));
        await f.State.SnoozeCommand.ExecuteAsync();
        f.State.Status.Should().Contain("24 hours");
        await f.State.OpenAsync();
        f.Opener.Versions.Should().ContainSingle().Which.ToString().Should().Be("1.2.3");
        f.Audit.Events.Should().Contain(item => item.ActionId == "maintenance.open-release" && item.Outcome == SecurityAuditOutcome.Succeeded);
        await f.State.CheckAsync();
        f.Client.Calls.Should().Be(1);
        f.State.Status.Should().Contain("deferred");
        f.Time.Now = f.State.NextCheck!.Value;
        await f.State.CheckAsync();
        f.State.Status.Should().Contain("snoozed");
        f.State.Channel = ReleaseChannel.Preview;
        f.State.Channel = ReleaseChannel.Preview;
        f.State.LastVerified.Should().BeNull();
        f.State.ReleasePage.Should().BeEmpty();
        f.State.NetworkEnabled = false;
        f.State.NetworkEnabled.Should().BeFalse();
        f.State.IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task Failed_or_stale_result_retains_historical_timestamp_not_up_to_date_or_navigation_authority()
    {
        using var f = new Fixture();
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        var verified = f.State.LastVerified;
        f.Time.Now = f.Time.Now.AddHours(6);
        f.State.IsStale.Should().BeTrue();
        f.State.CanOpen.Should().BeFalse();
        f.Time.Now = f.State.NextCheck!.Value;
        f.Client.Result = new(ReleaseAvailability.Unavailable, "404", f.Time.Now);
        await f.State.CheckAsync();
        f.State.Availability.Should().Be(ReleaseAvailability.Unavailable);
        f.State.LastVerified.Should().Be(verified);
        f.State.VerificationStatus.Should().Contain("stale");
        f.State.ReleaseDetails.Should().Contain("No currently verified");
        await f.State.OpenAsync();
        f.Opener.Versions.Should().BeEmpty();
        f.State.NextCheck.Should().Be(f.Time.Now.AddMinutes(5));
        f.State.NetworkEnabled = false;
        f.State.NetworkEnabled = true;
        f.State.Channel = ReleaseChannel.Preview;
        await f.State.CheckAsync();
        f.Client.Calls.Should().Be(2);
        for (var index = 0; index < 9; index++)
        {
            var next = f.State.NextCheck!.Value;
            f.Time.Now = next;
            await f.State.CheckAsync();
            (f.State.NextCheck!.Value - f.Time.Now).Should().BeLessThanOrEqualTo(TimeSpan.FromHours(6));
        }
        f.Time.Now = f.State.NextCheck!.Value;
        f.Client.Result = new(ReleaseAvailability.RateLimited, "429", f.Time.Now, RetryAt: f.Time.Now.AddHours(24));
        await f.State.CheckAsync();
        f.State.NextCheck.Should().Be(f.Time.Now.AddHours(24));
    }

    [Theory]
    [InlineData(RequestOrigin.ActivatedVoice)]
    [InlineData(RequestOrigin.HostSystem)]
    public async Task Ambient_origin_cannot_be_relabelled_as_native_UI(RequestOrigin origin)
    {
        using var f = new Fixture();
        using var activity = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        f.State.NetworkEnabled = true;
        f.State.NetworkEnabled.Should().BeFalse();
        f.State.Channel = ReleaseChannel.Preview;
        f.State.Channel.Should().Be(ReleaseChannel.Production);
        await f.State.CheckAsync();
        await f.State.OpenAsync();
        await f.State.SnoozeAsync();
        f.Client.Calls.Should().Be(0);
        f.Opener.Versions.Should().BeEmpty();
        f.Audit.Events.Should().OnlyContain(item => item.Outcome != SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task Ownership_privacy_call_denial_cancel_and_dispose_block_late_callbacks_and_no_unlock_replay()
    {
        using var f = new Fixture();
        f.Admitted = false;
        f.State.NetworkEnabled = true;
        f.State.NetworkEnabled.Should().BeFalse();
        f.State.ReleasePage.Should().BeEmpty();
        f.Admitted = true;
        f.State.NetworkEnabled = true;
        var source = new TaskCompletionSource<ReleaseCheck>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Client.Pending = source.Task;
        var checking = f.State.CheckAsync();
        await f.State.CheckAsync();
        f.Client.Calls.Should().Be(1);
        f.State.CanOpen.Should().BeFalse();
        f.Admitted = false;
        f.State.PrivacyClosed();
        f.Client.Token.IsCancellationRequested.Should().BeTrue();
        source.SetResult(f.Client.Success(f.Time.Now));
        await checking;
        f.State.LastVerified.Should().BeNull();
        f.State.Availability.Should().Be(ReleaseAvailability.Unknown);
        f.Admitted = true;
        f.Client.Pending = null;
        await f.State.CheckAsync();
        f.State.CanOpen.Should().BeTrue();
        f.State.Dispose();
        f.State.Dispose();
        f.State.PrivacyClosed();
        f.Time.Timers[0].Fire();
        await f.State.CheckAsync();
        f.State.CanOpen.Should().BeFalse();
        f.Time.Timers[0].Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Scheduled_checks_are_linked_deterministic_and_defer_when_admission_closes()
    {
        using var f = new Fixture();
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        f.State.NetworkEnabled = true;
        f.Time.Timers[0].Due.Should().Be(TimeSpan.Zero);
        f.Time.Timers[0].Fire();
        f.Client.Calls.Should().Be(1);
        stopped.Should().Contain(activity => activity.OperationName == "runtime.request" && activity.Links.Any());
        f.Admitted = false;
        f.Time.Now = f.State.NextCheck!.Value;
        f.Time.Timers[0].Fire();
        f.Client.Calls.Should().Be(1);
        f.State.Status.Should().Contain("deferred");
        f.State.NetworkEnabled = false;
        f.State.NetworkEnabled.Should().BeTrue();
        f.Admitted = true;
        f.State.NetworkEnabled = false;
        f.Time.Timers[0].Fire();
        f.Client.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Failures_cancel_invalid_jitter_and_invalid_snooze_are_explicit()
    {
        using var f = new Fixture();
        await f.State.SnoozeAsync();
        f.State.Status.Should().Contain("Unknown");
        ((Action)(() => f.State.Channel = (ReleaseChannel)99)).Should().Throw<InvalidDataException>();
        f.State.NetworkEnabled = true;
        f.Client.Pending = Task.FromException<ReleaseCheck>(new OperationCanceledException("fixture"));
        await f.State.CheckAsync();
        f.State.Status.Should().Contain("cancelled");
        f.Client.Pending = null;
        f.Jitter = 31;
        await ((Func<Task>)(() => f.State.CheckCommand.ExecuteAsync())).Should().ThrowAsync<InvalidDataException>();
        f.Jitter = 0;
        await f.State.CheckAsync();
        f.Opener.Failure = new IOException("fixture");
        await f.State.OpenAsync();
        f.State.Status.Should().Contain("failed");
        f.State.CanOpen.Should().BeFalse();
        f.Client.Pending = Task.FromException<ReleaseCheck>(new IOException("fixture"));
        f.Time.Now = f.State.NextCheck!.Value;
        f.State.CheckCommand.Execute(null);
        f.State.Status.Should().Contain("Unknown");
    }

    [Fact]
    public async Task Passive_age_refresh_does_not_check_early_and_snooze_expires_without_replay()
    {
        using var f = new Fixture();
        f.State.VerificationStatus.Should().Contain("No successful");
        f.State.ReleaseDetails.Should().Contain("No currently");
        f.State.CanOpen.Should().BeFalse();
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        await f.State.SnoozeAsync();
        f.Time.Now = f.Time.Now.AddHours(6);
        f.Time.Timers[0].Fire();
        f.Client.Calls.Should().Be(1);
        f.Time.Timers[0].Due.Should().Be(TimeSpan.FromMinutes(15));
        f.State.IsStale.Should().BeTrue();
        f.Time.Now = f.Time.Now.AddHours(24);
        f.Client.Result = f.Client.Success(f.Time.Now);
        await f.State.CheckAsync();
        f.State.Status.Should().NotContain("snoozed");
        f.State.NetworkEnabled = false;
        f.State.IsStale.Should().BeTrue();
        f.State.ReleasePage.Should().BeEmpty();
        f.State.ReleaseDetails.Should().Contain("No currently");
        f.State.NetworkEnabled = true;
        f.Time.Now = f.State.NextCheck!.Value;
        f.Client.Result = new(ReleaseAvailability.Unknown, "invalid", f.Time.Now);
        await f.State.CheckAsync();
        f.State.ReleasePage.Should().BeEmpty();
        f.State.CanOpen.Should().BeFalse();
    }

    [Fact]
    public async Task Default_jitter_and_disposed_async_failure_are_safe()
    {
        using var f = new Fixture();
        using var defaults = new MaintenanceViewModel(f.Client, new AssemblyApplicationInfo(() => "1.0.0"),
            ReleaseArchitecture.X64, f.Opener, f.Time, new Dispatcher(), f.Audit, NullLogger<MaintenanceViewModel>.Instance);
        await defaults.CheckAsync();
        defaults.BindGate(() => true);
        defaults.NetworkEnabled = true;
        await defaults.CheckAsync();
        defaults.NextCheck.Should().BeOnOrAfter(f.Time.Now.AddHours(6)).And.BeOnOrBefore(f.Time.Now.AddHours(6.5));
        var pending = new TaskCompletionSource<ReleaseCheck>();
        f.Client.Pending = pending.Task;
        f.State.NetworkEnabled = true;
        var complete = new TaskCompletionSource();
        f.State.CheckCommand.CanExecuteChanged += (_, _) =>
        {
            if (f.State.CheckCommand.CanExecute(null)) { complete.TrySetResult(); }
        };
        f.State.CheckCommand.Execute(null);
        f.State.Dispose();
        pending.SetException(new IOException("late fixture"));
        await complete.Task.WaitAsync(TestContext.Current.CancellationToken);
        f.State.CanOpen.Should().BeFalse();
        f.State.Channel = ReleaseChannel.Preview;
        f.State.Status.Should().Contain("denied");
        f.State.NetworkEnabled = false;
        f.Time.Timers[0].Fire();
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("ownership")]
    [InlineData("dispose")]
    [InlineData("cancel")]
    [InlineData("disposed-cancel")]
    public async Task Navigation_revalidates_late_callbacks_without_claiming_browser_rollback(string transition)
    {
        using var f = new Fixture();
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Opener.Pending = completion.Task;
        var opening = f.State.OpenAsync();
        f.State.CanOpen.Should().BeFalse();
        if (transition is "dispose" or "disposed-cancel") { f.State.Dispose(); }
        if (transition is "privacy") { f.State.PrivacyClosed(); }
        if (transition is "ownership") { f.Admitted = false; }
        if (transition is "cancel" or "disposed-cancel") { completion.SetException(new OperationCanceledException("fixture")); }
        else { completion.SetResult(); }
        await opening;
        f.State.CanOpen.Should().BeFalse();
        f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Unknown);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ReleaseFixture tracing = new();
        internal readonly ReleaseFixture.Clock Time = new();
        internal readonly Client Client;
        internal readonly Opener Opener = new();
        internal readonly Audit Audit = new();
        internal readonly MaintenanceViewModel State;
        internal bool Admitted = true;
        internal int Jitter = 15;
        internal Fixture()
        {
            Client = new();
            State = new(Client, new AssemblyApplicationInfo(() => "1.0.0"), ReleaseArchitecture.X64,
                Opener, Time, new Dispatcher(), Audit, NullLogger<MaintenanceViewModel>.Instance, () => Jitter);
            State.BindGate(() => Admitted);
        }
        public void Dispose() { State.Dispose(); tracing.Dispose(); }
    }
    private sealed class Dispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Func<Task> action) => action();
        public void Post(Action action) => action();
    }
    private sealed class Client : IReleaseMetadataClient
    {
        internal int Calls;
        internal ReleaseCheck? Result;
        internal Task<ReleaseCheck>? Pending;
        internal CancellationToken Token;
        internal ReleaseCheck Success(DateTimeOffset now)
        {
            var version = ReleaseVersion.Parse("1.2.3");
            return new(ReleaseAvailability.Available, "canonical", now, now, new(1, version,
                ReleaseFixture.Source, ReleaseArchitecture.X64,
                [new(10, "Kora-1.2.3-win-x64.zip", 123, new string('b', 64))]));
        }
        public Task<ReleaseCheck> CheckAsync(ReleaseChannel channel, string currentVersion, ReleaseArchitecture architecture, CancellationToken cancellationToken)
        {
            Calls++;
            Token = cancellationToken;
            return Pending ?? Task.FromResult(Result ?? Success(new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero)));
        }
    }
    private sealed class Opener : ICanonicalReleasePageOpener
    {
        internal readonly List<ReleaseVersion> Versions = [];
        internal IOException? Failure;
        internal Task? Pending;
        public Task OpenAsync(ReleaseVersion version, CancellationToken cancellationToken)
        {
            if (Failure is not null) { throw Failure; }
            Versions.Add(version);
            return Pending ?? Task.CompletedTask;
        }
    }
    private sealed class Audit : ISecurityAuditLog
    {
        internal readonly List<SecurityAuditEvent> Events = [];
        public void Write(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }
}

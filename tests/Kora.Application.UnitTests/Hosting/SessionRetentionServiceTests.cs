using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Interaction;
using Kora.Application.UnitTests.Interaction;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Hosting;

[Collection("Host tracing")]
public sealed class SessionRetentionServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public SessionRetentionServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public async Task Bound_broker_retires_suppression_before_the_authority_delete_callback_without_reentering_its_gate()
    {
        using var events = new LocalEventBrokerTests.Fixture();
        await using var broker = events.Create();
        await events.Observe(broker);
        await using var fixture = new Fixture(broker);
        fixture.Store.Id = events.Session;
        var revoked = false;
        fixture.Service.Revoking += id =>
        {
            id.Should().Be(events.Session);
            events.Saved!.Receipts.Should().BeEmpty();
            revoked = true;
        };
        await fixture.Service.RunAsync(fixture.Token);
        revoked.Should().BeTrue();
        events.Audits.Should().Contain(item => item.Event.ActionId == "local-event.retire");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Host_only_run_revokes_sources_before_receipt_and_joins_owned_lifetime(bool subscriber)
    {
        await using var fixture = new Fixture();
        var revoked = new List<HostId<SessionIdentity>>();
        if (subscriber) { fixture.Service.Revoking += revoked.Add; }
        (await fixture.Service.RunAsync(fixture.Token)).Should().Be(fixture.Store.Result);
        revoked.Should().HaveCount(subscriber ? 1 : 0);
        fixture.Store.Calls.Should().Be(1);
        fixture.Logger.Completed.Should().Be(1);
        fixture.Service.Start();
        fixture.Service.Invoking(service => service.Start()).Should().Throw<InvalidOperationException>();
        await fixture.Service.DisposeAsync();
        await fixture.Service.DisposeAsync();
        fixture.Time.Timer!.Disposed.Should().BeTrue();
        fixture.Service.Invoking(service => service.Start()).Should().Throw<ObjectDisposedException>();
        var disposed = () => fixture.Service.RunAsync(fixture.Token);
        await disposed.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Theory]
    [InlineData("configuration")]
    [InlineData("access")]
    [InlineData("revision")]
    [InlineData("cancelled-source")]
    [InlineData("storage")]
    public async Task Unknown_admission_changed_revision_cancellation_and_storage_failure_cannot_be_success(string failure)
    {
        await using var fixture = new Fixture();
        if (failure is "configuration") { fixture.Configuration = false; }
        if (failure is "access") { fixture.Access.CanControl = false; }
        if (failure is "revision") { fixture.Store.BeforeRevoke = () => fixture.Access.ControlRevision++; }
        if (failure is "cancelled-source") { fixture.Store.CancelSource = true; }
        if (failure is "storage") { fixture.Store.Failure = new IOException(); }
        var action = () => fixture.Service.RunAsync(fixture.Token);
        await action.Should().ThrowAsync<Exception>();
        fixture.Logger.Completed.Should().Be(0);
        fixture.Logger.Failures.Should().Be(failure is "configuration" or "access" or "cancelled-source" ? 0 : 1);
    }

    [Fact]
    public async Task Precancelled_run_and_passive_access_never_run_cleanup_and_missing_inspection_seam_fails_closed()
    {
        await using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var cancelled = () => fixture.Service.RunAsync(cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        fixture.Store.Calls.Should().Be(0);
        var workspace = new SessionWorkspaceService(fixture.ControlStore, new(fixture.ControlStore), fixture.Access,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionWorkspaceService>.Instance);
        workspace.BindRetention(fixture.Service);
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var read = () => workspace.ReadSessionsAsync(null, 25, fixture.Token);
        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("*unavailable*");
        fixture.Store.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("available")]
    [InlineData("configuration")]
    [InlineData("access")]
    [InlineData("disposed")]
    public async Task Passive_inspection_preserves_configuration_privacy_and_owned_lifetime_without_cleanup(string scenario)
    {
        await using var fixture = new Fixture();
        if (scenario is "configuration") { fixture.Configuration = false; }
        if (scenario is "access") { fixture.Access.CanControl = false; }
        if (scenario is "disposed") { await fixture.Service.DisposeAsync(); }
        if (scenario is "available") { fixture.Service.RequirePassiveInspection(); }
        else
        {
            var inspect = () => fixture.Service.RequirePassiveInspection();
            inspect.Should().Throw<Exception>();
        }
        fixture.Store.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("io", false)]
    [InlineData("data", true)]
    [InlineData("access", true)]
    [InlineData("admission", true)]
    public async Task Timer_reports_failure_visibly_and_stops_without_execution_or_unowned_work(string failure, bool subscriber)
    {
        await using var fixture = new Fixture();
        fixture.Store.Failure = failure switch
        {
            "io" => new IOException(),
            "data" => new InvalidDataException(),
            "access" => new UnauthorizedAccessException(),
            _ => new InvalidOperationException(),
        };
        var reported = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (subscriber) { fixture.Service.Failed += message => reported.SetResult(message); }
        fixture.Service.Start();
        fixture.Time.Timer!.Fire();
        await fixture.Dispatcher.Invoked.Task.WaitAsync(fixture.Token);
        if (subscriber) { (await reported.Task.WaitAsync(fixture.Token)).Should().StartWith("Session retention held:"); }
        fixture.Logger.Failures.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Timer_hold_is_diagnostic_and_never_calls_storage(bool configuration)
    {
        await using var fixture = new Fixture();
        if (configuration) { fixture.Configuration = false; } else { fixture.Access.CanControl = false; }
        fixture.Service.Start();
        fixture.Time.Timer!.Fire();
        await fixture.Logger.Held.Task.WaitAsync(fixture.Token);
        fixture.Store.Calls.Should().Be(0);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly Preferences preferences = new();
        private readonly SessionRetentionConfigurationService configuration;
        internal Fixture(LocalEventBroker? broker = null)
        {
            Admission = new(ControlStore, ControlStore, new HostTaskCoordinator(ControlStore));
            configuration = new(preferences, new(), Admission, new Audit());
            configuration.Observe();
            Service = new(Store, configuration, Access, Dispatcher, Time, Logger, broker);
        }
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        internal AudioControlTestStore ControlStore { get; } = new();
        internal DiagnosticRetentionAdmission Admission { get; }
        internal Store Store { get; } = new();
        internal Access Access { get; } = new();
        internal Dispatcher Dispatcher { get; } = new();
        internal Clock Time { get; } = new();
        internal Logger Logger { get; } = new();
        internal SessionRetentionService Service { get; }
        internal bool Configuration
        {
            set
            {
                preferences.Invalid = !value;
                if (value) { configuration.Observe(); }
                else { configuration.Invoking(service => service.Observe()).Should().Throw<InvalidDataException>(); }
            }
        }
        public async ValueTask DisposeAsync() { await Service.DisposeAsync(); await Admission.DisposeAsync(); }
    }

    private sealed class Store : ISessionRetentionStore
    {
        internal SessionRetentionBatch Result { get; } = new(1, 2, 3, true);
        internal HostId<SessionIdentity> Id { get; set; } = new(Guid.NewGuid());
        internal Exception? Failure { get; set; }
        internal Action? BeforeRevoke { get; set; }
        internal bool CancelSource { get; set; }
        internal int Calls { get; private set; }
        public ValueTask<SessionRetentionState> ReadRetentionAsync(HostId<SessionIdentity> session, CancellationToken token) => throw new NotSupportedException();
        public ValueTask SetPerpetualAsync(HostRequest request, HostRevision generation, bool perpetual, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public async ValueTask<SessionRetentionBatch> ApplyRetentionAsync(Func<bool> admitted,
            Func<HostId<SessionIdentity>, CancellationToken, ValueTask> revokeSources, CancellationToken token)
        {
            HostActivity.RequireCurrent().Request.Origin.Should().Be(RequestOrigin.HostSystem);
            Calls++;
            if (Failure is { } failure) { throw failure; }
            BeforeRevoke?.Invoke();
            using var cancelled = new CancellationTokenSource();
            if (CancelSource) { await cancelled.CancelAsync(); }
            await revokeSources(Id, CancelSource ? cancelled.Token : token);
            if (!admitted()) { throw new InvalidOperationException("Admission changed."); }
            return Result;
        }
    }

    private sealed class Access : ISessionWorkspaceAccess
    {
        public bool CanInspect => true;
        public bool CanControl { get; set; } = true;
        public long ControlRevision { get; set; }
    }

    private sealed class Preferences : ISessionRetentionPreferences
    {
        internal bool Invalid { get; set; }
        public SessionRetentionSettings? Load() => Invalid ? throw new InvalidDataException() : null;
        public SessionRetentionSettings? ReadBack() => Load();
        public void BeginWrite() => throw new NotSupportedException();
        public void Save(SessionRetentionSettings settings) => throw new NotSupportedException();
        public void Reset() => throw new NotSupportedException();
        public void ConfirmWrite() => throw new NotSupportedException();
    }
    private sealed class Audit : ISecurityAuditLog { public void Write(SecurityAuditEvent auditEvent) => throw new NotSupportedException(); }

    private sealed class Dispatcher : IUiDispatcher
    {
        internal TaskCompletionSource Invoked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task InvokeAsync(Func<Task> action) { await action(); Invoked.TrySetResult(); }
        public void Post(Action action) => action();
    }

    private sealed class Logger : ILogger<SessionRetentionService>
    {
        internal int Completed { get; private set; }
        internal int Failures { get; private set; }
        internal TaskCompletionSource Held { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id == 1) { Completed++; }
            if (eventId.Id == 2) { Failures++; }
            if (eventId.Id == 3) { Held.TrySetResult(); }
        }
    }

    private sealed class Clock : TimeProvider
    {
        internal Timer? Timer { get; private set; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            Timer = new(callback, state);
    }
    private sealed class Timer(TimerCallback callback, object? state) : ITimer
    {
        internal bool Disposed { get; private set; }
        internal void Fire() => callback(state);
        public bool Change(TimeSpan dueTime, TimeSpan period) => !Disposed;
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}

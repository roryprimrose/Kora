using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Hosting;

public sealed class SessionWorkspaceServiceTests
{
    [Fact]
    public async Task Passive_pages_preserve_exact_typed_records_without_committing_intent()
    {
        using var fixture = new Fixture();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var page = await fixture.Service.ReadSessionsAsync(null, 1, fixture.Token);
        page.Records.Should().ContainSingle().Which.Should().Be(fixture.Session);
        page.Next.Should().Be(fixture.Session.SessionId.Value);
        page = await fixture.Service.ReadSessionsAsync(page.Next, 1, fixture.Token);
        page.Records.Should().BeEmpty();
        page.Next.Should().BeNull();
        (await fixture.Service.ReadQuestionsAsync(fixture.Request.SessionId, null, 1, fixture.Token)).Records
            .Should().ContainSingle().Which.Should().Be(fixture.Question);
        (await fixture.Service.ReadTasksAsync(fixture.Request.SessionId, null, 1, fixture.Token)).Records
            .Should().ContainSingle().Which.Should().Be(fixture.Task);
        fixture.TaskWrites.Should().BeEmpty();
        fixture.ControlCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Privacy_closure_before_or_after_read_denies_projection(bool initial, bool revokeAfter)
    {
        using var fixture = new Fixture { CanInspect = initial, RevokeAfterRead = revokeAfter };
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var read = () => fixture.Service.ReadSessionsAsync(null, 1, fixture.Token);
        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("*private desktop*");
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.ActivatedVoice, true)]
    public async Task Explicit_control_owns_fresh_original_intent_and_commits_terminal_receipt(RequestOrigin origin, bool active)
    {
        using var fixture = new Fixture();
        var result = await fixture.Service.ChangeLifecycleAsync(fixture.Request.SessionId, new(1), active, origin, fixture.Token);
        result.Should().Be(new WorkSessionAuthorization(fixture.Request.SessionId, new(2), active));
        fixture.TaskWrites.Should().HaveCount(2);
        var intent = fixture.TaskWrites[0];
        intent.State.Should().Be(HostTaskState.IntentRecorded);
        intent.Request.Origin.Should().Be(origin);
        intent.Request.SessionId.Should().Be(fixture.Request.SessionId);
        intent.Request.TaskId.Should().NotBe(fixture.Request.TaskId);
        fixture.TaskWrites[1].State.Should().Be(HostTaskState.Succeeded);
        fixture.LastExpected.Should().Be(new HostRevision(1));
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem)]
    [InlineData((RequestOrigin)99)]
    public async Task Provider_or_system_origin_cannot_control_subject(RequestOrigin origin)
    {
        using var fixture = new Fixture();
        var act = () => fixture.Service.ChangeLifecycleAsync(fixture.Request.SessionId, new(1), false, origin, fixture.Token);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*original trusted*");
        fixture.TaskWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public async Task Host_gate_or_revision_change_denies_without_success(bool allowed, bool revoke, bool revise)
    {
        using var fixture = new Fixture { CanControl = allowed, RevokeDuringControl = revoke, ReviseDuringControl = revise };
        var act = () => fixture.Service.ChangeLifecycleAsync(fixture.Request.SessionId, new(1), false, RequestOrigin.LocalUi, fixture.Token);
        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.TaskWrites.Should().NotContain(task => task.State == HostTaskState.Succeeded);
        if (allowed) { fixture.TaskWrites.Last().State.Should().Be(HostTaskState.Denied); }
        else { fixture.TaskWrites.Should().BeEmpty(); }
    }

    [Theory]
    [InlineData("read")]
    [InlineData("read-cancel")]
    [InlineData("write")]
    [InlineData("receipt")]
    [InlineData("cancel")]
    public async Task Storage_audit_receipt_and_cancellation_failures_are_not_success_shaped(string failure)
    {
        using var fixture = new Fixture { Failure = failure };
        if (failure.StartsWith("read", StringComparison.Ordinal))
        {
            using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
            var read = () => fixture.Service.ReadTasksAsync(fixture.Request.SessionId, null, 1, fixture.Token);
            await read.Should().ThrowAsync<Exception>();
        }

        else
        {
            var control = () => fixture.Service.ChangeLifecycleAsync(fixture.Request.SessionId, new(1), false, RequestOrigin.LocalUi, fixture.Token);
            await control.Should().ThrowAsync<Exception>();
            fixture.TaskWrites.Should().NotContain(task => task.State == HostTaskState.Succeeded);
        }
    }

    [Fact]
    public async Task Failure_logging_is_structured_and_correlated_without_history_content()
    {
        using var fixture = new Fixture { Failure = "read" };
        fixture.Logger.Enabled = true;
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var read = () => fixture.Service.ReadTasksAsync(fixture.Request.SessionId, null, 1, fixture.Token);
        await read.Should().ThrowAsync<IOException>();
        fixture.Logger.Events.Should().ContainSingle().Which.Should().Be(new EventId(182, "Failure"));
        fixture.Logger.HostRequests.Should().ContainSingle().Which.Should().Be(fixture.Request);
        fixture.Logger.Messages.Should().ContainSingle().Which.Should().Be("Sessions workspace failed; exception type IOException.");
    }

    private sealed class Fixture : ISessionWorkspaceAccess, ISessionWorkspaceStore, IHostTaskStore, IDisposable
    {
        private readonly ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };

        internal Fixture()
        {
            ActivitySource.AddActivityListener(listener);
            Session = new(Request.SessionId, new(1), true);
            Task = new(Request, new(2), HostTaskState.Succeeded);
            Question = new(new(Request, new(Guid.NewGuid()), new(1)),
                new("Actual question", QuestionKind.SingleChoice, [new("yes", "Yes")]), new(1), DateTimeOffset.UtcNow);
            Service = new(this, new(this), this, Logger);
        }

        internal HostRequest Request { get; } = HostRequest.Create(RequestOrigin.LocalUi);
        internal WorkSessionAuthorization Session { get; }
        internal HostTaskRecord Task { get; }
        internal HostQuestionRecord Question { get; }
        internal SessionWorkspaceService Service { get; }
        internal RecordingLogger Logger { get; } = new();
        internal List<HostTaskRecord> TaskWrites { get; } = [];
        internal int ControlCalls { get; private set; }
        internal HostRevision LastExpected { get; private set; }
        internal bool RevokeAfterRead { get; init; }
        internal bool RevokeDuringControl { get; init; }
        internal bool ReviseDuringControl { get; init; }
        internal string? Failure { get; init; }
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        public bool CanInspect { get; set; } = true;
        public bool CanControl { get; set; } = true;
        public long ControlRevision { get; private set; } = 1;

        public async ValueTask<HostTaskRecord> RecordControlIntentAsync(HostRequest request, CancellationToken cancellationToken)
        {
            var record = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
            await CommitAsync(record, 0, cancellationToken);
            return record;
        }

        public ValueTask<SessionPage<WorkSessionAuthorization>> ReadSessionsAsync(Guid? after, int limit, CancellationToken cancellationToken)
        {
            if (RevokeAfterRead) { CanInspect = false; }
            return ValueTask.FromResult(after is null
                ? new SessionPage<WorkSessionAuthorization>([Session], Session.SessionId.Value) : new([], null));
        }

        public ValueTask<SessionPage<HostQuestionRecord>> ReadQuestionPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new SessionPage<HostQuestionRecord>([Question], null));

        public ValueTask<SessionPage<HostTaskRecord>> ReadTaskPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken cancellationToken) =>
            Failure switch
            {
                "read" => ValueTask.FromException<SessionPage<HostTaskRecord>>(new IOException("private reader unavailable")),
                "read-cancel" => ValueTask.FromException<SessionPage<HostTaskRecord>>(new OperationCanceledException("cancelled")),
                _ => ValueTask.FromResult(new SessionPage<HostTaskRecord>([Task], null)),
            };

        public ValueTask<WorkSessionAuthorization> ChangeIdleLifecycleAsync(HostRequest request, HostRevision expectedGeneration,
            bool active, Func<bool> canControl, CancellationToken cancellationToken)
        {
            ControlCalls++;
            LastExpected = expectedGeneration;
            HostActivity.RequireCurrent().Request.Should().Be(request);
            if (RevokeDuringControl) { CanControl = false; }
            if (ReviseDuringControl) { ControlRevision++; }
            if (!canControl()) { throw new InvalidOperationException("changed gate"); }
            if (string.Equals(Failure, "write", StringComparison.Ordinal)) { throw new IOException("audit failed"); }
            if (string.Equals(Failure, "cancel", StringComparison.Ordinal)) { throw new OperationCanceledException("cancelled"); }
            return ValueTask.FromResult(new WorkSessionAuthorization(request.SessionId, new(2), active));
        }

        public ValueTask CommitAsync(HostTaskRecord record, long expectedRevision, CancellationToken cancellationToken)
        {
            if (string.Equals(Failure, "receipt", StringComparison.Ordinal) && record.IsTerminal)
            {
                throw new IOException("receipt failed");
            }
            TaskWrites.Add(record);
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<HostTaskRecord>> ReadIncompleteAsync(int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Dispose() => listener.Dispose();
    }

    private sealed class RecordingLogger : ILogger<SessionWorkspaceService>
    {
        internal bool Enabled { get; set; }
        internal List<EventId> Events { get; } = [];
        internal List<HostRequest> HostRequests { get; } = [];
        internal List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => Enabled;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Events.Add(eventId);
            HostRequests.Add(HostActivity.RequireCurrent().Request);
            Messages.Add(formatter(state, exception));
        }
    }
}

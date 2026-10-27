using System.Collections.Immutable;
using System.Globalization;

using Avalonia.Controls;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class AllWorkWaitingQuestionNavigationTests
{
    private const string Phrase = "show sessions needing an answer";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OriginalDispatcherCompletesRealReadAndDisplaysActualWaitingGroups(bool voice, bool alreadyOpen)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new Fixture(voice);
            await f.Initialize();
            if (alreadyOpen)
            {
                f.All.Open();
                f.Model.Filter = SessionListFilter.Done;
                f.Model.QueryKind = SessionListSearchKind.ExactId;
                f.Model.QueryText = Guid.NewGuid().ToString("D");
                f.Model.TaskState = HostTaskState.Succeeded;
                f.Store.MetadataReads.Should().Be(0);
            }
            var before = await f.Native.Interactions.ReadMetadataPageAsync(null, 25, f.Native.Token);
            if (voice)
            {
                f.Native.Main.CommandText = "set assistant name to Nova";
                await f.Native.Main.RunTypedCommand.ExecuteAsync();
            }
            await f.Run(voice);
            var row = f.Model.Records.Should().ContainSingle().Subject;
            row.Session.Should().Be(f.Store.Entries[0]);
            row.Work!.PendingQuestions.Should().HaveCount(1);
            f.Model.Filter.Should().Be(SessionListFilter.All);
            f.Model.NeedsAnswer.Should().BeTrue();
            f.Model.QueryText.Should().BeEmpty();
            f.Model.TaskState.Should().BeNull();
            f.Model.SelectedRecord.Should().BeNull();
            f.Model.Status.Should().Contain("Needs answer only").And.Contain(SessionWorkOverviewPage.Scope);
            f.Window!.FindControl<ListBox>("Records")!.ItemsSource.Should().BeSameAs(f.Model.Records);
            f.Store.MetadataReads.Should().Be(1);
            f.Store.WorkReads.Should().Be(2);
            f.Store.Origins.Should().OnlyContain(origin => origin == (voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi));
            f.Store.Requests[0].Should().Be(f.Navigation!.Request);
            f.Store.Requests.Skip(1).Select(request => request.SessionId).Should()
                .Equal(f.Store.Entries.Select(entry => entry.Authority.SessionId));
            (await f.All.WaitingQuestionOperation).Should().Be(WorkspaceSurfaceNavigationOutcome.Completed);
            f.Navigation!.Outcome.Should().Be(WorkspaceSurfaceNavigationOutcome.Completed);
            f.Native.Voice.ReplyCalls.Should().Be(0);
            f.Store.ForbiddenCalls.Should().Be(0);
            (await f.Native.Interactions.ReadMetadataPageAsync(null, 25, f.Native.Token)).Records.Should().Equal(before.Records);
            f.Model.Select(row);
            f.Model.EditFind("Observed");
            f.Native.Main.CommandText = "replacement";
            f.Model.Records.Should().BeEmpty();
            f.Model.SelectedRecord.Should().BeNull();
            f.Model.SelectedDetails.Should().BeEmpty();
            f.Model.CanFind.Should().BeFalse();
            f.Model.CanNext.Should().BeFalse();
        });
    }

    public static IEnumerable<TheoryDataRow<string, string, bool>> Withdrawals()
    {
        foreach (var layer in new[] { "metadata", "work" })
        {
            foreach (var change in new[] { "input", "owner", "call", "name", "filter", "source", "close", "dispose", "cancel", "order" })
            {
                yield return new(layer, change, false);
                yield return new(layer, change, true);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Withdrawals))]
    public async Task WithdrawalClearsAuthorityButJoinsOriginalIgnoringReader(string layer, string change, bool lateFailure)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new Fixture();
            await f.Initialize();
            f.Store.HoldLayer = layer;
            f.Store.LateFailure = lateFailure;
            f.Native.Main.CommandText = Phrase;
            var command = f.Native.Main.RunTypedCommand.ExecuteAsync();
            await f.Store.Held.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Native.Token);
            var model = f.Model;
            var originalRead = model.WaitingQuestionQuiescence;
            Task? disposal = null;
            switch (change)
            {
                case "input": f.Native.Main.CommandText = "different input"; break;
                case "owner": f.Native.Access.SetOpen(false); f.Native.Access.SetOpen(true); break;
                case "call": f.Call.Set(CallState.Active); await Dispatcher.UIThread.InvokeAsync(() => { }); break;
                case "name":
                    f.Native.Main.CommandText = "set assistant name to Nova";
                    await f.Native.Main.RunTypedCommand.ExecuteAsync();
                    break;
                case "filter": model.NeedsAnswer = false; break;
                case "source": model.Revoke(); break;
                case "close": f.Window!.Close(); break;
                case "dispose": disposal = f.All.DisposeAsync().AsTask(); break;
                case "cancel": model.Cancel(); break;
                case "order": model.Order = SessionWorkOverviewPageOrder.SessionIdDescending; break;
            }
            await f.Store.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Native.Token);
            model.Records.Should().BeEmpty();
            model.CanFind.Should().BeFalse();
            model.SelectedRecord.Should().BeNull();
            command.IsCompleted.Should().BeFalse();
            originalRead.IsCompleted.Should().BeFalse("the original task owns the physical read, not only its cancellable waiter");
            f.All.WaitingQuestionOperation.IsCompleted.Should().BeFalse();
            if (disposal is not null) { disposal.IsCompleted.Should().BeFalse(); }
            f.Store.Release.TrySetResult();
            await command;
            if (disposal is not null) { await disposal; }
            f.Store.Joined.Task.IsCompletedSuccessfully.Should().BeTrue();
            (await f.All.WaitingQuestionOperation).Should().Be(WorkspaceSurfaceNavigationOutcome.Cancelled);
            var terminal = await Record.ExceptionAsync(() => originalRead);
            terminal.Should().BeOfType<OperationCanceledException>();
            if (lateFailure) { terminal!.InnerException.Should().BeSameAs(f.Store.Failure); }
            model.Records.Should().BeEmpty();
            model.SelectedDetails.Should().BeEmpty();
            f.Store.MetadataReads.Should().Be(1);
            f.Store.WorkReads.Should().Be(layer is "metadata" ? 0 : 1);
            f.Store.ForbiddenCalls.Should().Be(0);
        });
    }

    [Theory]
    [InlineData("zero-next")]
    [InlineData("omitted")]
    [InlineData("unavailable")]
    [InlineData("output-bound")]
    [InlineData("metadata-failure")]
    public async Task VisiblePartialAndFailedSourcesNeverBecomeNobodyWaiting(string scenario)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new Fixture();
            await f.Initialize();
            f.Store.Scenario = scenario;
            if (scenario is "zero-next") { f.Store.Entries = [.. Enumerable.Range(1, 11).Select(Entry)]; }
            await f.Run(false);
            if (scenario is "metadata-failure")
            {
                f.Model.Records.Should().BeEmpty();
                (await f.All.WaitingQuestionOperation).Should().Be(WorkspaceSurfaceNavigationOutcome.Unavailable);
                f.Native.Main.ResponseBody.Should().Contain("no fresh bounded observation completed");
            }

            else
            {
                (await f.All.WaitingQuestionOperation).Should().Be(WorkspaceSurfaceNavigationOutcome.Completed);
                f.Model.Status.Should().Contain("No full-scope totals or all-clear");
                if (scenario is "zero-next")
                {
                    f.Model.Records.Should().BeEmpty();
                    f.Model.CanNext.Should().BeTrue();
                    f.Model.Status.Should().Contain("Next continues even with zero matches");
                    await f.Model.RefreshAsync(next: true);
                    f.Model.Records.Should().ContainSingle().Which.Session.Should().Be(f.Store.Entries[10]);
                    f.Model.CanNext.Should().BeFalse();
                }
                if (scenario is "omitted")
                {
                    f.Model.Records.Should().HaveCount(2);
                    f.Model.Status.Should().Contain("14 pending-question records omitted");
                }
                if (scenario is "unavailable" or "output-bound")
                {
                    f.Model.Records.Should().HaveCount(2).And.OnlyContain(row =>
                        row.Availability == (string.Equals(scenario, "unavailable", StringComparison.Ordinal)
                            ? SessionWorkOverviewAvailability.ReadUnavailable : SessionWorkOverviewAvailability.OutputBound));
                }
            }
            f.Store.ForbiddenCalls.Should().Be(0);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalDeadlineExpiresOnceButCannotDetachAnIgnoringReader(bool lateFailure)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new Fixture();
            await f.Initialize();
            f.Store.HoldLayer = "metadata";
            f.Store.LateFailure = lateFailure;
            f.Native.Main.CommandText = Phrase;
            var command = f.Native.Main.RunTypedCommand.ExecuteAsync();
            await f.Store.Held.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Native.Token);
            var original = f.Model.WaitingQuestionQuiescence;
            f.Clock.Fire();
            await f.Store.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Native.Token);
            f.Clock.Created.Should().Be(1);
            original.IsCompleted.Should().BeFalse();
            command.IsCompleted.Should().BeFalse();
            f.Model.Records.Should().BeEmpty();
            f.Model.SelectedRecord.Should().BeNull();
            f.Model.CanFind.Should().BeFalse();
            f.Store.Release.TrySetResult();
            await command;
            var failure = await Record.ExceptionAsync(() => original);
            failure.Should().BeOfType<TimeoutException>();
            if (lateFailure) { failure!.InnerException.Should().BeSameAs(f.Store.Failure); }
            (await f.All.WaitingQuestionOperation).Should().Be(WorkspaceSurfaceNavigationOutcome.Unavailable);
            f.Clock.Created.Should().Be(1);
            f.Store.MetadataReads.Should().Be(1);
            f.Store.WorkReads.Should().Be(0);
            f.Store.ForbiddenCalls.Should().Be(0);
        });
    }

    [WindowsFact]
    public async Task OldOpeningIsReadFreeAndBusyClosedDisposedOrMissingPresenterCannotClaimARead()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new Fixture();
            await f.Initialize();
            await f.RunTyped("open all work");
            f.Store.MetadataReads.Should().Be(0);
            var original = f.Model;
            f.Store.HoldLayer = "metadata";
            var manual = original.RefreshAsync();
            await f.Store.Held.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Native.Token);
            await f.RunTyped(Phrase);
            f.Navigation!.Outcome.Should().Be(WorkspaceSurfaceNavigationOutcome.Unavailable);
            f.Store.MetadataReads.Should().Be(1);
            f.Store.Release.TrySetResult();
            await manual;
            original.Records.Should().HaveCount(2, "ordinary native Refresh remains unfiltered");
            f.Store.HoldLayer = null;
            original.Close();
            await f.RunTyped(Phrase);
            f.Navigation!.Outcome.Should().Be(WorkspaceSurfaceNavigationOutcome.Unavailable);
            f.Store.MetadataReads.Should().Be(1);
            f.Window!.Close();
            await f.Run(false);
            f.Model.Should().NotBeSameAs(original);
            f.Model.Records.Should().ContainSingle();
            await f.All.DisposeAsync();
            await f.RunTyped(Phrase);
            f.Navigation!.Outcome.Should().Be(WorkspaceSurfaceNavigationOutcome.Unavailable);
            f.Commands.Dispose();
            await f.RunTyped(Phrase);
            f.Native.Main.ResponseBody.Should().Contain("no fresh bounded observation completed");
            f.Store.ForbiddenCalls.Should().Be(0);
        });
    }

    [Theory]
    [InlineData("input")]
    [InlineData("owner")]
    [InlineData("dispose")]
    public async Task QueuedCommandCannotAcquireLaterAuthority(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new Fixture();
            await f.Initialize();
            f.Native.Main.CommandText = Phrase;
            var command = f.Native.Main.RunTypedCommand.ExecuteAsync();
            command.IsCompleted.Should().BeFalse();
            if (change is "input") { f.Native.Main.CommandText = "replacement"; }
            if (change is "owner") { f.Native.Access.SetOpen(false); f.Native.Access.SetOpen(true); }
            if (change is "dispose") { f.Commands.Dispose(); }
            await command;
            f.Store.MetadataReads.Should().Be(0);
            f.Navigation!.Outcome.Should().Be(WorkspaceSurfaceNavigationOutcome.Cancelled);
        });
    }

    private static SessionWorkspaceEntry Entry(int id)
    {
        var session = new HostId<SessionIdentity>(Guid.Parse("00000000-0000-0000-0000-" + id.ToString("D12", CultureInfo.InvariantCulture)));
        return new(new(session, new(1), true), new(session, new(1), new("Waiting group " + id.ToString(CultureInfo.InvariantCulture))));
    }

    private sealed class Fixture(bool voice = false) : IAsyncDisposable
    {
        internal NativeUxFixtureSession Native { get; } = new(Path.GetFullPath("."), syntheticVoice: voice);
        internal OverviewStore Store { get; } = new();
        internal MutableCallState Call { get; } = new();
        internal OverviewClock Clock { get; } = new();
        internal AllWorkWindowController All { get; private set; } = null!;
        internal WorkspaceSurfaceCommandController Commands { get; private set; } = null!;
        internal AllWorkWindow? Window { get; private set; }
        internal AllWorkViewModel Model => (AllWorkViewModel)Window!.DataContext!;
        internal WorkspaceSurfaceNavigation? Navigation { get; private set; }
        private SessionsWindowController? sessions;
        private IDisposable? opened;

        internal async Task Initialize()
        {
            await Native.InitializeAsync(null, callState: Call);
            if (voice)
            {
                Native.Main.BindVoiceOwnershipGate(static () => true);
                await Native.Main.SetVoiceConsentAsync(true);
                await Native.Main.BeginPushToTalkAsync();
                await Native.Main.EndPushToTalkAsync();
            }
            var service = new SessionWorkspaceService(Store, new(Native.Tasks), Native.Access,
                NullLogger<SessionWorkspaceService>.Instance, overviewTime: Clock);
            sessions = new(Native.Main, service, Native.Evidence, Native.Access, NullLogger<SessionsViewModel>.Instance);
            sessions.Bind();
            All = new(Native.Main, service, Native.Access, NullLogger<AllWorkViewModel>.Instance, sessions);
            All.Bind();
            Native.Main.WorkspaceSurfaceRequested += request => { Navigation = request; return Task.CompletedTask; };
            Commands = new(Native.Main, Native.Access, All, sessions);
            Commands.Bind();
            opened = Avalonia.Controls.Window.WindowOpenedEvent.AddClassHandler<AllWorkWindow>((window, _) => Window = window);
        }

        internal async Task Run(bool activatedVoice)
        {
            if (!activatedVoice) { await RunTyped(Phrase); return; }
            if (!Native.Main.IsVoiceEnabled) { await Native.Main.ToggleListeningCommand.ExecuteAsync(); }
            var captures = Native.Voice.StartCalls;
            await Native.Main.BeginPushToTalkAsync();
            await Native.Main.EndPushToTalkAsync();
            Native.Voice.StartCalls.Should().BeGreaterThan(captures, Native.Main.ResponseBody);
            Native.Voice.PublishTranscript(Native.Main.AssistantName + ", " + Phrase);
            await Dispatcher.UIThread.InvokeAsync(() => { });
            Navigation.Should().NotBeNull(Native.Main.ResponseBody);
            await Store.MetadataEntered.Task.WaitAsync(TimeSpan.FromSeconds(20), Native.Token);
            await All.WaitingQuestionOperation;
            await Dispatcher.UIThread.InvokeAsync(() => { });
        }

        internal async Task RunTyped(string phrase)
        {
            Native.Main.CommandText = phrase;
            await Native.Main.RunTypedCommand.ExecuteAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Store.Release.TrySetResult();
            Commands?.Dispose();
            if (All is not null) { await All.DisposeAsync(); }
            sessions?.Dispose();
            opened?.Dispose();
            await Native.Main.ExitAsync();
            Native.Dispose();
        }
    }

    private sealed class MutableCallState : ICallStateService
    {
        public CallState CurrentState { get; private set; } = CallState.Clear;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
        internal void Set(CallState state) { CurrentState = state; StateChanged?.Invoke(this, new(state)); }
    }

    private sealed class OverviewClock : TimeProvider
    {
        private Action? expire;
        internal int Created { get; private set; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            dueTime.Should().Be(SessionWorkspaceService.WorkOverviewTimeout);
            period.Should().Be(Timeout.InfiniteTimeSpan);
            Created++;
            expire = () => callback(state);
            return new DeadlineTimer();
        }
        internal void Fire() => expire!();
        private sealed class DeadlineTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new InvalidOperationException("The original deadline must never renew.");
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class OverviewStore : ISessionWorkspaceStore, ISessionWorkStore
    {
        internal ImmutableArray<SessionWorkspaceEntry> Entries { get; set; } = [Entry(1), Entry(2)];
        internal string? Scenario { get; set; }
        internal string? HoldLayer { get; set; }
        internal bool LateFailure { get; set; }
        internal Exception Failure { get; } = new NotSupportedException("Late unexpected store failure.");
        internal TaskCompletionSource Held { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Joined { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource MetadataEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int MetadataReads { get; private set; }
        internal int WorkReads { get; private set; }
        internal int ForbiddenCalls { get; private set; }
        internal List<RequestOrigin> Origins { get; } = [];
        internal List<HostRequest> Requests { get; } = [];

        private async Task Hold(string layer, CancellationToken token)
        {
            if (!string.Equals(HoldLayer, layer, StringComparison.Ordinal)) { return; }
            using var registration = token.Register(() => Cancelled.TrySetResult());
            Held.TrySetResult();
            try
            {
                await Release.Task;
                if (LateFailure) { throw Failure; }
            }
            finally { Joined.TrySetResult(); }
        }

        private void Observe()
        {
            var request = HostActivity.RequireCurrent().Request;
            Requests.Add(request);
            Origins.Add(request.Origin);
        }

        public async ValueTask<SessionPage<SessionWorkspaceEntry>> ReadMetadataPageAsync(Guid? after, int limit, CancellationToken token)
        {
            Observe();
            limit.Should().Be(SessionWorkOverviewPage.MaximumSessions);
            MetadataReads++;
            MetadataEntered.TrySetResult();
            await Hold("metadata", token);
            if (Scenario is "metadata-failure") { throw new IOException("Metadata unavailable."); }
            var rows = Entries.Where(entry => string.CompareOrdinal(entry.Authority.SessionId.Value.ToString("D"),
                after?.ToString("D") ?? string.Empty) > 0).Take(limit + 1).ToArray();
            return new([.. rows.Take(limit)], rows.Length > limit ? rows[limit - 1].Authority.SessionId.Value : null);
        }

        public async ValueTask<SessionWorkSnapshot> ReadWorkAsync(HostId<SessionIdentity> session, long revision, SessionQueueLimits limits, CancellationToken token)
        {
            Observe();
            WorkReads++;
            await Hold("work", token);
            if (Scenario is "unavailable") { throw new IOException("Work unavailable."); }
            var entry = Entries.Single(row => row.Authority.SessionId == session);
            var request = new HostRequest(new(Guid.NewGuid()), session, new(Guid.NewGuid()), RequestOrigin.LocalUi);
            var questions = Scenario is "output-bound" ? 50 : Scenario is "zero-next"
                ? (entry == Entries[^1] ? 1 : 0) : Scenario is "omitted" ? 0 : entry == Entries[0] ? 1 : 0;
            var task = new HostTaskObservation(new(request, new(1), HostTaskState.Unknown), entry.Authority.Generation, "body excluded", false, null);
            var now = DateTimeOffset.Parse("2026-10-10T00:00:00Z", CultureInfo.InvariantCulture);
            var queueEntry = new SessionQueueEntry(request, new(1), new(1), 1, SessionQueueState.Pending,
                Guid.NewGuid(), 1, now, now.AddMinutes(30));
            return new(entry, 41, DateTimeOffset.UtcNow, new(session, entry.Authority.Generation, 1, []),
                0, 8, 2, Scenario is "output-bound"
                    ? [.. Enumerable.Repeat(new SessionQueueObservation(queueEntry, SessionQueueEligibility.Ready, null), 50)] : [],
                0, Scenario is "output-bound" ? [.. Enumerable.Repeat(task, 50)] : [], 0,
                [.. Enumerable.Range(0, questions).Select(_ => new SessionWorkQuestion(new(request, new(Guid.NewGuid()), new(1)),
                    entry.Authority.Generation, QuestionStatus.Pending, QuestionKind.SingleChoice, RequestOrigin.LocalUi, DateTimeOffset.MaxValue))],
                Scenario is "omitted" ? 7 : 0);
        }

        private T Forbidden<T>() { ForbiddenCalls++; throw new InvalidOperationException("Passive observation cannot perform this operation."); }
        public ValueTask<SessionDispositionPreview> PreviewDispositionAsync(HostId<SessionIdentity> session, HostRevision generation, long revision, CancellationToken token) => Forbidden<ValueTask<SessionDispositionPreview>>();
        public ValueTask<SessionDispositionReceipt> DisposeSessionAsync(HostRequest request, SessionDispositionPreview preview, Func<bool> admitted, CancellationToken token) => Forbidden<ValueTask<SessionDispositionReceipt>>();
        public ValueTask<HostTaskRecord> RecordControlIntentAsync(HostRequest request, CancellationToken token) => Forbidden<ValueTask<HostTaskRecord>>();
        public ValueTask<SessionPage<WorkSessionAuthorization>> ReadSessionsAsync(Guid? after, int limit, CancellationToken token) => Forbidden<ValueTask<SessionPage<WorkSessionAuthorization>>>();
        public ValueTask<SessionWorkspaceEntry> ReadMetadataAsync(HostId<SessionIdentity> session, CancellationToken token) => Forbidden<ValueTask<SessionWorkspaceEntry>>();
        public ValueTask<HostTaskObservation?> ReadTaskAsync(HostId<SessionIdentity> session, HostId<TaskIdentity> task, CancellationToken token) => Forbidden<ValueTask<HostTaskObservation?>>();
        public ValueTask<HostTaskObservation> CancelWaitingTaskAsync(HostRequest control, HostTaskCancellationTarget target, Func<bool> admitted, CancellationToken token) => Forbidden<ValueTask<HostTaskObservation>>();
        public ValueTask<SessionWorkspaceEntry> CreateNamedSessionAsync(HostRequest request, SessionName name, Func<bool> admitted, CancellationToken token) => Forbidden<ValueTask<SessionWorkspaceEntry>>();
        public ValueTask<SessionWorkspaceEntry> RenameSessionAsync(HostRequest request, HostRevision generation, long revision, SessionName name, Func<bool> admitted, CancellationToken token) => Forbidden<ValueTask<SessionWorkspaceEntry>>();
        public ValueTask<SessionPage<HostQuestionRecord>> ReadQuestionPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken token) => Forbidden<ValueTask<SessionPage<HostQuestionRecord>>>();
        public ValueTask<SessionPage<HostTaskRecord>> ReadTaskPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken token) => Forbidden<ValueTask<SessionPage<HostTaskRecord>>>();
        public ValueTask<WorkSessionAuthorization> ChangeIdleLifecycleAsync(HostRequest request, HostRevision generation, bool active, Func<bool> admitted, CancellationToken token) => Forbidden<ValueTask<WorkSessionAuthorization>>();
    }
}

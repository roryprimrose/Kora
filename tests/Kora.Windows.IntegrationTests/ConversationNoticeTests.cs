using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application;
using Kora.Application.Diagnostics;
using Kora.Application.Interaction;
using Kora.Application.Maintenance;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Maintenance;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed partial class ConversationNoticeTests
{
    [Theory]
    [InlineData(ModelTurnOutcome.Succeeded, LocalEventType.Completed)]
    [InlineData(ModelTurnOutcome.Unavailable, LocalEventType.Failed)]
    [InlineData(ModelTurnOutcome.Denied, LocalEventType.Failed)]
    [InlineData(ModelTurnOutcome.Unknown, LocalEventType.Unknown)]
    [InlineData(ModelTurnOutcome.Cancelled, LocalEventType.Unknown)]
    [InlineData(ModelTurnOutcome.TimedOut, LocalEventType.Unknown)]
    public async Task RealReaderProjectsOnlyExactAdmittedTerminalsWithoutContentOrActivity(
        ModelTurnOutcome outcome, LocalEventType expected)
    {
        using var f = new Fixture();
        await f.Initialize();
        var request = await f.Submit(outcome);
        var before = await f.Storage.Store.ReadRetentionAsync(request.SessionId, f.Token);
        var count = f.Storage.Count("host_tasks");
        var audits = f.Storage.Count("security_audit_events");
        var work = await f.Storage.Store.ReadWorkAsync(request.SessionId, f.Access.ControlRevision, new(), f.Token);
        var task = work.Tasks.Single(item => item.Task.Request == request);
        task.CurrentSource.Should().BeFalse("execution authority must not be broadened for notices");
        task.Question.Should().BeNull();
        task.ConversationNotice.Should().NotBeNull();
        JsonSerializer.Serialize(work).Should().NotContain("private original marker").And.NotContain("private answer marker");
        await using var broker = f.Broker();
        var observed = await broker.ObserveAsync(request.SessionId, () => true, f.Token);
        observed.Events.Single().Event.Type.Should().Be(expected);
        observed.Events.Single().Event.TaskId.Should().Be(request.TaskId);
        var again = await broker.ObserveAsync(request.SessionId, () => true, f.Token);
        again.Events.Single().Reason.Should().Be(LocalEventReason.PresentedNoReplay);
        (await f.Storage.Store.ReadRetentionAsync(request.SessionId, f.Token)).Should().Be(before);
        f.Storage.Count("host_tasks").Should().Be(count);
        f.Storage.Count("security_audit_events").Should().Be(audits);
        f.Storage.Reopen();
        await f.Storage.Store.InitializeAsync(f.Token);
        var restarted = await f.Storage.Store.ReadWorkAsync(request.SessionId, f.Access.ControlRevision, new(), f.Token);
        restarted.Tasks.Single(item => item.Task.Request == request).ConversationNotice.Should().BeNull();
    }

    [WindowsFact]
    public async Task NativeInspectDismissDeferNeverAnswersRetargetsFocusesDispatchesOrRenewsActivity()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            var request = await f.Submit(null);
            var question = await f.Clarify(request);
            await using var broker = f.Broker();
            var viewer = f.Viewer(broker);
            await viewer.RefreshAsync();
            await viewer.SelectAsync(viewer.Sessions.Single());
            var window = new SessionsWindow(viewer);
            window.Show();
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                var refresh = window.FindControl<Button>("ReadQueue")!;
                refresh.Focus().Should().BeTrue();
                await viewer.RefreshWorkAsync();
                window.FocusManager!.GetFocusedElement().Should().BeSameAs(refresh);
                var focusChanges = 0;
                var activations = 0;
                window.GotFocus += (_, _) => focusChanges++;
                window.Activated += (_, _) => activations++;
                var target = viewer.LocalEvents.Single();
                target.Event.Source.Should().Be(LocalEventSource.ConversationQuestion);
                var workSelection = viewer.SelectedWorkRecord;
                var selected = viewer.SelectedSessionRecord;
                var before = await f.Storage.Store.ReadRetentionAsync(request.SessionId, f.Token);
                var tasks = f.Storage.Count("host_tasks");
                var audits = f.Storage.Count("security_audit_events");
                viewer.SelectLocalEvent(target);
                await viewer.ReviewLocalEventAsync();
                viewer.Detail.Should().Contain(request.TaskId.Value.ToString("D")).And.Contain("Inspection only")
                    .And.NotContain("private original marker").And.NotContain("private question marker");
                await viewer.DeferLocalEventAsync();
                viewer.SelectedLocalEvent!.DeferredUntil.Should().Be(question.ExpiresAt);
                await viewer.DismissLocalEventAsync();
                viewer.SelectedLocalEvent!.Reason.Should().Be(LocalEventReason.Dismissed);
                viewer.PendingQuestions.Single().Key.Should().Be(question.Key);
                (await f.Storage.Store.ReadQuestionsAsync(request.SessionId, f.Token)).Single().Status.Should().Be(QuestionStatus.Pending);
                viewer.SelectedWorkRecord.Should().BeSameAs(workSelection);
                viewer.SelectedSessionRecord.Should().Be(selected);
                focusChanges.Should().Be(0, "notice actions never request focus, even when a busy-bound control loses it");
                activations.Should().Be(0, "notice actions never activate their surface");
                f.Storage.Count("host_tasks").Should().Be(tasks);
                f.Storage.Count("security_audit_events").Should().Be(audits);
                (await f.Storage.Store.ReadRetentionAsync(request.SessionId, f.Token)).Should().Be(before);
                f.Access.ControlRevision++;
                f.Access.CanControl = false;
                viewer.LocalEvents.Should().BeEmpty();
                f.Access.CanControl = true;
                await viewer.RefreshWorkAsync();
                viewer.LocalEvents.Should().BeEmpty("call/privacy reopen must not replay the old source");
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("plain-text")]
    [InlineData("wrong-source")]
    [InlineData("wrong-purpose")]
    [InlineData("wrong-options")]
    [InlineData("approval")]
    [InlineData("expired")]
    [InlineData("answered")]
    [InlineData("generation")]
    [InlineData("run")]
    public async Task ReaderRejectsUnadmittedQuestionAndRetiredLineage(string fault)
    {
        using var f = new Fixture();
        await f.Initialize();
        var request = await f.Submit(null);
        if (fault is not "plain-text")
        {
            var question = await f.Clarify(request, fault);
            if (fault is "expired") { f.Storage.Time.Now = question.ExpiresAt; }
            if (fault is "answered")
            {
                using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
                await f.Storage.Questions.SubmitAsync(question.Key, new(["option-0"]), RequestOrigin.LocalUi, f.Token);
            }
        }
        if (fault is "run")
        {
            f.Storage.Reopen();
            await f.Storage.Store.InitializeAsync(f.Token);
        }
        if (fault is "generation")
        {
            f.Storage.Mutate("UPDATE work_sessions SET generation=2;");
            var corrupt = () => f.Storage.Store.ReadWorkAsync(request.SessionId, f.Access.ControlRevision, new(), f.Token).AsTask();
            await corrupt.Should().ThrowAsync<InvalidDataException>();
            return;
        }
        var work = await f.Storage.Store.ReadWorkAsync(request.SessionId, f.Access.ControlRevision, new(), f.Token);
        work.Tasks.Single(item => item.Task.Request == request).ConversationNotice.Should().BeNull();
    }

    [Theory]
    [InlineData("task")]
    [InlineData("message")]
    [InlineData("missing")]
    [InlineData("run-missing")]
    public async Task CorruptOrMissingAuthorityNeverManufacturesSuccessfulNotice(string fault)
    {
        using var f = new Fixture();
        await f.Initialize();
        var request = await f.Submit(ModelTurnOutcome.Succeeded);
        f.Storage.Mutate(fault switch
        {
            "task" => "UPDATE host_tasks SET state=7 WHERE state=2;",
            "message" => "UPDATE conversation_messages SET payload=json_set(payload,'$.Revision',4);",
            "missing" => "DELETE FROM conversation_messages;",
            _ => "DELETE FROM host_task_runs WHERE task_id IN (SELECT task_id FROM host_tasks WHERE state=2);",
        });
        var read = () => f.Storage.Store.ReadWorkAsync(request.SessionId, f.Access.ControlRevision, new(), f.Token).AsTask();
        if (fault is "run-missing")
        {
            (await read()).Tasks.Single(item => item.Task.Request == request).ConversationNotice.Should().BeNull();
        }
        else { await read.Should().ThrowAsync<InvalidDataException>(); }
    }

    [Fact]
    public async Task DoneResumeAndDeletionRetireConversationSourcesAndSuppressionWithoutReplay()
    {
        using var f = new Fixture();
        await f.Initialize();
        var request = await f.Submit(ModelTurnOutcome.Succeeded);
        await using var broker = f.Broker();
        var service = WindowsSqliteSessionWorkspaceTests.Service(f.Storage, f.Access);
        service.BindLocalEvents(broker);
        (await broker.ObserveAsync(request.SessionId, () => true, f.Token)).Events.Should().ContainSingle();
        var done = await service.ChangeLifecycleAsync(request.SessionId, new(1), false, RequestOrigin.LocalUi, f.Token);
        (await broker.ObserveAsync(request.SessionId, () => true, f.Token)).Events.Should().BeEmpty();
        var resumed = await service.ChangeLifecycleAsync(request.SessionId, done.Generation, true, RequestOrigin.LocalUi, f.Token);
        (await f.Storage.Store.ReadWorkAsync(request.SessionId, f.Access.ControlRevision, new(), f.Token))
            .Tasks.Single(item => item.Task.Request == request).ConversationNotice.Should().BeNull();
        (await broker.ObserveAsync(request.SessionId, () => true, f.Token)).Events.Should().BeEmpty();
        var preview = await f.Storage.Store.PreviewDispositionAsync(request.SessionId, resumed.Generation, 0, f.Token);
        await broker.WithRetirementAsync(async retire =>
        {
            await retire(request.SessionId, f.Token);
            var control = new HostRequest(new(Guid.NewGuid()), request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
            using var root = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Request);
            await f.Storage.Store.RecordControlIntentAsync(control, f.Token);
            return await f.Storage.Store.DisposeSessionAsync(control, preview, () => true, f.Token);
        }, f.Token);
        var deleted = () => broker.ObserveAsync(request.SessionId, () => true, f.Token);
        await deleted.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UnadmittedOriginalSubmissionCannotManufactureTerminalNotice()
    {
        using var f = new Fixture();
        await f.Initialize();
        var request = new HostRequest(new(Guid.NewGuid()), f.Storage.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        using (var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Storage.Store.RetainConversationMessageAsync(request, new(1), 0, "completed: forged model event", () => true, f.Token);
            await f.Storage.Store.CompleteConversationSubmissionAsync(request, ModelTurnOutcome.Unavailable,
                ModelTurnReason.UnregisteredModel, () => true, f.Token);
        }
        await using var broker = f.Broker();
        (await broker.ObserveAsync(request.SessionId, () => true, f.Token)).Events.Should().BeEmpty();
    }

    private sealed class Fixture : IReleaseMetadataClient, ICanonicalReleasePageOpener, IUiDispatcher, ISecurityAuditLog, IDisposable
    {
        internal InteractionStorageFixture Storage { get; } = new();
        internal WindowsSqliteSessionWorkspaceTests.Access Access { get; } = new();
        private readonly MaintenanceViewModel maintenance;
        private readonly LocalEventStateStore state;
        internal Fixture()
        {
            maintenance = new(this, new AssemblyApplicationInfo(() => "1.0.0"), ReleaseArchitecture.X64,
                this, Storage.Time, this, this, NullLogger<MaintenanceViewModel>.Instance);
            maintenance.BindGate(() => Access.CanControl);
            state = new(Storage.Paths);
            Directory.CreateDirectory(Path.Combine(Storage.Paths.LocalRoot, "Preferences"));
        }
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        internal async Task Initialize()
        {
            await Storage.InitializeAsync();
            await WindowsSqliteSessionWorkspaceTests.FinishAsync(Storage, HostTaskState.Succeeded);
        }
        internal async Task<HostRequest> Submit(ModelTurnOutcome? outcome, HostId<SessionIdentity>? session = null,
            string answer = "private answer marker")
        {
            var request = new HostRequest(new(Guid.NewGuid()), session ?? Storage.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
            var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
            using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            var metadata = await Storage.Store.ReadMetadataAsync(request.SessionId, Token);
            var revision = metadata.Metadata?.Revision.Value ?? 0;
            await Storage.Store.RetainConversationMessageAsync(request, metadata.Authority.Generation, revision, "private original marker says completed", () => true, Token);
            await Storage.Store.BeginConversationExecutionAsync(request, provenance, revision, () => true, Token);
            if (outcome is { } terminal)
            {
                await Storage.Store.CompleteConversationExecutionAsync(request,
                    new(terminal, ModelTurnReason.ProviderOutcome, provenance,
                        terminal == ModelTurnOutcome.Succeeded ? new(answer, null) : null), () => true, Token);
            }
            return request;
        }
        internal async Task<HostQuestionRecord> Clarify(HostRequest request, string fault = "valid")
        {
            using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            await Storage.Store.PublishTrustedSnapshotAsync(request, new(true, true, false, true), null, 0, Token);
            var spec = LocalModelQuestionPolicy.CreateSpec(new("private question marker", ["Compact", "Detailed"]));
            if (fault is "wrong-source" or "wrong-purpose" or "wrong-options" or "approval")
            {
                spec = new(spec.Text, spec.Kind,
                    fault is "wrong-options" ? [new("foreign-a", "Compact"), new("foreign-b", "Detailed")] : spec.Options,
                    purpose: fault is "wrong-purpose" or "approval" ? "authorization" : spec.Purpose,
                    sourceId: fault is "wrong-source" ? "forged" : spec.SourceId);
            }
            return (await Storage.Questions.CreateAsync(request, spec, Storage.Time.Now.AddSeconds(15), Token)).Question!;
        }
        internal LocalEventBroker Broker() => new(new AuthorityLocalEventSource(Storage.Store, Access, maintenance, new(), Storage.Time),
            state, Access, Storage.Time, this, NullLogger<LocalEventBroker>.Instance);
        internal SessionsViewModel Viewer(LocalEventBroker broker,
            Func<Kora.Core.Presentation.AdmittedDetailContent, Func<bool>,
                Func<CancellationToken, Task<Kora.Core.Presentation.AdmittedDetailContent>>, CancellationToken, Task<string>>? open = null)
        {
            var sink = new WindowsSqliteEvidenceSink(Storage.Paths);
            sink.Initialize();
            return new(WindowsSqliteSessionWorkspaceTests.Service(Storage, Access),
                new(new WindowsSqliteEvidenceReader(sink), Access, Storage.Time, NullLogger<DurableEvidenceQuery>.Instance),
                Access, NullLogger<SessionsViewModel>.Instance, broker, openConversationDetail: open);
        }
        public Task<ReleaseCheck> CheckAsync(ReleaseChannel channel, string currentVersion, ReleaseArchitecture architecture, CancellationToken token) =>
            throw new InvalidOperationException("Notices cannot make network checks.");
        public Task OpenAsync(ReleaseVersion version, CancellationToken token) => throw new InvalidOperationException("Notices cannot navigate.");
        public Task InvokeAsync(Func<Task> action) => action();
        public void Post(Action action) => action();
        public void Write(SecurityAuditEvent entry) => HostActivity.RequireCurrent().CorrelationId.Should().Be(entry.CorrelationId);
        public void Dispose() { maintenance.Dispose(); Storage.Dispose(); }
    }
}

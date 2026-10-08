using System.Xml.Linq;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed class SessionsViewModelTests
{
    [WindowsFact]
    public async Task Closing_pending_passive_read_clears_late_records_and_never_changes_lifecycle_or_admits_work()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var held = new HeldStore();
        var service = new SessionWorkspaceService(held, new(fixture.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths);
        sink.Initialize();
        var evidence = new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), access, fixture.Time,
            NullLogger<DurableEvidenceQuery>.Instance);
        var viewer = new SessionsViewModel(service, evidence, access, NullLogger<SessionsViewModel>.Instance);
        var read = viewer.RefreshAsync();
        await viewer.RefreshAsync();
        held.Reads.Should().Be(1);
        viewer.Close();
        held.Completion.SetResult(new([new(new(fixture.Request.SessionId, new(1), true), null)], null));
        await read;
        viewer.Sessions.Should().BeEmpty();
        viewer.Detail.Should().BeEmpty();
        viewer.CanRead.Should().BeFalse();
        viewer.CanDone.Should().BeFalse();
        viewer.CanResume.Should().BeFalse();
        viewer.Status.Should().Contain("no late content");
        (await fixture.Tasks.ReadIncompleteAsync(10, fixture.Token)).Should().ContainSingle()
            .Which.Request.Should().Be(fixture.Request);
        held.ControlCalls.Should().Be(0);
        viewer.Close();
    }

    [WindowsFact]
    public async Task Privacy_denial_clears_old_rows_and_unaddressed_lifecycle_has_explicit_recovery()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths);
        sink.Initialize();
        var evidence = new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), access, fixture.Time,
            NullLogger<DurableEvidenceQuery>.Instance);
        var viewer = new SessionsViewModel(WindowsSqliteSessionWorkspaceTests.Service(fixture, access),
            evidence, access, NullLogger<SessionsViewModel>.Instance);
        await viewer.RefreshAsync();
        viewer.Sessions.Should().ContainSingle();
        var audits = fixture.Count("security_audit_events");
        await viewer.ChangeLifecycleAsync(active: false);
        viewer.Status.Should().Contain("Select the exact subject");
        viewer.Sessions.Should().BeEmpty();
        fixture.Count("security_audit_events").Should().Be(audits);
        await viewer.RefreshAsync();
        access.CanInspect = false;
        await viewer.RefreshAsync();
        viewer.Status.Should().Contain("unavailable");
        viewer.Sessions.Should().BeEmpty();
        viewer.Detail.Should().BeEmpty();
        viewer.Close();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("answered")]
    [InlineData("closed")]
    public async Task Native_exact_task_selection_inspection_and_separate_cancel_are_fresh_and_privacy_bound(string scenario)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var question = await WindowsSqliteTaskControlTests.WaitAsync(f);
        var access = new WindowsSqliteSessionWorkspaceTests.Access { CanControl = false };
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var viewer = new SessionsViewModel(WindowsSqliteSessionWorkspaceTests.Service(f, access),
            new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
            access, NullLogger<SessionsViewModel>.Instance);
        await viewer.RefreshAsync();
        await viewer.SelectAsync(viewer.Sessions.Single());
        var before = await f.Tasks.ReadTaskAsync(f.Request.TaskId, f.Token);
        var audits = f.Count("security_audit_events");
        viewer.SelectTask(viewer.TaskRecords.Single());
        viewer.CanInspectTask.Should().BeTrue();
        viewer.CanCancelTask.Should().BeFalse();
        f.Count("security_audit_events").Should().Be(audits);
        await viewer.InspectTaskAsync();
        viewer.Detail.Should().Contain(f.Request.TaskId.Value.ToString("D"))
            .And.Contain(LocalVersionWait.Source).And.Contain(question.Key.QuestionId.Value.ToString("D"));
        viewer.CanCancelTask.Should().BeTrue();
        (await f.Tasks.ReadTaskAsync(f.Request.TaskId, f.Token)).Should().Be(before);
        if (scenario is "answered")
        {
            await f.RunAsync(() => f.Questions.SubmitAsync(question.Key, new(["show"]), RequestOrigin.LocalUi, f.Token));
        }
        if (scenario is "closed") { viewer.Close(); }
        await viewer.CancelTaskAsync();
        var actual = (await f.Tasks.ReadTaskAsync(f.Request.TaskId, f.Token))!;
        if (scenario is "cancel")
        {
            actual.State.Should().Be(HostTaskState.Cancelled);
            viewer.Status.Should().Contain("committed atomically");
        }
        else
        {
            actual.Should().Be(before);
            viewer.Detail.Should().BeEmpty();
            viewer.CanCancelTask.Should().BeFalse();
        }
        viewer.Close();
    }

    [Fact]
    public void Native_workspace_is_themed_keyboard_accessible_passive_on_selection_and_has_no_reply_or_context_surface()
    {
        var source = Read("SessionsWindow.axaml");
        var document = XDocument.Parse(source);
        source.Should().Contain("DynamicResource").And.Contain("AutomationProperties.Name")
            .And.Contain("Mark selected ID _Done").And.Contain("Explicitly res_ume selected ID");
        document.Descendants().Should().NotContain(element =>
            element.Name.LocalName == "WebView" || element.Name.LocalName == "SelectableTextBlock");
        document.Descendants().Where(element => string.Equals(element.Name.LocalName, "TextBox", StringComparison.Ordinal))
            .Should().HaveCount(2, "only the bounded name draft and exact immutable history ID are editable; no conversation composer exists");
        source.Should().Contain("NameDraft").And.Contain("CanCreate").And.Contain("CanRename");
        source.Should().Contain("HistorySessionId").And.Contain("CanHistory").And.Contain("CanNextHistory")
            .And.Contain("Read bounded ordered session history without resuming");
        var code = Read("SessionsWindow.axaml.cs");
        code.Should().Contain("model.SelectAsync").And.Contain("Key.Escape").And.NotContain("SubmitAsync")
            .And.NotContain("ApproveAsync").And.NotContain("Clipboard").And.NotContain("Reasoner");
        Read("SessionsWindowController.cs").Should().Contain("PrivacyClosureRequested +=")
            .And.Contain("window?.Close()").And.Contain("SessionsRequested -=");
        Read("ResponseWindow.axaml.cs").Should().Contain("KeyModifiers.Control | KeyModifiers.Shift")
            .And.Contain("viewModel.ShowSessions()");
        Read("Program.cs").Should().Contain("ISessionWorkspaceStore>(interactions)");
        Read("App.axaml.cs").Should().Contain("sessionsWindow?.Dispose()");
        Read("DesktopSessionWorkspaceAccess.cs").Should().Contain("IsCapabilityAdmissionOpen").And.Contain("IsHandoffRecoveryRequired")
            .And.Contain("CanRevealPrivatePresentation").And.Contain("ControlRevision")
            .And.Contain("!main.CallObservation.IsProtected");
        Read("DesktopSessionWorkspaceAccess.cs").Should().Contain("OnPrivacyClosure").And.Contain("AdmissionRevision");
        source.Should().Contain("Enqueue local version").And.Contain("Confirm clear displayed pending queue");
        code.Should().Contain("model.ReadQueueAsync").And.Contain("model.DispatchQueueAsync");
    }

    private static string Read(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        {
            directory = directory.Parent;
        }
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "Kora", name));
    }

    private sealed class HeldStore : ISessionWorkspaceStore
    {
        public ValueTask<SessionDispositionPreview> PreviewDispositionAsync(HostId<SessionIdentity> session,
            HostRevision expectedGeneration, long expectedMetadataRevision, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<SessionDispositionReceipt> DisposeSessionAsync(HostRequest request, SessionDispositionPreview preview,
            Func<bool> canControl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<HostTaskObservation?> ReadTaskAsync(HostId<SessionIdentity> session, HostId<TaskIdentity> task, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<HostTaskObservation> CancelWaitingTaskAsync(HostRequest control, HostTaskCancellationTarget target,
            Func<bool> canControl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<SessionWorkspaceEntry> ReadMetadataAsync(HostId<SessionIdentity> session, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<HostTaskRecord> RecordControlIntentAsync(HostRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        internal TaskCompletionSource<SessionPage<SessionWorkspaceEntry>> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Reads { get; private set; }
        internal int ControlCalls { get; private set; }

        public ValueTask<SessionPage<WorkSessionAuthorization>> ReadSessionsAsync(Guid? after, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<SessionPage<SessionWorkspaceEntry>> ReadMetadataPageAsync(Guid? after, int limit, CancellationToken cancellationToken)
        {
            Reads++;
            return new(Completion.Task);
        }
        public ValueTask<SessionWorkspaceEntry> CreateNamedSessionAsync(HostRequest request, SessionName name,
            Func<bool> canControl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<SessionWorkspaceEntry> RenameSessionAsync(HostRequest request, HostRevision expectedGeneration,
            long expectedMetadataRevision, SessionName name, Func<bool> canControl, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<SessionPage<HostQuestionRecord>> ReadQuestionPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<SessionPage<HostTaskRecord>> ReadTaskPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<WorkSessionAuthorization> ChangeIdleLifecycleAsync(HostRequest request, HostRevision expectedGeneration, bool active,
            Func<bool> canControl, CancellationToken cancellationToken)
        {
            ControlCalls++;
            throw new NotSupportedException();
        }
    }
}

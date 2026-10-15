using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Interaction;
using Kora.Core.Diagnostics;
using Kora.Core.Storage;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class AllWorkWindowTests
{
    [WindowsFact]
    public async Task ExactNativeAvailabilityChoicesRevokeWithoutReadingAndRemainVolatileAccessibleAndPassive()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            var held = new GatedWorkStore(f.Store);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var service = new SessionWorkspaceService(held, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
            var model = new AllWorkViewModel(service, access, NullLogger<AllWorkViewModel>.Instance,
                (_, _, _) => throw new InvalidOperationException("Filtering grants no navigation."));
            var window = new AllWorkWindow(model);
            window.Show();
            try
            {
                var control = window.FindControl<ComboBox>("ObservationAvailability")!;
                var peer = ControlAutomationPeer.CreatePeerForElement(control)!;
                control.SelectedIndex.Should().Be(0);
                control.SelectedItem.Should().BeSameAs(model.AvailabilityChoices[0]);
                model.SelectedAvailabilityChoice.Label.Should().Be("Any");
                var selection = peer.GetProvider<ISelectionProvider>()!;
                selection.GetSelection().Should().ContainSingle().Which.GetName().Should().Be("Any");
                peer.GetName().Should().Contain("changing performs no read").And.Contain("unavailable is not empty work");
                peer.GetProvider<ISelectionProvider>().Should().NotBeNull();
                model.AvailabilityChoices.Select(choice => choice.Label).Should()
                    .Equal("Any", "Observed", "Source changed", "Read unavailable", "Output bound");
                held.Reads.Should().Be(0);
                var clock = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                var audits = f.Count("security_audit_events");
                var tasks = f.Count("host_tasks");
                await model.RefreshAsync();
                model.Select(model.Records.Single());
                var reads = held.Reads;
                control.Focus().Should().BeTrue();
                control.SelectedIndex = 1;
                model.Availability.Should().Be(SessionWorkOverviewAvailability.Observed);
                model.Records.Should().BeEmpty();
                model.SelectedRecord.Should().BeNull();
                model.CanNavigate.Should().BeFalse();
                model.CanNext.Should().BeFalse();
                held.Reads.Should().Be(reads);
                await model.RefreshAsync();
                model.Records.Single().Availability.Should().Be(SessionWorkOverviewAvailability.Observed);
                control.IsFocused.Should().BeTrue();
                for (var index = 2; index < 5; index++)
                {
                    control.SelectedIndex = index;
                    model.Availability.Should().Be(model.AvailabilityChoices[index].Value);
                    model.Records.Should().BeEmpty();
                    await model.RefreshAsync();
                    model.Records.Should().BeEmpty();
                    model.Status.Should().Contain("not a global all-clear");
                }
                control.SelectedIndex = 0;
                model.Availability.Should().BeNull();
                control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down });
                model.Availability.Should().Be(SessionWorkOverviewAvailability.Observed);
                control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Up });
                model.Availability.Should().BeNull();
                await model.RefreshAsync();
                model.Records.Should().ContainSingle();
                var invalid = () => model.Availability = (SessionWorkOverviewAvailability)99;
                invalid.Should().Throw<ArgumentOutOfRangeException>();
                held.Fail = true;
                model.Availability = SessionWorkOverviewAvailability.ReadUnavailable;
                control.SelectedIndex.Should().Be(3);
                await model.RefreshAsync();
                model.Records.Single().Availability.Should().Be(SessionWorkOverviewAvailability.ReadUnavailable);
                model.NeedsAnswer = true;
                await model.RefreshAsync();
                model.Records.Should().ContainSingle("unavailable is never no pending questions");
                model.Select(model.Records.Single());
                model.CanNavigate.Should().BeFalse();
                model.SelectedDetails.Should().Contain("not an empty queue");
                held.Fail = false;
                access.CanInspect = false;
                window.Close();
                model.Availability.Should().BeNull();
                model.Records.Should().BeEmpty();
                control.IsEnabled.Should().BeFalse();
                held.Writes.Should().Be(0);
                f.Count("security_audit_events").Should().Be(audits);
                f.Count("host_tasks").Should().Be(tasks);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(clock);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("query")]
    [InlineData("availability")]
    [InlineData("revision")]
    [InlineData("close")]
    public async Task ExactNavigationAdmissionDoesNotSurviveQueryOrPrivateLifetimeChanges(string boundary)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, access);
        var entered = new TaskCompletionSource<Func<bool>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new AllWorkViewModel(service, access, NullLogger<AllWorkViewModel>.Instance,
            async (id, admitted, _) =>
            {
                id.Should().Be(f.Request.SessionId);
                admitted().Should().BeTrue();
                entered.SetResult(admitted);
                await release.Task.WaitAsync(f.Token);
            });
        model.Availability = SessionWorkOverviewAvailability.Observed;
        model.QueryKind = SessionListSearchKind.ExactId;
        model.QueryText = f.Request.SessionId.Value.ToString("D");
        await model.RefreshAsync();
        model.Select(model.Records.Single());
        var navigation = model.NavigateAsync();
        var admitted = await entered.Task.WaitAsync(f.Token);
        if (boundary is "query") { model.QueryText = "00000000-0000-0000-0000-000000000099"; }
        if (boundary is "availability") { model.Availability = SessionWorkOverviewAvailability.SourceChanged; }
        if (boundary is "revision") { access.ControlRevision++; }
        if (boundary is "close") { model.Close(); }
        admitted().Should().BeFalse();
        release.SetResult();
        await navigation;
        model.Records.Should().BeEmpty();
        model.CanNavigate.Should().BeFalse();
        model.Close();
        model.QueryText.Should().BeEmpty();
    }

    [WindowsFact]
    public async Task PrivateMetadataQueryIsExplicitAccessibleFocusPreservingAndNeverPersistentOrAuthority()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            var question = await WindowsSqliteTaskControlTests.WaitAsync(f);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var service = WindowsSqliteSessionWorkspaceTests.Service(f, access);
            var target = await service.CreateAsync(new("Private café_100% name"), RequestOrigin.LocalUi, f.Token);
            var model = new AllWorkViewModel(service, access, NullLogger<AllWorkViewModel>.Instance,
                (_, _, _) => throw new InvalidOperationException("Search cannot navigate automatically."));
            var window = new AllWorkWindow(model);
            window.Show();
            try
            {
                var tasks = f.Count("host_tasks");
                var audit = f.Count("security_audit_events");
                var clock = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                var field = window.FindControl<TextBox>("MetadataQuery")!;
                var search = window.FindControl<Button>("Search")!;
                ControlAutomationPeer.CreatePeerForElement(field)!.GetName().Should().Contain("typing performs no read");
                field.Focus().Should().BeTrue();
                field.Text = "café_100%";
                model.QueryText.Should().Be("café_100%");
                model.Records.Should().BeEmpty();
                model.CanSearch.Should().BeTrue();
                var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
                {
                    if (args.PropertyName is nameof(AllWorkViewModel.Status) && model.Records.Count == 1)
                    {
                        complete.TrySetResult();
                    }
                }
                model.PropertyChanged += OnChanged;
                field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                await complete.Task.WaitAsync(f.Token);
                model.PropertyChanged -= OnChanged;
                field.IsFocused.Should().BeTrue();
                model.Records.Single().Session.Should().Be(target);
                model.QueryText = "CAFÉ_100%";
                model.Records.Should().BeEmpty();
                await model.RefreshAsync();
                model.Records.Should().BeEmpty();
                model.Status.Should().Contain("filter nonmatches").And.Contain("not a global all-clear");
                model.QueryKind = SessionListSearchKind.ExactId;
                model.QueryText = f.Request.SessionId.Value.ToString("D");
                model.NeedsAnswer = true;
                await model.RefreshAsync();
                model.Records.Single().Work!.PendingQuestions.Single().Key.Should().Be(question.Key);
                model.Select(model.Records.Single());
                model.CanNavigate.Should().BeTrue();
                model.QueryText = target.Authority.SessionId.Value.ToString("D");
                model.CanNavigate.Should().BeFalse();
                await model.RefreshAsync();
                model.Records.Should().BeEmpty("Needs answer composes with the exact ID");
                model.NeedsAnswer = false;
                model.Filter = SessionListFilter.Done;
                await model.RefreshAsync();
                model.Records.Should().BeEmpty("lifecycle composes with the exact ID");
                field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                model.QueryText.Should().BeEmpty();
                model.Records.Should().BeEmpty();
                window.IsVisible.Should().BeTrue("Escape in the query clears rather than closes");
                model.QueryKind = SessionListSearchKind.NameSubstring;
                foreach (var invalid in new[] { " ", " x", "x ", "cafe\u0301", "x\nx", "x\u200bx", new string('x', 121) })
                {
                    model.QueryText = invalid;
                    model.CanSearch.Should().BeFalse();
                    search.IsEnabled.Should().BeFalse();
                    model.QueryHint.Should().StartWith("Invalid");
                    await model.RefreshAsync();
                    model.Records.Should().BeEmpty("invalid text cannot fall back to All");
                    model.Status.Should().Contain("unavailable");
                }
                model.QueryKind = SessionListSearchKind.ExactId;
                model.QueryText = "AAAAAAAA-0000-0000-0000-000000000001";
                model.CanSearch.Should().BeFalse();
                model.QueryText = Guid.Empty.ToString("D");
                model.CanSearch.Should().BeFalse();
                model.QueryText = "private volatile marker";
                window.FindControl<Button>("ClearQuery")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                field.IsFocused.Should().BeTrue();
                model.QueryText.Should().BeEmpty();
                f.Count("host_tasks").Should().Be(tasks);
                f.Count("security_audit_events").Should().Be(audit);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(clock);
                var retained = await f.Store.ReadQuestionPageAsync(f.Request.SessionId, null, 50, f.Token);
                retained.Records.Single().Key.Should().Be(question.Key);
                model.QueryText = "private close marker";
                window.Close();
                model.QueryText.Should().BeEmpty();
                model.QueryKind.Should().Be(SessionListSearchKind.NameSubstring);
                field.Text.Should().BeEmpty();
                model.CanSearch.Should().BeFalse();
                var database = System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(f.DatabasePath, f.Token));
                database.Should().NotContain("private close marker").And.NotContain("private volatile marker");
            }
            finally { window.Close(); }
        });
    }

    [WindowsFact]
    public async Task OwnedControllerRevokesLifecycleRetentionAndPrivacyWithoutReopeningOrCollectingInBackground()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            using var sessions = new SessionsWindowController(f.Main, f.Sessions, f.Evidence, f.Access,
                NullLogger<SessionsViewModel>.Instance);
            using var controller = new AllWorkWindowController(f.Main, f.Sessions, f.Access,
                NullLogger<AllWorkViewModel>.Instance, sessions);
            AllWorkWindow? opened = null;
            using var windows = Window.WindowOpenedEvent.AddClassHandler<AllWorkWindow>((window, _) => opened = window);
            sessions.Bind();
            controller.Bind();
            sessions.OpenAllWork = controller.Open;
            controller.Open();
            var model = (AllWorkViewModel)opened!.DataContext!;
            model.Records.Should().BeEmpty();
            model.QueryKind = SessionListSearchKind.ExactId;
            model.QueryText = f.LifecycleTarget!.SessionId.Value.ToString("D");
            await model.RefreshAsync();
            model.Records.Should().NotBeEmpty();
            await Task.Run(f.AdvanceLifecycleTargetAsync);
            opened.IsVisible.Should().BeTrue();
            model.Records.Should().BeEmpty();
            model.SelectedDetails.Should().BeEmpty();
            await model.RefreshAsync();
            var target = f.LifecycleTarget!.SessionId;
            using (var root = HostActivity.BeginRoot(new(new(Guid.NewGuid()), target, new(Guid.NewGuid()), RequestOrigin.LocalUi),
                HostActivityLayer.Desktop, HostOperation.Request))
            {
                var observed = await f.Sessions.ReadRetentionAsync(target, f.Token);
                await f.Sessions.SetRetentionHoldAsync(observed, true, RequestOrigin.LocalUi, () => true, f.Token);
            }
            model.Records.Should().BeEmpty();
            model.CanNext.Should().BeFalse();
            await model.RefreshAsync();
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            opened.Closed += (_, _) => closed.TrySetResult();
            f.Main.CloseForObservedPrivacyEvent("Synthetic privacy closure", hidePresentation: true);
            await closed.Task.WaitAsync(TestContext.Current.CancellationToken);
            await f.Main.PrivacyClosureTask;
            opened.IsVisible.Should().BeFalse();
            model.Records.Should().BeEmpty();
            model.Status.Should().BeEmpty();
            model.QueryText.Should().BeEmpty();
            controller.Dispose();
            controller.Open();
            opened.IsVisible.Should().BeFalse();
        });
    }

    [Theory]
    [InlineData("close")]
    [InlineData("privacy")]
    [InlineData("revision")]
    [InlineData("filter")]
    [InlineData("cancel")]
    [InlineData("revoke")]
    [InlineData("query")]
    [InlineData("query-kind")]
    [InlineData("clear-query")]
    [InlineData("availability")]
    public async Task InFlightPrivateSnapshotNeverRepopulatesClosedOrChangedOverview(string boundary)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var held = new GatedWorkStore(f.Store);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var service = new SessionWorkspaceService(held, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
        var model = new AllWorkViewModel(service, access, NullLogger<AllWorkViewModel>.Instance,
            (_, _, _) => throw new InvalidOperationException("A passive test must not navigate."));
        model.Availability = SessionWorkOverviewAvailability.Observed;
        model.QueryKind = SessionListSearchKind.ExactId;
        model.QueryText = f.Request.SessionId.Value.ToString("D");
        await model.RefreshAsync();
        model.Records.Should().NotBeEmpty();
        held.Hold = true;
        var read = model.RefreshAsync();
        await held.Entered.Task.WaitAsync(f.Token);
        model.Records.Should().BeEmpty("old private rows clear before an async refresh");
        model.CanEdit.Should().BeTrue("input is never disabled by a passive read");
        if (boundary is "close") { model.Close(); }
        if (boundary is "privacy") { access.CanInspect = false; }
        if (boundary is "revision") { access.ControlRevision++; }
        if (boundary is "filter") { model.Filter = SessionListFilter.Done; }
        if (boundary is "cancel") { model.Cancel(); }
        if (boundary is "revoke") { model.Revoke(); }
        if (boundary is "query") { model.QueryText = "00000000-0000-0000-0000-000000000099"; }
        if (boundary is "query-kind") { model.QueryKind = SessionListSearchKind.NameSubstring; }
        if (boundary is "clear-query") { model.ClearQuery(); }
        if (boundary is "availability") { model.Availability = SessionWorkOverviewAvailability.OutputBound; }
        held.Release.SetResult();
        await read;
        model.Records.Should().BeEmpty();
        model.SelectedDetails.Should().BeEmpty();
        model.CanNext.Should().BeFalse();
        model.CanNavigate.Should().BeFalse();
        model.Close();
        model.Status.Should().BeEmpty();
        model.NeedsAnswer.Should().BeFalse();
        model.QueryText.Should().BeEmpty();
        model.Availability.Should().BeNull();
        held.Writes.Should().Be(0);
    }

    private sealed class GatedWorkStore(WindowsSqliteHostInteractionStore store) : ISessionWorkspaceStore, ISessionWorkStore
    {
        internal bool Hold { get; set; }
        internal bool Fail { get; set; }
        internal int Reads { get; private set; }
        internal int Writes { get; private set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<SessionWorkSnapshot> ReadWorkAsync(HostId<SessionIdentity> session, long admissionRevision,
            SessionQueueLimits limits, CancellationToken token)
        {
            Reads++;
            if (Fail) { throw new IOException("Exact work source failed."); }
            var result = await store.ReadWorkAsync(session, admissionRevision, limits, token);
            if (Hold)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TestContext.Current.CancellationToken);
            }
            return result;
        }
        public ValueTask<SessionPage<SessionWorkspaceEntry>> ReadMetadataPageAsync(Guid? after, int limit, CancellationToken token) =>
            store.ReadMetadataPageAsync(after, limit, token);
        public ValueTask<SessionWorkspaceEntry> ReadMetadataAsync(HostId<SessionIdentity> session, CancellationToken token) =>
            store.ReadMetadataAsync(session, token);
        public ValueTask<SessionDispositionPreview> PreviewDispositionAsync(HostId<SessionIdentity> session,
            HostRevision generation, long metadataRevision, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionDispositionReceipt> DisposeSessionAsync(HostRequest request, SessionDispositionPreview preview,
            Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<HostTaskRecord> RecordControlIntentAsync(HostRequest request, CancellationToken token)
        {
            Writes++;
            throw new NotSupportedException();
        }
        public ValueTask<SessionPage<WorkSessionAuthorization>> ReadSessionsAsync(Guid? after, int limit, CancellationToken token) =>
            throw new NotSupportedException();
        public ValueTask<HostTaskObservation?> ReadTaskAsync(HostId<SessionIdentity> session, HostId<TaskIdentity> task,
            CancellationToken token) => throw new NotSupportedException();
        public ValueTask<HostTaskObservation> CancelWaitingTaskAsync(HostRequest control, HostTaskCancellationTarget target,
            Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionWorkspaceEntry> CreateNamedSessionAsync(HostRequest request, SessionName name,
            Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionWorkspaceEntry> RenameSessionAsync(HostRequest request, HostRevision generation, long metadataRevision,
            SessionName name, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<HostQuestionRecord>> ReadQuestionPageAsync(HostId<SessionIdentity> session, Guid? after,
            int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<HostTaskRecord>> ReadTaskPageAsync(HostId<SessionIdentity> session, Guid? after,
            int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<WorkSessionAuthorization> ChangeIdleLifecycleAsync(HostRequest request, HostRevision generation,
            bool active, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
    }

    [WindowsFact]
    public async Task NativeOverviewIsReachableScrollableFocusPreservingAndExactNavigationRemainsPassive()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            var question = await WindowsSqliteTaskControlTests.WaitAsync(f);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var service = WindowsSqliteSessionWorkspaceTests.Service(f, access);
            for (var i = 0; i < 12; i++) { await service.CreateAsync(new("Same inert label"), RequestOrigin.LocalUi, f.Token); }
            var sink = new WindowsSqliteEvidenceSink(f.Paths);
            sink.Initialize();
            var sessions = new SessionsViewModel(service,
                new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
                access, NullLogger<SessionsViewModel>.Instance);
            var sessionWindow = new SessionsWindow(sessions);
            var overview = new AllWorkViewModel(service, access, NullLogger<AllWorkViewModel>.Instance, sessions.SelectExactAsync);
            var window = new AllWorkWindow(overview);
            var opens = 0;
            sessionWindow.AllWorkRequested += (_, _) => { opens++; window.Show(); };
            sessionWindow.Show();
            sessionWindow.FindControl<Button>("AllWork")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            opens.Should().Be(1);
            overview.Records.Should().BeEmpty("opening does not collect work or grant authority");
            try
            {
                var refresh = window.FindControl<Button>("Refresh")!;
                refresh.Focus().Should().BeTrue();
                await overview.RefreshAsync();
                refresh.IsFocused.Should().BeTrue();
                overview.Records.Should().HaveCount(10);
                var list = window.FindControl<ListBox>("Records")!;
                list.Should().BeOfType<Kora.Controls.ScrollableListBox>();
                ControlAutomationPeer.CreatePeerForElement(list)!.GetProvider<IScrollProvider>().Should().NotBeNull();
                overview.CanNext.Should().BeTrue();
                await overview.RefreshAsync(next: true);
                overview.Records.Should().HaveCount(3);
                overview.CanNext.Should().BeFalse();
                await overview.RefreshAsync();
                overview.NeedsAnswer = true;
                overview.Records.Should().BeEmpty();
                SessionWorkOverviewRow? waiting = null;
                do
                {
                    await overview.RefreshAsync(next: overview.CanNext);
                    waiting ??= overview.Records.FirstOrDefault(row => row.Session.Authority.SessionId == f.Request.SessionId);
                } while (overview.CanNext);
                // Return to the exact observed source page if the question was on the first page.
                if (!overview.Records.Contains(waiting!))
                {
                    await overview.RefreshAsync();
                }
                waiting = overview.Records.Single();
                overview.Select(waiting);
                overview.SelectedDetails.Should().Contain(question.Key.QuestionId.Value.ToString("D"))
                    .And.Contain("Question IDs are observed metadata only").And.NotContain(question.Spec.Text);
                var clock = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                var tasks = f.Count("host_tasks");
                var audits = f.Count("security_audit_events");
                var beforeQuestion = await f.Store.ReadQuestionPageAsync(f.Request.SessionId, null, 50, f.Token);
                await overview.NavigateAsync();
                sessions.SelectedSessionRecord!.Authority.SessionId.Should().Be(f.Request.SessionId);
                sessions.PendingQuestions.Should().ContainSingle().Which.Key.Should().Be(question.Key);
                sessions.CanCancelWork.Should().BeFalse();
                sessions.CanCancelTask.Should().BeFalse();
                f.Count("host_tasks").Should().Be(tasks);
                f.Count("security_audit_events").Should().Be(audits);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(clock);
                (await f.Store.ReadQuestionPageAsync(f.Request.SessionId, null, 50, f.Token)).Records.Should()
                    .BeEquivalentTo(beforeQuestion.Records, options => options.WithStrictOrdering());
                overview.Revoke();
                overview.Records.Should().BeEmpty();
                overview.SelectedRecord.Should().BeNull();
                overview.SelectedDetails.Should().BeEmpty();
                overview.CanNext.Should().BeFalse();
                overview.Status.Should().Contain("revoked");
                await overview.RefreshAsync();
                window.Close();
                overview.Records.Should().BeEmpty();
                overview.Filter.Should().Be(SessionListFilter.All);
                overview.NeedsAnswer.Should().BeFalse();
                overview.Status.Should().BeEmpty();
                overview.CanRead.Should().BeFalse();
                await overview.RefreshAsync();
                overview.Records.Should().BeEmpty();
            }
            finally { window.Close(); sessionWindow.Close(); }
        });
    }

    [WindowsFact]
    public async Task UnavailableAndDoneSourcesDoNotBecomeAllClearResumeOrCancellationTargets()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, access);
        await service.ChangeLifecycleAsync(f.Request.SessionId, new(1), false, RequestOrigin.LocalUi, f.Token);
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var sessions = new SessionsViewModel(service,
            new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
            access, NullLogger<SessionsViewModel>.Instance);
        var overview = new AllWorkViewModel(service, access, NullLogger<AllWorkViewModel>.Instance, sessions.SelectExactAsync);
        try
        {
            overview.Filter = SessionListFilter.Done;
            await overview.RefreshAsync();
            overview.Select(overview.Records.Single());
            overview.SelectedDetails.Should().Contain("stored Done");
            var before = await f.Store.ReadSessionAsync(f.Request.SessionId, f.Token);
            await overview.NavigateAsync();
            sessions.SelectedSessionRecord!.Authority.Should().Be(before);
            sessions.CanCancelWork.Should().BeFalse();
            (await f.Store.ReadSessionAsync(f.Request.SessionId, f.Token)).Should().Be(before);
            access.CanInspect = false;
            overview.Close();
            overview.SelectedDetails.Should().BeEmpty();
            overview.Records.Should().BeEmpty();
            overview.CanNavigate.Should().BeFalse();
        }
        finally { overview.Close(); sessions.Close(); }
    }
}

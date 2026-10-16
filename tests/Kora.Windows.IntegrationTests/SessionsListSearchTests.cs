using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.Memory;
using Kora.Core.Authorization;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SessionsListSearchTests
{
    private static readonly string[] RangeControlNames = ["ListMinimumGeneration", "ListMaximumGeneration",
        "ListMinimumMetadataRevision", "ListMaximumMetadataRevision"];

    [WindowsFact]
    public async Task ExactNameKeepsDuplicateNamesPassiveAndInvalidEditsRetireSelectionWithoutSearching()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            var access = new Access();
            var service = new SessionWorkspaceService(f.Store, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
            var first = await service.CreateAsync(new("café 100%_"), RequestOrigin.LocalUi, f.Token);
            var second = await service.CreateAsync(new("café 100%_"), RequestOrigin.LocalUi, f.Token);
            await service.CreateAsync(new("prefix café 100%_"), RequestOrigin.LocalUi, f.Token);
            var sink = new WindowsSqliteEvidenceSink(f.Paths);
            sink.Initialize();
            var model = new SessionsViewModel(service,
                new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
                access, NullLogger<SessionsViewModel>.Instance);
            var window = new SessionsWindow(model);
            var audits = f.Count("security_audit_events");
            var tasks = f.Count("host_tasks");
            window.Show();
            try
            {
                model.ListSearchKind = SessionListSearchKind.ExactName;
                model.ListNameAvailability.Should().Be(SessionNameAvailability.Any);
                foreach (var name in RangeControlNames)
                {
                    var range = window.FindControl<TextBox>(name)!;
                    range.Focus().Should().BeTrue();
                    range.Text.Should().BeEmpty();
                    ControlAutomationPeer.CreatePeerForElement(range)!.GetName().Should().Contain("empty for Any");
                }
                model.ListQuery = "café 100%_";
                model.Sessions.Should().BeEmpty("edits do not read");
                await model.SearchListAsync();
                model.Sessions.Should().BeEquivalentTo([first, second]);
                model.SelectedSessionRecord.Should().BeNull("a name never selects even when it matches");
                await model.SelectAsync(first);
                model.ListQuery = "cafe\u0301 100%_";
                model.ListQuery.Should().Be("cafe\u0301 100%_");
                model.CanSearchList.Should().BeFalse();
                model.Status.Should().Contain("ExactName").And.Contain("Nothing is normalized");
                model.Sessions.Should().BeEmpty();
                model.SelectedSessionRecord.Should().BeNull();
                model.Detail.Should().BeEmpty();
                model.CanCancelWork.Should().BeFalse();
                model.ListQuery = new('a', 1000);
                model.ListQuery.Length.Should().Be(1000);
                model.CanSearchList.Should().BeFalse();
                model.ListQuery = "CAFÉ 100%_";
                await model.SearchListAsync();
                model.Sessions.Should().BeEmpty("exact name is ordinal case-sensitive");
                model.ListQuery = "café 100%_";
                model.ListNameAvailability = SessionNameAvailability.Unnamed;
                await model.SearchListAsync();
                model.Sessions.Should().BeEmpty("ExactName plus Unnamed is an empty conjunction");
                model.ClearListSearch();
                model.ListNameAvailability.Should().Be(SessionNameAvailability.Any);
                model.ListQuery.Should().BeEmpty();
                model.CanSearchList.Should().BeFalse("clearing does not reinterpret ExactName as Browse");
                f.Count("security_audit_events").Should().Be(audits);
                f.Count("host_tasks").Should().Be(tasks);
            }
            finally { window.Close(); }
        });
    }

    [WindowsFact]
    public async Task ExplicitBrowseAndAvailabilityAreAccessibleBoundedPassiveAndRetireSelectionWithoutReadsOnEdits()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            var access = new Access();
            var service = new SessionWorkspaceService(f.Store, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
            var named = await service.CreateAsync(new("Named passive session"), RequestOrigin.LocalUi, f.Token);
            var sink = new WindowsSqliteEvidenceSink(f.Paths);
            sink.Initialize();
            var model = new SessionsViewModel(service,
                new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
                access, NullLogger<SessionsViewModel>.Instance);
            var audits = f.Count("security_audit_events");
            var tasks = f.Count("host_tasks");
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                model.CanSearchList.Should().BeFalse("empty NameSubstring is not Browse");
                model.ListSearchKind = SessionListSearchKind.Browse;
                model.ListNameAvailability = SessionNameAvailability.Named;
                model.Sessions.Should().BeEmpty("editing does not read");
                model.CanSearchList.Should().BeTrue();
                var combo = window.FindControl<ComboBox>("ListNameAvailability")!;
                combo.Focus().Should().BeTrue();
                combo.SelectedItem.Should().Be(SessionNameAvailability.Named);
                ControlAutomationPeer.CreatePeerForElement(combo)!.GetName().Should().Contain("Any, Named or Unnamed")
                    .And.Contain("not ownership");
                await model.SearchListAsync();
                model.Sessions.Should().ContainSingle().Which.Should().Be(named);
                model.SelectedSessionRecord.Should().BeNull("Browse never implicitly selects");
                await model.SelectAsync(named);
                model.ListNameAvailability = SessionNameAvailability.Unnamed;
                model.Sessions.Should().BeEmpty();
                model.SelectedSessionRecord.Should().BeNull();
                model.Detail.Should().BeEmpty();
                await model.SearchListAsync();
                model.Sessions.Should().ContainSingle().Which.Metadata.Should().BeNull();
                model.SelectedSessionRecord.Should().BeNull();
                model.ListMaximumMetadataRevision = "1";
                await model.SearchListAsync();
                model.Sessions.Should().BeEmpty("Unnamed and a name-revision bound are an empty conjunction");
                model.Status.Should().Contain("1 unnamed");
                model.ListQuery = "not empty";
                model.CanSearchList.Should().BeFalse();
                model.Status.Should().Contain("Browse requires empty query text");
                model.ClearListSearch();
                model.ListNameAvailability.Should().Be(SessionNameAvailability.Any);
                model.ListQuery.Should().BeEmpty();
                model.ListMaximumMetadataRevision.Should().BeEmpty();
                model.Sessions.Should().BeEmpty("Clear never reads");
                f.Count("security_audit_events").Should().Be(audits);
                f.Count("host_tasks").Should().Be(tasks);
            }
            finally { window.Close(); }
            model.ListNameAvailability.Should().Be(SessionNameAvailability.Any);
        });
    }

    [Theory]
    [InlineData(SessionListSearchKind.NameSubstring)]
    [InlineData(SessionListSearchKind.ExactName)]
    public async Task NativeMetadataSearchIsKeyboardAccessibleAndPreservesTheExactVisibleWorkHistoryAndMemorySubject(SessionListSearchKind kind)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            var question = await WindowsSqliteTaskControlTests.WaitAsync(f);
            var access = new Access();
            var service = new SessionWorkspaceService(f.Store, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
            await service.RenameAsync(f.Request.SessionId, new(1), 0, new("Private café"), RequestOrigin.LocalUi, f.Token);
            await using var memories = new MemoryManagementService(service, f.Store, access, access, f.Store,
                new Audit(), NullLogger<MemoryAdmissionService>.Instance, f.Time);
            var sink = new WindowsSqliteEvidenceSink(f.Paths);
            sink.Initialize();
            var model = new SessionsViewModel(service,
                new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
                access, NullLogger<SessionsViewModel>.Instance, memories: memories);
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            model.MemoryDraft = "Retain this unsubmitted exact-session memory draft";
            await model.ReadHistoryAsync();
            model.SelectWorkRecord(model.WorkRecords.Single(record => record.TaskId == f.Request.TaskId.Value));
            model.CanCancelWork.Should().BeTrue();
            var selected = model.SelectedSessionRecord;
            var work = model.SelectedWorkRecord;
            var history = model.HistoryRecords.ToArray();
            var clock = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
            var audits = f.Count("security_audit_events");
            var task = await f.Tasks.ReadTaskAsync(f.Request.TaskId, f.Token);
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                var field = window.FindControl<TextBox>("ListQuery")!;
                field.Focus().Should().BeTrue();
                var modes = window.FindControl<ComboBox>("ListSearchKind")!;
                modes.Focus().Should().BeTrue();
                ControlAutomationPeer.CreatePeerForElement(modes)!.GetName().Should()
                    .Contain("whole-name ordinal case-sensitive ExactName");
                model.ListSearchKinds.Should().Equal(SessionListSearchKind.NameSubstring, SessionListSearchKind.ExactId,
                    SessionListSearchKind.Browse, SessionListSearchKind.ExactName);
                model.ListSearchKind = kind;
                model.ListQuery = kind is SessionListSearchKind.ExactName ? "Private café" : "café";
                model.ListMinimumGeneration = "1";
                model.ListMaximumGeneration = "1";
                model.ListMinimumMetadataRevision = "1";
                model.ListMaximumMetadataRevision = "1";
                model.SelectedSessionRecord.Should().Be(selected, "editing a draft in ordinary list mode does not target or clear work");
                var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
                {
                    if (args.PropertyName is nameof(SessionsViewModel.Status)
                        && model.Status.StartsWith("Passive session metadata:", StringComparison.Ordinal)) { complete.TrySetResult(); }
                }
                model.PropertyChanged += OnChanged;
                try
                {
                    await DispatchWhenReady(model, () => model.CanSearchList, () =>
                    {
                        model.CanSearchList.Should().BeTrue();
                        field.Focus().Should().BeTrue();
                        var key = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter };
                        field.RaiseEvent(key);
                        key.Handled.Should().BeTrue();
                    }, f.Token);
                    await complete.Task.WaitAsync(TimeSpan.FromSeconds(30), f.Token);
                }
                finally { model.PropertyChanged -= OnChanged; }
                field.IsFocused.Should().BeTrue();
                model.Sessions.Should().ContainSingle().Which.Should().Be(selected);
                model.SelectedSessionRecord.Should().Be(selected);
                model.SelectedWorkRecord.Should().Be(work);
                model.CanCancelWork.Should().BeTrue("search does not renew or replace the already inspected cancellation target");
                model.PendingQuestions.Should().ContainSingle().Which.Key.Should().Be(question.Key);
                model.HistoryRecords.Should().Equal(history);
                model.MemoryDraft.Should().Contain("Retain this unsubmitted");
                model.CanNext.Should().BeFalse();
                model.CanNextListSearch.Should().BeFalse();
                ControlAutomationPeer.CreatePeerForElement(field)!.GetName().Should().Contain("not conversation-history");
                foreach (var name in RangeControlNames)
                {
                    var range = window.FindControl<TextBox>(name)!;
                    range.Focus().Should().BeTrue();
                    range.Text.Should().Be("1");
                    range.IsEnabled.Should().BeTrue();
                    ControlAutomationPeer.CreatePeerForElement(range)!.GetName().Should().Contain("inclusive")
                        .And.Contain("positive decimal or empty for Any");
                }
                window.FindControl<Button>("SearchList")!.IsEnabled.Should().BeTrue();
                model.Status.Should().Contain("not an atomic snapshot").And.Contain("0 matching records omitted");
                f.Count("security_audit_events").Should().Be(audits);
                (await f.Tasks.ReadTaskAsync(f.Request.TaskId, f.Token)).Should().Be(task);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(clock);
                model.ListMinimumMetadataRevision = "2";
                model.CanSearchList.Should().BeFalse();
                model.Status.Should().Contain("minimum <= maximum");
                model.SelectedSessionRecord.Should().BeNull();
                model.PendingQuestions.Should().BeEmpty();
                model.WorkRecords.Should().BeEmpty();
                model.MemoryDraft.Should().BeEmpty();
                model.HistoryRecords.Should().BeEmpty();
                model.Detail.Should().BeEmpty();
                field.Focus().Should().BeTrue();
                await DispatchWhenReady(model, () => model.CanRead && model.CanEditListSearch,
                    () => field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }), f.Token);
                window.IsVisible.Should().BeTrue();
                field.IsFocused.Should().BeTrue();
                model.ListQuery.Should().BeEmpty();
                model.ListMinimumGeneration.Should().BeEmpty();
                model.ListMaximumGeneration.Should().BeEmpty();
                model.ListMinimumMetadataRevision.Should().BeEmpty();
                model.ListMaximumMetadataRevision.Should().BeEmpty();
                await model.RefreshAsync();
                model.Sessions.Should().ContainSingle();
                model.ListSearchKind = SessionListSearchKind.ExactId;
                model.ListQuery = f.Request.SessionId.Value.ToString("D");
                var exactSearch = Task.CompletedTask;
                await DispatchWhenReady(model, () => model.CanSearchList,
                    () => exactSearch = model.SearchListAsync(), f.Token);
                await exactSearch;
                model.Status.Should().StartWith("Passive session metadata:");
                model.Sessions.Should().ContainSingle().Which.Authority.SessionId.Should().Be(f.Request.SessionId);
                await DispatchWhenReady(model, () => model.CanRead && model.CanEditListSearch,
                    () => window.FindControl<Button>("ClearListSearch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), f.Token);
                field.IsFocused.Should().BeTrue();
                model.ListQuery.Should().BeEmpty();
                model.Sessions.Should().BeEmpty();
                model.ListQuery = "not an immutable ID";
                model.CanSearchList.Should().BeFalse();
                model.Status.Should().Contain("Invalid session metadata query").And.Contain("Nothing is normalized");
            }
            finally { window.Close(); }
            model.ListQuery.Should().BeEmpty();
        });
    }

    [Theory]
    [InlineData(SessionListSearchKind.NameSubstring)]
    [InlineData(SessionListSearchKind.ExactName)]
    public async Task ChangedSelectedMetadataInvalidatesSearchAndStaleControlsOnPassiveWorkRefresh(SessionListSearchKind kind)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var question = await WindowsSqliteTaskControlTests.WaitAsync(f);
        var access = new Access();
        var service = new SessionWorkspaceService(f.Store, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
        await service.RenameAsync(f.Request.SessionId, new(1), 0, new("needle"), RequestOrigin.LocalUi, f.Token);
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var model = new SessionsViewModel(service,
            new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
            access, NullLogger<SessionsViewModel>.Instance);
        try
        {
            model.ListSearchKind = kind;
            model.ListQuery = "needle";
            await model.SearchListAsync();
            await model.SelectAsync(model.Sessions.Single());
            model.PendingQuestions.Should().ContainSingle().Which.Key.Should().Be(question.Key);
            await service.RenameAsync(f.Request.SessionId, new(1), 1, new("renamed"), RequestOrigin.LocalUi, f.Token);
            await model.RefreshWorkAsync();
            model.Sessions.Should().BeEmpty();
            model.SelectedSessionRecord.Should().BeNull();
            model.WorkRecords.Should().BeEmpty();
            model.PendingQuestions.Should().BeEmpty();
            model.CanCancelWork.Should().BeFalse();
            model.Status.Should().Contain("metadata changed");
        }
        finally { model.Close(); }
    }

    private static async Task DispatchWhenReady(SessionsViewModel model, Func<bool> ready, Action action,
        CancellationToken token)
    {
        var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = true;
        void TryDispatch()
        {
            if (!active || dispatched.Task.IsCompleted || !ready()) { return; }
            try { action(); dispatched.TrySetResult(); }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            { dispatched.TrySetException(exception); }
        }
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args) =>
            Dispatcher.UIThread.Post(TryDispatch, DispatcherPriority.Background);
        // The real window's periodic passive refresh can refuse a search. Admit and dispatch
        // in one UI turn, after notifications unwind, instead of racing an earlier readiness check.
        model.PropertyChanged += Changed;
        try
        {
            Dispatcher.UIThread.Post(TryDispatch, DispatcherPriority.Background);
            await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
        }
        finally { active = false; model.PropertyChanged -= Changed; }
    }

    private sealed class Access : ISessionWorkspaceAccess, ICapabilityHostAccess, IEvidenceQueryAccess
    {
        public bool CanInspect => true;
        public bool CanControl => true;
        public long ControlRevision => 1;
        public bool IsCurrentHost => true;
    }

    private sealed class Audit : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent) => HostActivity.RequireCurrent();
    }
}

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
    public async Task NativeMetadataSearchIsKeyboardAccessibleAndPreservesTheExactVisibleWorkHistoryAndMemorySubject()
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
                model.ListQuery = "café";
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

    [WindowsFact]
    public async Task ChangedSelectedMetadataInvalidatesSearchAndStaleControlsOnPassiveWorkRefresh()
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

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Hosting;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed class SessionsDiagnosticsPlanTests
{
    [WindowsFact]
    public async Task CommandPreparationUsesOriginalCompleteReviewForEverySubsetAndNeverEnqueuesOrReadsDiagnostics()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var action = new NoDiagnosticsExecution();
        await using var queue = new SessionQueueService(f.Store, f.Store, new(f.Tasks), access, action,
            new(), NullLogger<SessionQueueService>.Instance, f.Time);
        var service = new SessionWorkspaceService(f.Store, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance, queue);
        var viewer = CreateViewer(f, access, queue);
        try
        {
            SessionQueueReadProfile[] profiles = [SessionQueueReadProfile.Version, SessionQueueReadProfile.Capabilities,
                SessionQueueReadProfile.Readiness, SessionQueueReadProfile.Runtimes];
            var originalQueue = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
            var audits = f.Count("security_audit_events");
            for (var mask = 1; mask < 16; mask++)
            {
                var selected = profiles.Where((_, index) => (mask & (1 << index)) != 0).ToArray();
                var preparation = await service.PrepareDiagnosticsPlanAsync(f.Request.SessionId, selected, () => true, f.Token);
                (await viewer.PrepareDiagnosticsPlanAsync(preparation, () => true, () => true, f.Token)).Should().BeTrue();
                viewer.SelectedSessionRecord.Should().Be(preparation.Work.Session);
                viewer.DiagnosticsPlanStatus.Should().Contain("Selected count " + selected.Length).And.Contain("NOT an atomic batch")
                    .And.Contain("original deadlines").And.Contain("limits revision").And.Contain("prerequisite None")
                    .And.Contain("execution slots unchanged");
                viewer.DiagnosticsVersion.Should().Be(selected.Contains(SessionQueueReadProfile.Version));
                viewer.DiagnosticsCapabilities.Should().Be(selected.Contains(SessionQueueReadProfile.Capabilities));
                viewer.DiagnosticsReadiness.Should().Be(selected.Contains(SessionQueueReadProfile.Readiness));
                viewer.DiagnosticsRuntimes.Should().Be(selected.Contains(SessionQueueReadProfile.Runtimes));
                (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Should().BeEquivalentTo(originalQueue);
                f.Count("security_audit_events").Should().Be(audits);
            }
            action.Reads.Should().Be(0);
        }
        finally { viewer.Close(); }
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("discard")]
    [InlineData("save-unavailable")]
    public async Task CommandPreparationHonorsOriginalDirtyComposerDecisionBeforeChangingExactSelection(string decision)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var original = f.Request.SessionId;
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        await using var queue = new SessionQueueService(f.Store, f.Store, new(f.Tasks), access, new NoDiagnosticsExecution(),
            new(), NullLogger<SessionQueueService>.Instance, f.Time);
        var service = new SessionWorkspaceService(f.Store, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance, queue);
        var viewer = CreateViewer(f, access, queue);
        try
        {
            await viewer.RefreshAsync();
            await viewer.SelectAsync(viewer.Sessions.Single(row => row.Authority.SessionId == original));
            viewer.ComposerBody = "original dirty private buffer";
            viewer.DecideComposerDraft = () => Task.FromResult(decision switch
            {
                "discard" => ComposerDraftDecision.Discard,
                "cancel" => ComposerDraftDecision.Cancel,
                _ => ComposerDraftDecision.Save,
            });
            var preparation = await service.PrepareDiagnosticsPlanAsync(f.Request.SessionId,
                [SessionQueueReadProfile.Readiness], () => true, f.Token);
            (await viewer.PrepareDiagnosticsPlanAsync(preparation, () => true, () => true, f.Token))
                .Should().Be(decision is "discard");
            viewer.SelectedSessionRecord!.Authority.SessionId.Should().Be(decision is "discard" ? f.Request.SessionId : original);
            viewer.ComposerBody.Should().Be(decision is "discard" ? "" : "original dirty private buffer");
            (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Should().BeEmpty();
        }
        finally { viewer.Close(); }
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("input")]
    public async Task PreparedCommandNativeConfirmationRetainsOriginalAdmissionThroughSeparateAction(string change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        await using var queue = new SessionQueueService(f.Store, f.Store, new(f.Tasks), access, new NoDiagnosticsExecution(),
            new(), NullLogger<SessionQueueService>.Instance, f.Time);
        var service = new SessionWorkspaceService(f.Store, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance, queue);
        var viewer = CreateViewer(f, access, queue);
        try
        {
            var admitted = true;
            var owner = true;
            var preparation = await service.PrepareDiagnosticsPlanAsync(f.Request.SessionId,
                [SessionQueueReadProfile.Version], () => admitted, f.Token);
            (await viewer.PrepareDiagnosticsPlanAsync(preparation, () => admitted, () => owner, f.Token)).Should().BeTrue();
            if (change is "owner") { owner = false; } else { admitted = false; }
            viewer.CanConfirmDiagnosticsPlan.Should().BeFalse();
            await viewer.ConfirmDiagnosticsPlanAsync();
            (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Should().BeEmpty();
        }
        finally { viewer.Close(); }
    }

    [WindowsFact]
    public async Task NativeExplicitProfilesCompleteReviewCancelAndConfirmUsePrivateSqliteWithoutInheritedPrerequisiteOrDispatch()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var action = new NoDiagnosticsExecution();
        await using var queue = new SessionQueueService(f.Store, f.Store, new(f.Tasks), access, action,
            new(), NullLogger<SessionQueueService>.Instance, f.Time);
        var viewer = CreateViewer(f, access, queue);
        try
        {
            await viewer.RefreshAsync();
            await viewer.SelectAsync(viewer.Sessions.Single());
            viewer.CanReviewDiagnosticsPlan.Should().BeFalse();
            viewer.CanConfirmDiagnosticsPlan.Should().BeFalse();
            await viewer.EnqueueVersionAsync();
            await viewer.RefreshWorkAsync();
            var original = viewer.QueueRecords.Single();
            viewer.SelectQueuePrerequisite(viewer.QueuePrerequisites.Single(row => row.TaskId == original.Request.TaskId));
            viewer.NameDraft = "keep draft";
            viewer.DiagnosticsRuntimes = true;
            viewer.DiagnosticsVersion = true;
            viewer.DiagnosticsReadiness = true;
            viewer.DiagnosticsCapabilities = true;
            var audits = f.Count("security_audit_events");
            var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
            await viewer.ReviewDiagnosticsPlanAsync();
            viewer.CanConfirmDiagnosticsPlan.Should().BeTrue();
            viewer.DiagnosticsPlanStatus.Should().Contain(f.Request.SessionId.Value.ToString("D")).And.Contain("Selected count 4")
                .And.Contain("1 -> 5/10").And.Contain("prerequisite None")
                .And.Contain(original.ExpiresAt.ToString("O")).And.Contain("NOT an atomic batch");
            viewer.DiagnosticsPlanStatus.IndexOf("1. application.get_version", StringComparison.Ordinal).Should()
                .BeLessThan(viewer.DiagnosticsPlanStatus.IndexOf("4. runtime.list", StringComparison.Ordinal));
            f.Count("security_audit_events").Should().Be(audits);
            (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
            viewer.DiscardDiagnosticsPlan();
            viewer.CanConfirmDiagnosticsPlan.Should().BeFalse();
            viewer.DiagnosticsVersion.Should().BeTrue();
            (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Should().ContainSingle().Which.Should().Be(original);
            await viewer.ReviewDiagnosticsPlanAsync();
            await viewer.ConfirmDiagnosticsPlanAsync();
            var committed = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
            committed.Entries.Should().HaveCount(5);
            committed.Entries[0].Should().Be(original);
            committed.Entries.Skip(1).Select(entry => entry.ReadProfile).Should().Equal(
                SessionQueueReadProfile.Version, SessionQueueReadProfile.Capabilities,
                SessionQueueReadProfile.Readiness, SessionQueueReadProfile.Runtimes);
            committed.Entries.Skip(1).Should().OnlyContain(entry => entry.Dependency == null && entry.IsPending);
            viewer.DiagnosticsPlanStatus.Should().Contain("Accepted").And.Contain("durable queue revision")
                .And.Contain("NEW request").And.NotContain("commit unconfirmed");
            viewer.NameDraft.Should().Be("keep draft");
            viewer.CanConfirmDiagnosticsPlan.Should().BeFalse();
            action.Reads.Should().Be(0);
            await viewer.RefreshWorkAsync();
            viewer.DiagnosticsVersion = true;
            await viewer.ReviewDiagnosticsPlanAsync();
            viewer.DiagnosticsRuntimes = true;
            viewer.CanConfirmDiagnosticsPlan.Should().BeFalse();
            await viewer.ReviewDiagnosticsPlanAsync();
            await viewer.RefreshWorkAsync();
            viewer.CanConfirmDiagnosticsPlan.Should().BeFalse();
        }
        finally { viewer.Close(); }
        viewer.DiagnosticsPlanStatus.Should().NotContain(f.Request.SessionId.Value.ToString("D"));
        viewer.DiagnosticsVersion.Should().BeFalse();
    }

    [WindowsFact]
    public async Task NativeDiagnosticsPrivateRevisionAndWorkSelectionRetireReviewsBeforeAnyEnqueue()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        await using var queue = new SessionQueueService(f.Store, f.Store, new(f.Tasks), access, new NoDiagnosticsExecution(),
            new(), NullLogger<SessionQueueService>.Instance, f.Time);
        var viewer = CreateViewer(f, access, queue);
        try
        {
            await viewer.RefreshAsync();
            await viewer.SelectAsync(viewer.Sessions.Single());
            await viewer.EnqueueVersionAsync();
            viewer.DiagnosticsVersion = true;
            await viewer.ReviewDiagnosticsPlanAsync();
            viewer.SelectQueueEntry(viewer.QueueRecords.Single());
            viewer.CanConfirmDiagnosticsPlan.Should().BeFalse();
            viewer.DiagnosticsVersion = true;
            await viewer.ReviewDiagnosticsPlanAsync();
            access.ControlRevision++;
            viewer.CanConfirmDiagnosticsPlan.Should().BeFalse();
            access.CanInspect = false;
            viewer.DiagnosticsPlanStatus.Should().NotContain(f.Request.SessionId.Value.ToString("D"));
            (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Should().ContainSingle();
        }
        finally { viewer.Close(); }
    }

    private static SessionsViewModel CreateViewer(InteractionStorageFixture f, WindowsSqliteSessionWorkspaceTests.Access access,
        SessionQueueService queue)
    {
        var service = new SessionWorkspaceService(f.Store, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance, queue);
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        return new(service, new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), access, f.Time,
            NullLogger<DurableEvidenceQuery>.Instance), access, NullLogger<SessionsViewModel>.Instance);
    }

    private sealed class NoDiagnosticsExecution : IDeterministicVersionQueueAction
    {
        internal int Reads { get; private set; }
        public CapabilityReply Observe(SessionQueueReadProfile profile, CancellationToken token, SessionQueueCapabilityTarget? target = null)
        {
            Reads++;
            throw new InvalidOperationException("Enqueue plans must never dispatch.");
        }
    }
}

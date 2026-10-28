using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Fact]
    public async Task AlreadyCancelledOverviewDoesNotStartEvenOuterAdmissionStorage()
    {
        using var f = new OverviewHeldFixture
        {
            PassiveRead = _ => throw new InvalidOperationException("closed admission must not read retention"),
            MetadataRead = _ => throw new InvalidOperationException("closed admission must not read metadata")
        };
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var controlStore = new AudioControlTestStore();
        await using var diagnosticAdmission = new DiagnosticRetentionAdmission(controlStore, controlStore, new(controlStore));
        var configuration = new SessionRetentionConfigurationService(new RetentionPreferences(),
            new(), diagnosticAdmission, new RetentionAudit());
        configuration.Observe();
        await using var retention = new SessionRetentionService(new RetentionStore(), configuration, f,
            new RetentionDispatcher(), TimeProvider.System, NullLogger<SessionRetentionService>.Instance);
        f.Service.BindRetention(retention);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = await Record.ExceptionAsync(() =>
            f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, cancellation.Token));
        error.Should().BeOfType<OperationCanceledException>()
            .Which.CancellationToken.Should().Be(cancellation.Token);
        f.TaskWrites.Should().BeEmpty();
    }

    public static IEnumerable<TheoryDataRow<string, string, bool>> OverviewHeldReads()
    {
        foreach (var layer in new[] { "admission", "metadata", "work", "publication" })
        {
            foreach (var closure in new[] { "cancel", "timeout", "cancel-timeout", "source", "privacy", "control" })
            {
                yield return new(layer, closure, false);
                yield return new(layer, closure, true);
            }
        }
    }

    [Theory]
    [MemberData(nameof(OverviewHeldReads))]
    public async Task OverviewOwnsEveryHeldLayerUntilActualCompletionAndNeverContinuesAfterClosure(
        string layer, string closure, bool fail)
    {
        using var f = new OverviewHeldFixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var controlStore = new AudioControlTestStore();
        await using var diagnosticAdmission = new DiagnosticRetentionAdmission(controlStore, controlStore, new(controlStore));
        var configuration = new SessionRetentionConfigurationService(new RetentionPreferences(),
            new(), diagnosticAdmission, new RetentionAudit());
        configuration.Observe();
        await using var retention = new SessionRetentionService(new RetentionStore(), configuration, f,
            new RetentionDispatcher(), TimeProvider.System, NullLogger<SessionRetentionService>.Instance);
        var clock = new OverviewTime();
        var service = new SessionWorkspaceService(f, new(f), f, f.Logger, overviewTime: clock);
        service.BindRetention(retention);
        var entries = new[] { ListEntry(1), ListEntry(2) };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken heldToken = default;
        var failure = new NotSupportedException("held authoritative source failed");
        var passiveReads = 0;
        var metadataReads = 0;
        var workReads = 0;
        async Task HoldAsync(CancellationToken token)
        {
            heldToken = token;
            entered.SetResult();
            try
            {
                await release.Task;
                if (fail) { throw failure; }
            }
            finally { joined.SetResult(); }
        }
        f.PassiveRead = async token =>
        {
            passiveReads++;
            if (layer is "admission" && passiveReads == 1 || layer is "publication" && passiveReads == 6)
            {
                await HoldAsync(token);
            }
            return 0;
        };
        f.MetadataRead = async token =>
        {
            metadataReads++;
            if (layer is "metadata") { await HoldAsync(token); }
            return new([.. entries], null);
        };
        f.OverviewReader = async (id, token) =>
        {
            workReads++;
            if (layer is "work") { await HoldAsync(token); }
            return OverviewSnapshot(entries.Single(entry => entry.Authority.SessionId == id));
        };
        using var cancellation = new CancellationTokenSource();
        var invalidated = false;
        service.WorkOverviewInvalidated += _ => invalidated = true;
        var pending = service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(f.Token);
            var readsAtClosure = (metadataReads, workReads);
            switch (closure)
            {
                case "cancel": cancellation.Cancel(); break;
                case "timeout": clock.Fire(); break;
                case "cancel-timeout": cancellation.Cancel(); clock.Fire(); break;
                case "source":
                    await service.ChangeLifecycleAsync(f.Request.SessionId, new(1), false, RequestOrigin.LocalUi, f.Token);
                    invalidated.Should().BeTrue();
                    break;
                case "privacy": f.CanInspect = false; break;
                case "control": f.AdvanceRevision(); break;
            }
            if (closure is "cancel" or "timeout" or "cancel-timeout")
            {
                heldToken.IsCancellationRequested.Should().BeTrue();
            }
            joined.Task.IsCompleted.Should().BeFalse();
            pending.IsCompleted.Should().BeFalse("logical retirement cannot stand in for physical completion");
            release.SetResult();
            var error = await Record.ExceptionAsync(async () => await pending.WaitAsync(f.Token));
            joined.Task.IsCompletedSuccessfully.Should().BeTrue();
            (metadataReads, workReads).Should().Be(readsAtClosure, "closure forbids any subsequent overview read");
            if (closure is "timeout")
            {
                error.Should().BeOfType<TimeoutException>();
            }
            else if (closure is "cancel" or "cancel-timeout")
            {
                error.Should().BeOfType<OperationCanceledException>()
                    .Which.CancellationToken.Should().Be(cancellation.Token);
            }
            else if (fail)
            {
                error.Should().BeSameAs(failure, "unexpected source failures must remain observed, not unavailable pages");
            }
            else
            {
                error.Should().BeOfType<InvalidOperationException>();
            }
            if (fail && closure is "cancel" or "timeout" or "cancel-timeout")
            {
                error!.InnerException.Should().BeSameAs(failure, "logical cancellation must retain the joined storage failure");
            }
            pending.IsCompletedSuccessfully.Should().BeFalse("no page is current after authority retirement");
            f.TaskWrites.Should().NotContain(record => record.State == HostTaskState.Succeeded && closure != "source");
            HostActivity.RequireCurrent().Should().Be(root);
            clock.Timer!.Disposed.Should().BeTrue();
        }
        finally
        {
            release.TrySetResult();
            _ = await Record.ExceptionAsync(async () => await pending.WaitAsync(f.Token));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRacingHandledStorageFailureStillJoinsAndCannotPublishUnavailable(bool timeout)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        OverviewReader(f, [ListEntry(1), ListEntry(2)]);
        using var cancellation = new CancellationTokenSource();
        var clock = new OverviewTime();
        var service = new SessionWorkspaceService(f, new(f), f, f.Logger, overviewTime: clock);
        var reads = 0;
        f.OverviewReader = (_, _) =>
        {
            reads++;
            if (timeout) { clock.Fire(); }
            else { cancellation.Cancel(); }
            throw new IOException("source cancelled while failing");
        };
        var error = await Record.ExceptionAsync(() =>
            service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, cancellation.Token));
        if (timeout) { error.Should().BeOfType<TimeoutException>(); }
        else { error.Should().BeOfType<OperationCanceledException>(); }
        reads.Should().Be(1);
        f.TaskWrites.Should().BeEmpty();
        clock.Timer!.Disposed.Should().BeTrue();
    }

    private sealed class OverviewHeldFixture : Fixture, ISessionWorkspaceStore, ISessionRetentionControlStore
    {
        internal Func<CancellationToken, ValueTask<long>> PassiveRead { get; set; } = null!;
        internal Func<CancellationToken, ValueTask<SessionPage<SessionWorkspaceEntry>>> MetadataRead { get; set; } = null!;

        ValueTask<long> ISessionRetentionControlStore.ValidatePassiveInspectionAsync(CancellationToken token) => PassiveRead(token);

        ValueTask<SessionPage<SessionWorkspaceEntry>> ISessionWorkspaceStore.ReadMetadataPageAsync(
            Guid? after, int limit, CancellationToken cancellationToken) => MetadataRead(cancellationToken);
    }
}

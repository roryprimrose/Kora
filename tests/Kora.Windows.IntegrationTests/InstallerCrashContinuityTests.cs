using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

using AwesomeAssertions;

using Kora.Setup;
using Kora.Windows.Coordination;

namespace Kora.Windows.IntegrationTests;

public sealed class InstallerCrashContinuityTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    public async Task ConfirmedSuccessRollbackOrRebootClearsOwnedMarkersOnlyOnNormalDisposal(int status, bool reboot)
    {
        var store = new MarkerStore();
        var engine = new Engine(store);
        var shutdown = new Shutdown(store);
        await using var session = new InstallerSession(engine, shutdown: shutdown);
        session.Detected(0, false);
        await session.StartAsync(SetupAction.Install, true, cancellationToken: TestContext.Current.CancellationToken);
        store.Events.Should().Equal("create.guard", "flush.guard", "create.owner", "flush.owner", "plan");
        store.Entries.Should().HaveCount(2);
        session.Planned(0);
        store.Events.Should().EndWith("apply");
        engine.Applies.Should().Be(1);
        session.Applied(status, reboot);
        store.Entries.Should().HaveCount(2);
        store.Events.Should().NotContain("delete.owner").And.NotContain("delete.guard");
        await session.DisposeAsync();
        store.Entries.Should().BeEmpty();
        store.Handles.Should().OnlyContain(handle => handle.Closed);
        store.Events.Should().ContainInOrder("delete.owner", "delete.guard", "close.owner", "close.guard");
        session.CanLaunchOnClose.Should().Be(status >= 0 && !reboot);
    }

    [Fact]
    public async Task CancellationBeforePlanningClearsOnlyTheUnusedOwnedReservation()
    {
        var store = new MarkerStore();
        var engine = new Engine(store);
        using var cancellation = new CancellationTokenSource();
        var shutdown = new Shutdown(store) { AfterCreate = cancellation.Cancel };
        await using var session = new InstallerSession(engine, shutdown: shutdown);
        session.Detected(0, false);
        await session.StartAsync(SetupAction.Install, true, cancellationToken: cancellation.Token);
        session.Phase.Should().Be(SetupPhase.Cancelled);
        engine.Applies.Should().Be(0);
        store.Events.Should().NotContain("plan").And.NotContain("apply");
        store.Entries.Should().HaveCount(2);
        await session.DisposeAsync();
        store.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task FailedPlanningCannotClaimNativeMutationAndCanClearItsUnusedReservation()
    {
        var store = new MarkerStore();
        var engine = new Engine(store);
        await using var session = new InstallerSession(engine, shutdown: new Shutdown(store));
        session.Detected(0, false);
        await session.StartAsync(SetupAction.Install, true, cancellationToken: TestContext.Current.CancellationToken);
        session.Planned(-1);
        await session.DisposeAsync();
        engine.Applies.Should().Be(0);
        store.Entries.Should().BeEmpty();
        session.CanOfferLaunch.Should().BeFalse();
    }

    [Fact]
    public async Task UnknownApplyExceptionRetainsBothMarkersAndRejectsLateSuccess()
    {
        var store = new MarkerStore();
        var failure = Marshal.GetExceptionForHR(unchecked((int)0x80004005), new IntPtr(-1))
            ?? throw new InvalidOperationException("The failing native HRESULT did not produce an exception.");
        var engine = new Engine(store) { ApplyFailure = failure };
        await using var session = new InstallerSession(engine, shutdown: new Shutdown(store));
        session.Detected(0, false);
        await session.StartAsync(SetupAction.Install, true, cancellationToken: TestContext.Current.CancellationToken);
        var apply = () => session.Planned(0);
        apply.Should().Throw<COMException>();
        session.Fail(failure.HResult);
        await session.DisposeAsync();
        store.Entries.Should().HaveCount(2);
        store.Handles.Should().OnlyContain(handle => handle.Closed);
        store.Events.Should().NotContain("delete.owner").And.NotContain("delete.guard");
        session.RecordNativeApplyComplete().Should().BeFalse();
        session.Applied(0, false);
        session.Phase.Should().Be(SetupPhase.Failed);
        session.CanOfferLaunch.Should().BeFalse();
        store.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task RealTerminalCallbackBeforeDisposalCanProveRollbackWithoutChangingAnEarlierFailureToSuccess()
    {
        var store = new MarkerStore();
        var engine = new Engine(store);
        await using var session = new InstallerSession(engine, shutdown: new Shutdown(store));
        session.Detected(0, false);
        await session.StartAsync(SetupAction.Install, true, cancellationToken: TestContext.Current.CancellationToken);
        session.Planned(0);
        session.Fail(-1);
        session.RecordNativeApplyComplete().Should().BeTrue();
        session.Applied(0, false);
        session.Phase.Should().Be(SetupPhase.Failed);
        session.CanOfferLaunch.Should().BeFalse();
        await session.DisposeAsync();
        store.Entries.Should().BeEmpty();
    }

    [Fact]
    public void TerminalCallbackAfterDisposalIsSealedCannotReclassifyUnknownNativeWork()
    {
        var store = new MarkerStore();
        using var continuity = new InstallerMaintenanceContinuity(store);
        continuity.BeginNativeApply();
        continuity.SealForDisposal();
        continuity.ConfirmNativeApplyComplete().Should().BeFalse();
        continuity.Dispose();
        store.Entries.Should().HaveCount(2);
        store.Events.Should().NotContain("delete.owner").And.NotContain("delete.guard");
        var apply = () => continuity.BeginNativeApply();
        apply.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ProcessExitWithoutNormalDisposalRetainsExplicitNonRecoverableMaintenanceIdentity()
    {
        var store = new MarkerStore();
        using var continuity = new InstallerMaintenanceContinuity(store);
        continuity.BeginNativeApply();
        foreach (var handle in store.Handles) { handle.Dispose(); }
        store.Entries.Should().HaveCount(2);
        store.Events.Should().NotContain("delete.owner").And.NotContain("delete.guard");
        var guard = store.Entries[InstallerContinuityMarkerKind.DebugRecoveryGuard].Data;
        Encoding.UTF8.GetString(guard).Should().Be("Kora.InstallerMaintenance.v1.guard\n");
        // DebugOwnerRecovery.Read requires exactly 60 bytes and KORD magic before any recovery can be admitted.
        guard.Length.Should().NotBe(60);
        BinaryPrimitives.ReadInt32LittleEndian(guard).Should().NotBe(0x4B4F5244);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingForeignMarkerCannotBeReplacedOrDeleted(bool guard)
    {
        var kind = guard ? InstallerContinuityMarkerKind.DebugRecoveryGuard : InstallerContinuityMarkerKind.Owner;
        var store = new MarkerStore();
        var foreign = store.ReplaceWithForeign(kind);
        var originalBytes = foreign.Data.ToArray();
        var engine = new Engine(store);
        await using var session = new InstallerSession(engine, shutdown: new Shutdown(store));
        session.Detected(0, false);
        await session.StartAsync(SetupAction.Install, true, cancellationToken: TestContext.Current.CancellationToken);
        session.Phase.Should().Be(SetupPhase.Failed);
        engine.Applies.Should().Be(0);
        store.Events.Should().NotContain("plan").And.NotContain("apply");
        store.Entries[kind].Should().BeSameAs(foreign);
        foreign.Data.Should().Equal(originalBytes);
        foreign.Deleted.Should().BeFalse();
        store.Handles.Should().OnlyContain(handle => handle.Closed);
    }

    [Fact]
    public void ReplacedPathsCannotCauseNormalDisposalToDeleteForeignObjects()
    {
        var store = new MarkerStore();
        using var continuity = new InstallerMaintenanceContinuity(store);
        continuity.BeginNativeApply();
        continuity.ConfirmNativeApplyComplete().Should().BeTrue();
        var foreignOwner = store.ReplaceWithForeign(InstallerContinuityMarkerKind.Owner);
        var foreignGuard = store.ReplaceWithForeign(InstallerContinuityMarkerKind.DebugRecoveryGuard);
        continuity.Dispose();
        store.Entries[InstallerContinuityMarkerKind.Owner].Should().BeSameAs(foreignOwner);
        store.Entries[InstallerContinuityMarkerKind.DebugRecoveryGuard].Should().BeSameAs(foreignGuard);
        foreignOwner.Deleted.Should().BeFalse();
        foreignGuard.Deleted.Should().BeFalse();
        store.Handles.Should().OnlyContain(handle => handle.Record.Deleted && handle.Closed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedDurableCreationCannotPlanAndNeverDeletesAnUncertainWrite(bool guard)
    {
        var kind = guard ? InstallerContinuityMarkerKind.DebugRecoveryGuard : InstallerContinuityMarkerKind.Owner;
        var store = new MarkerStore { FlushFailure = kind };
        var engine = new Engine(store);
        await using var session = new InstallerSession(engine, shutdown: new Shutdown(store));
        session.Detected(0, false);
        await session.StartAsync(SetupAction.Install, true, cancellationToken: TestContext.Current.CancellationToken);
        session.Phase.Should().Be(SetupPhase.Failed);
        engine.Applies.Should().Be(0);
        store.Events.Should().NotContain("plan").And.NotContain("apply");
        store.Entries.Should().NotBeEmpty();
        store.Handles.Should().OnlyContain(handle => handle.Closed);
        store.Events.Should().NotContain("delete.owner").And.NotContain("delete.guard");
    }

    [Fact]
    public async Task FailedOwnedCleanupRemainsAnErrorAndCannotApproveCompletionLaunch()
    {
        var store = new MarkerStore { DeleteFailure = InstallerContinuityMarkerKind.Owner };
        var engine = new Engine(store);
        var session = new InstallerSession(engine, shutdown: new Shutdown(store));
        session.Detected(0, false);
        await session.StartAsync(SetupAction.Install, true, cancellationToken: TestContext.Current.CancellationToken);
        session.Planned(0);
        session.Applied(0, false);
        session.AcceptClose(true);
        var dispose = async () => await session.DisposeAsync();
        await dispose.Should().ThrowAsync<IOException>();
        await dispose.Should().ThrowAsync<IOException>();
        var launcher = new Launcher();
        var launch = () => session.LaunchAfterCloseAsync(launcher, TestContext.Current.CancellationToken);
        await launch.Should().ThrowAsync<InvalidOperationException>();
        launcher.Launches.Should().Be(0);
        store.Entries.Should().HaveCount(2);
        store.Handles.Should().OnlyContain(handle => handle.Closed);
    }

    private sealed class Shutdown(MarkerStore store) : IInstallerShutdown
    {
        public Action? AfterCreate { get; init; }
        public Task<IInstallerMaintenanceLease> AcquireAsync(InstallScope scope, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lease = new Lease(new InstallerMaintenanceContinuity(store));
            AfterCreate?.Invoke();
            return Task.FromResult<IInstallerMaintenanceLease>(lease);
        }
        public void Validate(InstallScope scope) { }
    }

    private sealed class Lease(InstallerMaintenanceContinuity continuity) : IInstallerMaintenanceLease
    {
        public void BeginNativeApply() => continuity.BeginNativeApply();
        public bool ConfirmNativeApplyComplete() => continuity.ConfirmNativeApplyComplete();
        public ValueTask DisposeAsync()
        {
            continuity.SealForDisposal();
            continuity.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Engine(MarkerStore store) : IInstallerEngine
    {
        public Exception? ApplyFailure { get; init; }
        public int Applies { get; private set; }
        public void Plan(SetupAction action, InstallScope scope, bool startAtLogin) => store.Events.Add("plan");
        public void Apply()
        {
            store.Entries.Should().HaveCount(2);
            store.Entries.Values.Should().OnlyContain(record => record.Durable);
            store.Handles.Should().OnlyContain(handle => !handle.Closed);
            store.Events.Add("apply");
            Applies++;
            if (ApplyFailure is not null) { throw ApplyFailure; }
        }
        public void LogFailure(int status) { }
    }

    private sealed class Launcher : IInstallerApplicationLauncher
    {
        public int Launches { get; private set; }
        public Task LaunchAsync(InstallScope scope, CancellationToken cancellationToken)
        {
            Launches++;
            return Task.CompletedTask;
        }
    }

    private sealed class MarkerStore : IInstallerMaintenanceMarkerStore
    {
        public Dictionary<InstallerContinuityMarkerKind, MarkerRecord> Entries { get; } = [];
        public List<MarkerHandle> Handles { get; } = [];
        public List<string> Events { get; } = [];
        public InstallerContinuityMarkerKind? FlushFailure { get; init; }
        public InstallerContinuityMarkerKind? DeleteFailure { get; init; }

        public IInstallerMaintenanceMarker CreateDurable(InstallerContinuityMarkerKind kind)
        {
            if (Entries.ContainsKey(kind)) { throw new IOException("An existing marker must be preserved."); }
            Events.Add("create." + Name(kind));
            var record = new MarkerRecord { Data = InstallerMaintenanceMarkerFormat.Identity(kind).ToArray() };
            Entries.Add(kind, record);
            var handle = new MarkerHandle(this, kind, record);
            Handles.Add(handle);
            if (FlushFailure == kind)
            {
                handle.Dispose();
                throw new IOException("The marker flush did not complete.");
            }
            record.Durable = true;
            Events.Add("flush." + Name(kind));
            return handle;
        }

        public MarkerRecord ReplaceWithForeign(InstallerContinuityMarkerKind kind)
        {
            var record = new MarkerRecord { Data = [42, 17, 91], Durable = true };
            Entries[kind] = record;
            return record;
        }

        public static string Name(InstallerContinuityMarkerKind kind) => kind switch
        {
            InstallerContinuityMarkerKind.Owner => "owner",
            InstallerContinuityMarkerKind.DebugRecoveryGuard => "guard",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private sealed class MarkerRecord
    {
        public byte[] Data { get; init; } = [];
        public bool Durable { get; set; }
        public bool Deleted { get; set; }
    }

    private sealed class MarkerHandle(MarkerStore store, InstallerContinuityMarkerKind kind,
        MarkerRecord record) : IInstallerMaintenanceMarker
    {
        public MarkerRecord Record => record;
        public bool Closed { get; private set; }
        public void DeleteOwned()
        {
            if (Closed) { throw new InvalidOperationException("The owned handle is closed."); }
            if (store.DeleteFailure == kind) { throw new IOException("Owned marker deletion failed."); }
            store.Events.Add("delete." + MarkerStore.Name(kind));
            record.Deleted = true;
            if (store.Entries.TryGetValue(kind, out var current) && ReferenceEquals(current, record))
            {
                store.Entries.Remove(kind);
            }
        }
        public void Dispose()
        {
            if (Closed) { return; }
            Closed = true;
            store.Events.Add("close." + MarkerStore.Name(kind));
        }
    }
}

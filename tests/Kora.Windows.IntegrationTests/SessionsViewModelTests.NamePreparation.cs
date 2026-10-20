using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsViewModelTests
{
    [Theory]
    [InlineData("none")]
    [InlineData("close")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("revision")]
    [InlineData("native")]
    [InlineData("original")]
    [InlineData("cancel")]
    [InlineData("selection")]
    [InlineData("editor")]
    [InlineData("source")]
    [InlineData("reentrant")]
    public async Task QueuedNamePreparationNeverAdoptsChangedNativeOrOriginalSource(string change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var held = new HeldStore { HoldNameReads = true };
        var service = new SessionWorkspaceService(held, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var viewer = new SessionsViewModel(service,
            new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
            access, NullLogger<SessionsViewModel>.Instance);
        using var cancellation = new CancellationTokenSource();
        var native = true;
        var original = true;
        var command = SessionNamePreparationCommand.Parse("session name prepare " + f.Request.SessionId.Value.ToString("D") + " \"new private literal\"", "Kora")!;
        var read = viewer.PrepareNameAsync(command, RequestOrigin.LocalUi, () => original, () => native, cancellation.Token);
        read.IsCompleted.Should().BeFalse();
        switch (change)
        {
            case "close": viewer.Close(); break;
            case "private": access.CanInspect = false; break;
            case "control": access.CanControl = false; break;
            case "revision": access.ControlRevision++; break;
            case "native": native = false; break;
            case "original": original = false; break;
            case "cancel": cancellation.Cancel(); break;
            case "selection": await viewer.SelectAsync(new(new(new(Guid.NewGuid()), new(1), true), null)); break;
            case "editor": viewer.NameDraft = "independent dirty editor"; break;
            case "source": held.NameSourceRevision++; break;
            case "reentrant":
                var replace = () => viewer.PrepareNameAsync(command, RequestOrigin.LocalUi, () => true, () => true, f.Token);
                await replace.Should().ThrowAsync<InvalidOperationException>();
                break;
        }
        held.NameCompletion.SetResult(new(new(f.Request.SessionId, new(1), true), null));
        (await read).Should().Be(change is "none");
        viewer.HasNamePreparation.Should().Be(change is "none");
        if (change is not "none") { viewer.NamePreparationReview.Should().BeEmpty(); }
        if (change is "editor") { viewer.NameDraft.Should().Be("independent dirty editor"); }
        held.ControlCalls.Should().Be(0);
        viewer.Close();
    }

    private sealed partial class HeldStore
    {
        internal bool HoldNameReads { get; init; }
        internal long NameSourceRevision { get; set; }
        internal TaskCompletionSource<SessionWorkspaceEntry> NameCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<long> ValidatePassiveInspectionAsync(CancellationToken token) => ValueTask.FromResult(NameSourceRevision);
        public ValueTask<SessionRetentionObservation> ReadRetentionObservationAsync(HostId<SessionIdentity> session, CancellationToken token) =>
            throw new NotSupportedException();
        public ValueTask<SessionRetentionObservation> SetRetentionHoldAsync(HostRequest request, SessionRetentionObservation expected,
            bool perpetual, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
    }
}

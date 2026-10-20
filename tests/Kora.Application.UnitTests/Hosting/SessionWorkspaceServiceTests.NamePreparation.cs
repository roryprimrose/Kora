using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Fact]
    public async Task ExactNamePreparationObservationIsPassiveAndPreservesTheCompleteMetadata()
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var expected = new SessionWorkspaceEntry(f.Session, new(f.Session.SessionId, new(4), new("Private Café")));
        f.ExactListReader = _ => expected;
        var observed = await f.Service.ReadNamePreparationAsync(f.Session.SessionId, f.Token);
        observed.Entry.Should().Be(expected);
        observed.SourceRevision.Should().Be(f.PassiveRevision);
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("source")]
    [InlineData("cancel")]
    [InlineData("privacy")]
    public async Task NamePreparationRejectsChangedExactSourceWithoutWritingIntent(string failure)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        using var cancellation = new CancellationTokenSource();
        f.ExactListReader = _ =>
        {
            if (failure is "source") { f.PassiveRevision++; }
            if (failure is "cancel") { cancellation.Cancel(); }
            if (failure is "privacy") { f.CanInspect = false; }
            return new(failure is "foreign" ? new(new(Guid.NewGuid()), new(1), true) : f.Session, null);
        };
        var action = () => f.Service.ReadNamePreparationAsync(f.Session.SessionId, cancellation.Token);
        await action.Should().ThrowAsync<Exception>();
        f.TaskWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingRenameAcceptsTheOriginalNativeFenceWithoutWeakeningCanonicalBehavior(bool admitted)
    {
        using var f = new Fixture();
        var action = () => f.Service.RenameAsync(f.Session.SessionId, f.Session.Generation, 0, new("Name"),
            RequestOrigin.LocalUi, f.Token, () => admitted);
        if (admitted) { (await action()).Metadata!.Name.Value.Should().Be("Name"); }
        else { await action.Should().ThrowAsync<InvalidOperationException>(); f.TaskWrites.Should().BeEmpty(); }
    }

    [Fact]
    public async Task PreparedRenameRequiresTheSourceFencedStoreAndPassesTheExactObservation()
    {
        using var f = new Fixture();
        var result = await f.Service.RenameAsync(f.Session.SessionId, f.Session.Generation, 0, new("Name"),
            RequestOrigin.LocalUi, f.Token, () => true, 42);
        result.Metadata!.Name.Value.Should().Be("Name");
        f.NameSourceRevision.Should().Be(42);
        var unsupported = new Kora.Application.Hosting.SessionWorkspaceService(new NoFileStore(f), new(f), f, f.Logger);
        var rename = () => unsupported.RenameAsync(f.Session.SessionId, f.Session.Generation, 0, new("Name"),
            RequestOrigin.LocalUi, f.Token, () => true, 42);
        await rename.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Source-fenced*");
    }

    private partial class Fixture : ISessionNameRevisionStore
    {
        internal long NameSourceRevision { get; private set; }
        public ValueTask<SessionWorkspaceEntry> RenameSessionAsync(HostRequest request, HostRevision expectedGeneration,
            long expectedMetadataRevision, SessionName name, long expectedSourceRevision,
            Func<bool> canControl, CancellationToken cancellationToken)
        {
            NameSourceRevision = expectedSourceRevision;
            return RenameSessionAsync(request, expectedGeneration, expectedMetadataRevision, name, canControl, cancellationToken);
        }
    }
}

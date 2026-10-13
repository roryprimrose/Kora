using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Theory]
    [InlineData("read")]
    [InlineData("resolve")]
    [InlineData("initial-control")]
    [InlineData("initial-admission")]
    [InlineData("read-filter")]
    [InlineData("read-late")]
    [InlineData("resolve-session")]
    [InlineData("resolve-metadata")]
    [InlineData("resolve-late")]
    [InlineData("resolve-control")]
    [InlineData("cancel")]
    [InlineData("missing")]
    public async Task SavedDraftCatalogueUsesPassiveMetadataAndFailsClosed(string boundary)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var exact = new SavedDraftSummary(Guid.NewGuid(), new(Guid.NewGuid()), f.Session, null,
            1, 4, 1, f.ControlRevision, 1, new string('a', 64));
        f.DraftPage = new(SessionListFilter.All, [exact], null, 1, 1, false);
        f.DraftTarget = new(exact.Session, null);
        var gate = boundary is not "initial-admission";
        if (boundary is "initial-control") { f.AdvanceRevision(); }
        f.BeforeDraftRead = () =>
        {
            if (boundary is "read-filter") { f.DraftPage = f.DraftPage with { Filter = SessionListFilter.Active }; }
            if (boundary is "read-late") { gate = false; }
            if (boundary is "cancel") { throw new OperationCanceledException(); }
        };
        f.BeforeDraftResolve = () =>
        {
            if (boundary is "resolve-session") { f.DraftTarget = new(f.Session with { Generation = new(2) }, null); }
            if (boundary is "resolve-metadata") { f.DraftTarget = new(f.Session, new(f.Session.SessionId, new(1), new("renamed"))); }
            if (boundary is "resolve-late") { gate = false; }
            if (boundary is "resolve-control") { f.AdvanceRevision(); }
        };
        if (boundary is "read")
        {
            (await f.Service.ReadSavedDraftCatalogue(SessionListFilter.All, null, () => gate, f.Token)).Should().Be(f.DraftPage);
        }
        else if (boundary is "resolve")
        {
            (await f.Service.ResolveSavedDraft(exact, () => gate, f.Token)).Should().Be(f.DraftTarget);
        }
        else if (boundary is "missing")
        {
            var missing = new Kora.Application.Hosting.SessionWorkspaceService(new NoFileStore(f), new(f), f, f.Logger);
            await missing.Invoking(async value => await value.ReadSavedDraftCatalogue(SessionListFilter.All, null, () => true, f.Token))
                .Should().ThrowAsync<InvalidOperationException>();
            var absent = () => f.Service.ResolveSavedDraft(null!, () => true, f.Token);
            await absent.Should().ThrowAsync<ArgumentNullException>();
        }
        else
        {
            Func<Task> action = boundary.StartsWith("read", StringComparison.Ordinal) || boundary is "cancel"
                ? async () => await f.Service.ReadSavedDraftCatalogue(SessionListFilter.All, null, () => gate, f.Token)
                : async () => await f.Service.ResolveSavedDraft(exact, () => gate, f.Token);
            await action.Should().ThrowAsync<Exception>();
        }
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
        f.PassiveInspectionCalls.Should().Be(0);
    }

    private sealed partial class Fixture : ISavedDraftCatalogueStore
    {
        internal SavedDraftPage DraftPage { get; set; } = new(SessionListFilter.All, [], null, 0, 0, false);
        internal SessionWorkspaceEntry? DraftTarget { get; set; }
        internal Action? BeforeDraftRead { get; set; }
        internal Action? BeforeDraftResolve { get; set; }
        public ValueTask<SavedDraftPage> ReadSavedDraftPage(SessionListFilter filter, string? cursor,
            long controlRevision, Func<bool> admitted, CancellationToken token)
        {
            BeforeDraftRead?.Invoke();
            return ValueTask.FromResult(DraftPage);
        }
        public ValueTask<SessionWorkspaceEntry> ResolveSavedDraft(SavedDraftSummary exact, Func<bool> admitted, CancellationToken token)
        {
            BeforeDraftResolve?.Invoke();
            return ValueTask.FromResult(DraftTarget!);
        }
    }
}

using System.Collections.Immutable;

using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Theory]
    [InlineData("match")]
    [InlineData("empty")]
    [InlineData("scope")]
    [InlineData("revision")]
    [InlineData("minimum")]
    [InlineData("maximum")]
    [InlineData("browse-any")]
    public async Task SavedDraftMetadataFiltersAreForwardedAndUntrustedPagesCannotSubstituteThem(string boundary)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var row = new SavedDraftSummary(Guid.NewGuid(), new(Guid.NewGuid()), f.Session, null,
            2, 4, 1, f.ControlRevision, 1, new string('a', 64));
        var filter = boundary is "browse-any" ? SavedDraftMetadataFilter.Any : new SavedDraftMetadataFilter(2, 4, 4);
        f.DraftPage = new(SessionListFilter.All, boundary is "empty" ? [] : [row], null, 1, 0, false,
            MetadataFilter: boundary is "browse-any" ? null : filter);
        f.BeforeDraftRead = () =>
        {
            f.DraftPage = boundary switch
            {
                "scope" => f.DraftPage with { MetadataFilter = null },
                "revision" => f.DraftPage with { Records = [row with { DraftRevision = 3 }] },
                "minimum" => f.DraftPage with { Records = [row with { Utf8Bytes = 3 }] },
                "maximum" => f.DraftPage with { Records = [row with { Utf8Bytes = 5 }] },
                _ => f.DraftPage,
            };
        };
        var read = () => f.Service.ReadSavedDraftCatalogue(SessionListFilter.All, null, () => true, f.Token,
            metadataFilter: filter);
        if (boundary is "match" or "empty" or "browse-any") { (await read()).Should().Be(f.DraftPage); }
        else { await read.Should().ThrowAsync<Exception>(); }
        f.ObservedDraftMetadataFilter.Should().BeSameAs(filter);
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Fact]
    public async Task SavedDraftQueryConsumesMoreThanOneThousandFakeRetainedSessionsWithoutInventingAProfileQuota()
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var query = new SessionListSearch(SessionListSearchKind.NameSubstring, SessionListFilter.All, "needle");
        var source = Guid.NewGuid();
        var profile = new HostId<DeviceProfileIdentity>(Guid.NewGuid());
        var found = new HashSet<HostId<SessionIdentity>>();
        string? cursor = null;
        for (var offset = 0; offset < 1003; offset += SavedDraftPage.MaximumRecords)
        {
            var rows = Enumerable.Range(offset + 1, Math.Min(SavedDraftPage.MaximumRecords, 1003 - offset))
                .Select(index =>
                {
                    var session = f.Session with { SessionId = new(Guid.Parse(
                        "30000000-0000-0000-0000-" + index.ToString("D12", System.Globalization.CultureInfo.InvariantCulture))) };
                    return new SavedDraftSummary(source, profile, session, new(session.SessionId, new(1), new("needle")),
                        1, 4, 1, f.ControlRevision, 1, new string('a', 64));
                }).ToImmutableArray();
            var next = offset + rows.Length < 1003
                ? Convert.ToBase64String(BitConverter.GetBytes(offset)) + "." + new string('a', 64) : null;
            f.DraftPage = new(SessionListFilter.All, rows, next, rows.Length, 0, false, query.Kind, query.Digest);
            var page = await f.Service.ReadSavedDraftCatalogue(SessionListFilter.All, cursor, () => true, f.Token, query);
            f.ObservedDraftCursor.Should().Be(cursor);
            foreach (var row in page.Records) { found.Add(row.Session.SessionId).Should().BeTrue(); }
            cursor = page.Next;
        }
        found.Should().HaveCount(1003);
        cursor.Should().BeNull();
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("valid-name")]
    [InlineData("valid-id")]
    [InlineData("filter")]
    [InlineData("kind")]
    [InlineData("digest")]
    [InlineData("nonmatch")]
    [InlineData("unnamed")]
    [InlineData("late")]
    [InlineData("browse")]
    public async Task SavedDraftMetadataQueryIsForwardedAndObservedScopeCannotBeSubstituted(string boundary)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var row = new SavedDraftSummary(Guid.NewGuid(), new(Guid.NewGuid()), f.Session,
            new(f.Session.SessionId, new(1), new("café 🙂 100%_")), 1, 4, 1, f.ControlRevision, 1, new string('a', 64));
        var query = boundary is "valid-id"
            ? new SessionListSearch(SessionListSearchKind.ExactId, SessionListFilter.All, f.Session.SessionId.Value.ToString("D"))
            : new SessionListSearch(SessionListSearchKind.NameSubstring,
                boundary is "filter" ? SessionListFilter.Done : SessionListFilter.All, "café 🙂 100%_");
        f.DraftPage = new(SessionListFilter.All, [row], null, 1, 1, false, query.Kind, query.Digest);
        var admitted = true;
        f.BeforeDraftRead = () =>
        {
            f.DraftPage = boundary switch
            {
                "kind" => f.DraftPage with { QueryKind = SessionListSearchKind.ExactId },
                "digest" => f.DraftPage with { QueryDigest = new string('b', 64) },
                "nonmatch" => f.DraftPage with { Records = [row with { Metadata = new(f.Session.SessionId, new(1), new("different")) }] },
                "unnamed" => f.DraftPage with { Records = [row with { Metadata = null }] },
                _ => f.DraftPage,
            };
            if (boundary is "late") { admitted = false; }
        };
        var read = () => f.Service.ReadSavedDraftCatalogue(SessionListFilter.All, null, () => admitted, f.Token,
            boundary is "browse" ? null : query);
        if (boundary.StartsWith("valid", StringComparison.Ordinal))
        {
            (await read()).Records.Should().Equal(row);
            f.ObservedDraftQuery.Should().BeSameAs(query);
        }
        else { await read.Should().ThrowAsync<Exception>(); }
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
        f.PassiveInspectionCalls.Should().Be(0);
    }

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

    private partial class Fixture : ISavedDraftCatalogueStore
    {
        internal SavedDraftPage DraftPage { get; set; } = new(SessionListFilter.All, [], null, 0, 0, false);
        internal SessionWorkspaceEntry? DraftTarget { get; set; }
        internal Action? BeforeDraftRead { get; set; }
        internal Action? BeforeDraftResolve { get; set; }
        internal SessionListSearch? ObservedDraftQuery { get; private set; }
        internal SavedDraftMetadataFilter? ObservedDraftMetadataFilter { get; private set; }
        internal string? ObservedDraftCursor { get; private set; }
        public ValueTask<SavedDraftPage> ReadSavedDraftPage(SessionListFilter filter, string? cursor,
            long controlRevision, Func<bool> admitted, CancellationToken token, SessionListSearch? query = null,
            SavedDraftMetadataFilter? metadataFilter = null)
        {
            ObservedDraftQuery = query;
            ObservedDraftMetadataFilter = metadataFilter;
            ObservedDraftCursor = cursor;
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

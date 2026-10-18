using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed class WindowsSqliteSessionListSearchTests
{
    [Theory]
    [InlineData(SessionListSearchKind.NameSubstring)]
    [InlineData(SessionListSearchKind.ExactName)]
    public async Task PrivateMetadataSearchFindsNamesBeyondFiftyAndPreservesExactIdentityAuthorityAndClocks(SessionListSearchKind kind)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var perpetual = await f.GrantAsync("perpetual");
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        var created = new List<SessionWorkspaceEntry>();
        for (var i = 0; i < 53; i++)
        {
            created.Add(await service.CreateAsync(new("ordinary"), RequestOrigin.LocalUi, f.Token));
        }
        var ordered = created.OrderBy(entry => entry.Authority.SessionId.Value.ToString("D"), StringComparer.Ordinal).ToArray();
        var last = ordered[^1];
        last = await service.RenameAsync(last.Authority.SessionId, last.Authority.Generation, 1,
            new("Private café 🐈 100%_"), RequestOrigin.LocalUi, f.Token);
        var duplicate = await service.RenameAsync(ordered[^2].Authority.SessionId, ordered[^2].Authority.Generation, 1,
            last.Metadata!.Name, RequestOrigin.LocalUi, f.Token);
        var audits = f.Count("security_audit_events");
        var tasks = f.Count("host_tasks");
        var clocks = await f.Store.ReadRetentionAsync(last.Authority.SessionId, f.Token);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var value = kind is SessionListSearchKind.ExactName ? last.Metadata!.Name.Value : "café 🐈 100%_";
        var length = last.Metadata!.Name.GetScalarCount();
        var first = await service.SearchListAsync(kind, SessionListFilter.All, value, null, 25, f.Token,
            1, 1, 2, 2, SessionNameAvailability.Named, length, length);
        first.Scanned.Should().Be(50);
        first.Records.Should().BeEmpty();
        first.Next.Should().NotBeNull("nonmatching pages must not be confused with search exhaustion");
        var next = await service.SearchListAsync(kind, SessionListFilter.All, value, first.Next, 25, f.Token,
            1, 1, 2, 2, SessionNameAvailability.Named, length, length);
        next.Records.Should().Equal(duplicate, last);
        next.Next.Should().BeNull();
        var exact = await service.SearchListAsync(SessionListSearchKind.ExactId, SessionListFilter.All,
            last.Authority.SessionId.Value.ToString("D"), null, 25, f.Token, 1, 1, 2, 2,
            minimumNameLength: length, maximumNameLength: length);
        exact.Records.Should().ContainSingle().Which.Should().Be(last);
        var excluded = await service.SearchListAsync(SessionListSearchKind.ExactId, SessionListFilter.All,
            last.Authority.SessionId.Value.ToString("D"), null, 25, f.Token, 1, 1, 3, null);
        excluded.Records.Should().BeEmpty();
        excluded.Scanned.Should().Be(1);
        var unnamed = await service.SearchListAsync(SessionListSearchKind.Browse, SessionListFilter.Active,
            "", null, 25, f.Token, 1, 1, nameAvailability: SessionNameAvailability.Unnamed);
        unnamed.Scanned.Should().Be(50);
        unnamed.Next.Should().NotBeNull();
        var remainingUnnamed = await service.SearchListAsync(SessionListSearchKind.Browse, SessionListFilter.Active,
            "", unnamed.Next, 25, f.Token, 1, 1, nameAvailability: SessionNameAvailability.Unnamed);
        unnamed.Records.Concat(remainingUnnamed.Records).Should().ContainSingle().Which.Authority.SessionId.Should().Be(f.Request.SessionId);
        unnamed.Records.Concat(remainingUnnamed.Records).Single().Metadata.Should().BeNull();
        remainingUnnamed.Next.Should().BeNull();
        var excludedUnnamed = await service.SearchListAsync(SessionListSearchKind.Browse, SessionListFilter.Active,
            "", null, 25, f.Token, 1, 1, 1, 1, SessionNameAvailability.Unnamed);
        excludedUnnamed.Records.Should().BeEmpty();
        excludedUnnamed.Scanned.Should().Be(50);
        excludedUnnamed.Next.Should().NotBeNull();
        f.Count("security_audit_events").Should().Be(audits);
        f.Count("host_tasks").Should().Be(tasks);
        (await f.Store.ReadRetentionAsync(last.Authority.SessionId, f.Token)).Should().Be(clocks);
        (await f.Store.ReadGrantsAsync(f.Token)).Should().ContainSingle().Which.Should().Be(perpetual);
        (await f.Tasks.ReadTaskAsync(f.Request.TaskId, f.Token))!.State.Should().Be(HostTaskState.IntentRecorded);
    }

    [Fact]
    public async Task PrivateScalarRangesTreatSupplementaryNamesAsOneAndUnnamedAsUnknownNotZero()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        var one = await service.CreateAsync(new("\U0001F408"), RequestOrigin.LocalUi, f.Token);
        var two = await service.CreateAsync(new("q\u0301"), RequestOrigin.LocalUi, f.Token);
        var maximum = await service.CreateAsync(new(string.Concat(Enumerable.Repeat("\U0001F408", SessionName.MaximumScalars))),
            RequestOrigin.LocalUi, f.Token);
        var audits = f.Count("security_audit_events");
        var tasks = f.Count("host_tasks");
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var single = await service.SearchListAsync(SessionListSearchKind.Browse, SessionListFilter.Active, "", null, 25, f.Token,
            1, 1, 1, 1, SessionNameAvailability.Named, 1, 1);
        single.Records.Should().Equal(one);
        var pair = await service.SearchListAsync(SessionListSearchKind.NameSubstring, SessionListFilter.All, "q", null, 25, f.Token,
            minimumNameLength: 2, maximumNameLength: 2);
        pair.Records.Should().Equal(two);
        var full = await service.SearchListAsync(SessionListSearchKind.ExactName, SessionListFilter.All, maximum.Metadata!.Name.Value,
            null, 25, f.Token, minimumNameLength: SessionName.MaximumScalars, maximumNameLength: SessionName.MaximumScalars);
        full.Records.Should().Equal(maximum);
        var unnamed = await service.SearchListAsync(SessionListSearchKind.ExactId, SessionListFilter.Active,
            f.Request.SessionId.Value.ToString("D"), null, 25, f.Token, maximumNameLength: 1);
        unnamed.Records.Should().BeEmpty();
        unnamed.Scanned.Should().Be(1);
        unnamed.Unnamed.Should().Be(1);
        var unknown = await service.SearchListAsync(SessionListSearchKind.Browse, SessionListFilter.Active,
            "", null, 25, f.Token, nameAvailability: SessionNameAvailability.Unnamed, minimumNameLength: 1);
        unknown.Records.Should().BeEmpty();
        unknown.Unnamed.Should().Be(1);
        f.Count("security_audit_events").Should().Be(audits);
        f.Count("host_tasks").Should().Be(tasks);
    }

    [Fact]
    public async Task CorruptionAtTheFullPageTailCannotHideBehindResultOrScalarLengthExclusions()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        for (var index = 0; index < 50; index++)
        {
            await service.CreateAsync(new("needle"), RequestOrigin.LocalUi, f.Token);
        }
        f.Mutate("""
            UPDATE session_metadata SET name='cafe' || char(769)
            WHERE session_id=(SELECT session_id FROM work_sessions WHERE state<>2 ORDER BY session_id LIMIT 1 OFFSET 49);
            """);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var read = () => service.SearchListAsync(SessionListSearchKind.Browse, SessionListFilter.All,
            "", null, 1, f.Token, minimumNameLength: 6, maximumNameLength: 6);
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(SessionListSearchKind.NameSubstring)]
    [InlineData(SessionListSearchKind.ExactName)]
    public async Task MutableSearchReportsRenamesStateRemovalAndNewAppendsWithoutInventingSnapshotChronology(SessionListSearchKind kind)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        var created = new List<SessionWorkspaceEntry>();
        for (var i = 0; i < 3; i++) { created.Add(await service.CreateAsync(new("needle"), RequestOrigin.LocalUi, f.Token)); }
        created.Sort((left, right) => string.CompareOrdinal(left.Authority.SessionId.Value.ToString("D"), right.Authority.SessionId.Value.ToString("D")));
        SessionListSearchPage first;
        using (var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            first = await service.SearchListAsync(kind, SessionListFilter.All, "needle", null, 1, f.Token);
        }
        first.Records.Should().Equal(created[0]);
        await service.RenameAsync(created[1].Authority.SessionId, new(1), 1, new("changed"), RequestOrigin.LocalUi, f.Token);
        var done = await service.ChangeLifecycleAsync(created[2].Authority.SessionId, new(1), false, RequestOrigin.LocalUi, f.Token);
        var added = await service.CreateAsync(new("needle"), RequestOrigin.LocalUi, f.Token);
        using (var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            var next = await service.SearchListAsync(kind, SessionListFilter.All, "needle", first.Next, 50, f.Token);
            next.Records.Should().Contain(entry => entry.Authority == done);
            next.Records.Should().NotContain(entry => entry.Authority.SessionId == created[1].Authority.SessionId);
            next.Records.Any(entry => entry.Authority.SessionId == added.Authority.SessionId).Should()
                .Be(string.CompareOrdinal(added.Authority.SessionId.Value.ToString("D"), first.Next!.After.ToString("D")) > 0);
            var fresh = await service.SearchListAsync(kind, SessionListFilter.Done, "needle", null, 50, f.Token);
            fresh.Records.Should().ContainSingle().Which.Authority.Should().Be(done);
        }
        SessionDispositionPreview preview;
        using (var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            preview = await service.PreviewDispositionAsync(done.SessionId, done.Generation, 1, f.Token);
        }
        await service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, f.Token);
        using (var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            var gone = await service.SearchListAsync(kind, SessionListFilter.Done, "needle", null, 50, f.Token);
            gone.Records.Should().BeEmpty();
            var exactGone = () => service.SearchListAsync(SessionListSearchKind.ExactId, SessionListFilter.All,
                done.SessionId.Value.ToString("D"), null, 1, f.Token);
            await exactGone.Should().ThrowAsync<InvalidOperationException>();
            var unknown = () => service.SearchListAsync(SessionListSearchKind.ExactId, SessionListFilter.All,
                Guid.NewGuid().ToString("D"), null, 1, f.Token);
            await unknown.Should().ThrowAsync<InvalidDataException>();
        }
        SessionListSearchPage.Scope.Should().Contain("not an atomic snapshot").And.Contain("behind the cursor");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("replacement")]
    [InlineData("corrupt")]
    [InlineData("owner")]
    [InlineData("cancel")]
    public async Task ReadBoundariesRejectMissingChangedCorruptOrUnownedStorageWithoutInitializingAnInventory(string mode)
    {
        using var f = new InteractionStorageFixture();
        var access = new WindowsSqliteSessionWorkspaceTests.Access { CanControl = false };
        if (mode is not "missing")
        {
            await f.InitializeAsync();
            await WindowsSqliteSessionWorkspaceTests.Service(f, new()).RenameAsync(f.Request.SessionId,
                new(1), 0, new("needle"), RequestOrigin.LocalUi, f.Token);
        }
        if (mode is "replacement") { File.Delete(f.DatabasePath); }
        if (mode is "corrupt") { f.Mutate("UPDATE session_metadata SET name='cafe' || char(769);"); }
        if (mode is "owner") { access.CanInspect = false; }
        using var cancellation = new CancellationTokenSource();
        if (mode is "cancel") { cancellation.Cancel(); }
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, access);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var read = () => service.SearchListAsync(SessionListSearchKind.ExactName, SessionListFilter.All,
            "needle", null, 50, cancellation.Token, minimumNameLength: 1, maximumNameLength: 1);
        await read.Should().ThrowAsync<Exception>();
        if (mode is "replacement" or "missing") { File.Exists(f.DatabasePath).Should().BeFalse(); }
    }
}

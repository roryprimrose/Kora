using AwesomeAssertions;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteSessionProviderProfileTests
{
    [Fact]
    public async Task CopyCommitsOnlyDestinationSparseBytesAndCorrelatedAuditWithoutSourceTasksGrantsOrIdentityTransfer()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var source = await SeedCopySource(f);
        var expected = await f.Store.ReadProviderProfile(f.Request.SessionId, f.Token);
        var sourceTasks = f.Count("host_tasks");
        var sourceAudits = f.Count("security_audit_events");
        using (var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.RecordControlIntentAsync(f.Request, f.Token);
            var saved = await f.Store.CommitProviderProfile(f.Request, expected, source.Desired, static () => true, f.Token, source);
            saved.Session.Should().Be(expected.Session);
            saved.Desired.Serialize().Should().Be(source.Desired.Serialize());
            saved.Desired.Service.Should().BeNull();
            saved.Desired.Reasoning.Should().BeNull();
            saved.Desired.Context.Should().BeNull();
            saved.Held.Should().BeFalse();
            saved.Revision.Should().Be(1);
        }
        (await f.Store.ReadProviderProfile(source.Session.SessionId, f.Token)).Should().BeEquivalentTo(source);
        f.Count("host_tasks").Should().Be(sourceTasks + 1);
        f.Count("security_audit_events").Should().Be(sourceAudits + 3);
        f.Count("host_questions").Should().Be(0);
        f.Count("scoped_grants").Should().Be(0);
        using (var connection = f.OpenRaw())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT envelope FROM security_audit_events
                WHERE json_extract(envelope,'$.Request.TaskId.Value')=$task ORDER BY sequence;
                """;
            command.Parameters.AddWithValue("$task", f.Request.TaskId.Value.ToString("D"));
            using var rows = command.ExecuteReader();
            var count = 0;
            while (rows.Read())
            {
                rows.GetString(0).Should().Contain(f.Request.SessionId.Value.ToString("D"))
                    .And.NotContain(source.Session.SessionId.Value.ToString("D"));
                count++;
            }
            count.Should().Be(3);
        }
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await f.Store.ReadProviderProfile(expected.Session.SessionId, f.Token)).Desired.Serialize().Should().Be(source.Desired.Serialize());
        (await f.Store.ReadProviderProfile(source.Session.SessionId, f.Token)).Should().BeEquivalentTo(source);
    }

    [Theory]
    [InlineData("same")] [InlineData("generation")] [InlineData("revision")]
    [InlineData("payload")] [InlineData("held")] [InlineData("foreign")]
    [InlineData("different-desired")]
    public async Task SourceSnapshotIsAnExactDataFenceNotAnAuthorityOrRetargetingHint(string drift)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var source = await SeedCopySource(f);
        var destination = await f.Store.ReadProviderProfile(f.Request.SessionId, f.Token);
        var expectedSource = drift switch
        {
            "same" => destination,
            "generation" => source with { Session = source.Session with { Generation = new(2) } },
            "revision" => source with { Revision = source.Revision + 2 },
            "payload" => source with { Desired = new(ProviderService.Codex) },
            "held" => source with { Held = true },
            "foreign" => source with { Session = source.Session with { SessionId = new(Guid.NewGuid()) } },
            _ => source,
        };
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RecordControlIntentAsync(f.Request, f.Token);
        await f.Store.Invoking(store => store.CommitProviderProfile(f.Request, destination,
            drift is "different-desired" ? new(ProviderService.Claude) : source.Desired, static () => true, f.Token, expectedSource).AsTask())
            .Should().ThrowAsync<Exception>();
        (await f.Store.ReadProviderProfile(destination.Session.SessionId, f.Token)).Revision.Should().Be(0);
        (await f.Store.ReadProviderProfile(source.Session.SessionId, f.Token)).Should().BeEquivalentTo(source);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ExactSourceIsRevalidatedAtTheWriteAndTerminalBoundariesAndUncertaintyHoldsOnlyDestination(int phase)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var source = await SeedCopySource(f);
        var destination = await f.Store.ReadProviderProfile(f.Request.SessionId, f.Token);
        var checkpoint = new SourceCheckpoint(source.Session.SessionId, phase);
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        using (var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.RecordControlIntentAsync(f.Request, f.Token);
            await f.Store.Invoking(store => store.CommitProviderProfile(f.Request, destination, source.Desired, static () => true, f.Token, source).AsTask())
                .Should().ThrowAsync<Exception>();
        }
        checkpoint.Calls.Should().Be(phase);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        var held = await f.Store.ReadProviderProfile(destination.Session.SessionId, f.Token);
        held.Held.Should().BeTrue();
        held.Revision.Should().Be(phase == 3 ? 1 : 0);
        (await f.Store.ReadProviderProfile(source.Session.SessionId, f.Token)).Should().BeEquivalentTo(source);
    }

    private static async Task<SessionProviderProfile> SeedCopySource(InteractionStorageFixture f)
    {
        var sourceId = f.Request.SessionId;
        var initial = await f.Store.ReadProviderProfile(sourceId, f.Token);
        var tuple = new ProviderProfileTuple(ProviderService.Local, new("account"), new(1), new("model"), new("effort"), new("context"));
        f.Request = new(new(Guid.NewGuid()), sourceId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        SessionProviderProfile source;
        using (var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.RecordControlIntentAsync(f.Request, f.Token);
            source = await f.Store.CommitProviderProfile(f.Request, initial, new(model: tuple.Model, binding: tuple), static () => true, f.Token);
        }
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        f.Request = new(new(Guid.NewGuid()), f.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        return source;
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public async Task CopyAuditFailureOrCancellationPreservesSourceAndRequiresDestinationReceiptRecovery(bool cancel, int phase)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var source = await SeedCopySource(f);
        var destination = await f.Store.ReadProviderProfile(f.Request.SessionId, f.Token);
        using var cancellation = new CancellationTokenSource();
        f.Reopen(new Checkpoint(() =>
        {
            if (cancel) { cancellation.Cancel(); }
            else { throw new IOException("injected destination audit/readback boundary failure"); }
        }, phase));
        await f.Store.InitializeAsync(f.Token);
        using (var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.RecordControlIntentAsync(f.Request, f.Token);
            await f.Store.Invoking(store => store.CommitProviderProfile(f.Request, destination, source.Desired, static () => true, cancellation.Token, source).AsTask())
                .Should().ThrowAsync<Exception>();
        }
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        var held = await f.Store.ReadProviderProfile(destination.Session.SessionId, f.Token);
        held.Held.Should().BeTrue();
        held.Revision.Should().Be(phase == 3 ? 1 : 0);
        (await f.Store.ReadProviderProfile(source.Session.SessionId, f.Token)).Should().BeEquivalentTo(source);
    }

    private sealed class SourceCheckpoint(HostId<SessionIdentity> source, int phase) : IHostInteractionTransactionCheckpoint
    {
        internal int Calls { get; private set; }
        public void BeforeAudit(Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction transaction) { }
        public void BeforeCommit(Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction transaction)
        {
            if (++Calls != phase) { return; }
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE session_provider_profile SET revision=revision+2 WHERE session_id=$source;";
            command.Parameters.AddWithValue("$source", source.Value.ToString("D"));
            command.ExecuteNonQuery();
        }
    }
}

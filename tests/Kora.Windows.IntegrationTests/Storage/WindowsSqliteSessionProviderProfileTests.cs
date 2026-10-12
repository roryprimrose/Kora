using AwesomeAssertions;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Windows.Storage;
using Microsoft.Data.Sqlite;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class WindowsSqliteSessionProviderProfileTests
{
    [Fact]
    public async Task ExactDispositionInventoriesPolicyAndVerifiesDeletionWithoutChangingAppOrIndependentAudit()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var session = f.Request.SessionId;
        var original = await f.Tasks.ReadTaskAsync(f.Request.TaskId, f.Token);
        await f.RunAsync(() => f.Tasks.CommitAsync(original!.Next(HostTaskState.Succeeded), original.Revision.Value, f.Token).AsTask());
        var expected = await f.Store.ReadProviderProfile(session, f.Token);
        f.Request = new(new(Guid.NewGuid()), session, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        using (var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.RecordControlIntentAsync(f.Request, f.Token);
            await f.Store.CommitProviderProfile(f.Request, expected, new(ProviderService.Claude), static () => true, f.Token);
        }
        var preview = await f.Store.PreviewDispositionAsync(session, expected.Session.Generation, 0, f.Token);
        preview.ProviderProfiles.Should().Be(1);
        var audits = f.Count("security_audit_events");
        f.Request = new(new(Guid.NewGuid()), session, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        using (var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.RecordControlIntentAsync(f.Request, f.Token);
            var receipt = await f.Store.DisposeSessionAsync(f.Request, preview, static () => true, f.Token);
            receipt.Removed.ProviderProfiles.Should().Be(1);
        }
        f.Count("session_provider_profile").Should().Be(0);
        f.Count("security_audit_events").Should().BeGreaterThan(audits);
        (await f.Store.ReadProviderProfile(session, f.Token)).Session.IsActive.Should().BeFalse();
        new FileInfo(f.DatabasePath + "-journal").Length.Should().Be(0);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
    }

    [Fact]
    public async Task MissingPrivateAuthorityNeverCreatesReplacementOrInheritsEmptyPolicy()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        File.Move(f.DatabasePath, f.DatabasePath + ".missing");
        await f.Store.Invoking(store => store.ReadProviderProfile(f.Request.SessionId, f.Token).AsTask()).Should().ThrowAsync<InvalidDataException>();
        File.Exists(f.DatabasePath).Should().BeFalse();
    }
    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task ExactPrivateSessionCommitRetainsSparseBytesAuditAndTerminalControlAcrossReload(RequestOrigin origin)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var session = f.Request.SessionId;
        var initial = await f.Store.ReadProviderProfile(session, f.Token);
        initial.Revision.Should().Be(0);
        initial.Desired.Serialize().Should().Be(SessionProviderOverrides.Inherit.Serialize());
        initial.Held.Should().BeFalse();
        f.Request = new(new(Guid.NewGuid()), session, new(Guid.NewGuid()), origin);
        using (var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.RecordControlIntentAsync(f.Request, f.Token);
            var saved = await f.Store.CommitProviderProfile(f.Request, initial, new(ProviderService.Claude), static () => true, f.Token);
            saved.Revision.Should().Be(1);
            saved.Desired.Service.Should().Be(ProviderService.Claude);
            saved.Held.Should().BeFalse();
            var task = await f.Store.ReadTaskAsync(session, f.Request.TaskId, f.Token);
            task!.Task.State.Should().Be(HostTaskState.Succeeded);
        }
        var audit = ReadAudit(f);
        audit.Should().Contain("session.provider-profile").And.Contain("session-provider-profile");
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        var restarted = await f.Store.ReadProviderProfile(session, f.Token);
        restarted.Desired.Serialize().Should().Be(new SessionProviderOverrides(ProviderService.Claude).Serialize());
        restarted.Revision.Should().Be(1);
        restarted.Held.Should().BeFalse();
        f.Count("host_questions").Should().Be(0);
        f.Count("scoped_grants").Should().Be(0);
    }

    [Theory]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("revision")]
    [InlineData("policy")]
    [InlineData("held")]
    [InlineData("admission")]
    [InlineData("origin")]
    public async Task StaleOrForeignPolicyNeverCommitsToAnotherSession(string drift)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var initial = await f.Store.ReadProviderProfile(f.Request.SessionId, f.Token);
        var expected = drift switch
        {
            "session" => initial with { Session = initial.Session with { SessionId = new(Guid.NewGuid()) } },
            "generation" => initial with { Session = initial.Session with { Generation = new(2) } },
            "revision" => initial with { Revision = 1 },
            "policy" => initial with { Desired = new(ProviderService.Codex) },
            "held" => initial with { Held = true },
            _ => initial,
        };
        f.Request = new(new(Guid.NewGuid()), f.Request.SessionId, new(Guid.NewGuid()),
            drift is "origin" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RecordControlIntentAsync(f.Request, f.Token);
        await f.Store.Invoking(store => store.CommitProviderProfile(f.Request, expected, new(ProviderService.Claude),
            () => drift is not "admission", f.Token).AsTask()).Should().ThrowAsync<InvalidOperationException>();
        f.Count("session_provider_profile").Should().Be(0);
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public async Task FailureOrCancellationAfterRequestedCommitRetainsDurableHoldWithoutInheritanceFallback(bool cancel, int phase)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var expected = await f.Store.ReadProviderProfile(f.Request.SessionId, f.Token);
        using var cancellation = new CancellationTokenSource();
        var checkpoint = new Checkpoint(() => { if (cancel) { cancellation.Cancel(); } else { throw new IOException("injected commit failure"); } }, phase);
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        f.Request = new(new(Guid.NewGuid()), f.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        using (var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.RecordControlIntentAsync(f.Request, f.Token);
            await f.Store.Invoking(store => store.CommitProviderProfile(f.Request, expected, new(ProviderService.Claude),
                static () => true, cancellation.Token).AsTask()).Should().ThrowAsync<Exception>();
        }
        f.Count("session_provider_profile").Should().Be(phase == 3 ? 1 : 0);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        var held = await f.Store.ReadProviderProfile(f.Request.SessionId, f.Token);
        held.Held.Should().BeTrue();
        if (phase == 3) { held.Desired.Service.Should().Be(ProviderService.Claude); }
        ReadAudit(f).Should().Contain("session.provider-profile");
    }

    [Fact]
    public async Task SchemaEightMigrationPreservesExactAuditHistoryAndLegacyInheritWithoutWritingPolicies()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var before = ReadAudit(f);
        f.DropEmptyConversationSchemaForLegacy();
        f.Mutate("DROP TABLE session_queue_order; DROP TABLE session_provider_profile; PRAGMA user_version=8;");
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        ReadAudit(f).Should().Be(before);
        f.Count("session_provider_profile").Should().Be(0);
        var profile = await f.Store.ReadProviderProfile(f.Request.SessionId, f.Token);
        profile.Desired.Serialize().Should().Be(SessionProviderOverrides.Inherit.Serialize());
        profile.Held.Should().BeFalse();
    }

    [Theory]
    [InlineData("payload")]
    [InlineData("revision")]
    [InlineData("generation")]
    [InlineData("missing")]
    [InlineData("downgrade")]
    public async Task MalformedOrUnauditedPolicyBytesAndDowngradeFailClosed(string corruption)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var expected = await f.Store.ReadProviderProfile(f.Request.SessionId, f.Token);
        f.Request = new(new(Guid.NewGuid()), f.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        using (var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.RecordControlIntentAsync(f.Request, f.Token);
            await f.Store.CommitProviderProfile(f.Request, expected, new(ProviderService.Claude), static () => true, f.Token);
        }
        f.Mutate(corruption switch
        {
            "payload" => "UPDATE session_provider_profile SET payload='{}';",
            "revision" => "UPDATE session_provider_profile SET revision=2;",
            "generation" => "UPDATE session_provider_profile SET generation=2;",
            "missing" => "DELETE FROM session_provider_profile;",
            _ => "DROP TABLE session_queue_order; DROP TABLE session_provider_profile; PRAGMA user_version=8;",
        });
        f.Reopen();
        await f.Store.Invoking(store => store.InitializeAsync(f.Token).AsTask()).Should().ThrowAsync<InvalidDataException>();
    }

    private static string ReadAudit(InteractionStorageFixture f)
    {
        using var connection = f.OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT group_concat(envelope,'') FROM security_audit_events ORDER BY sequence;";
        return (string)command.ExecuteScalar()!;
    }

    private sealed class Checkpoint(Action fail, int phase = 2) : IHostInteractionTransactionCheckpoint
    {
        private int commits;
        public void BeforeAudit(SqliteConnection connection, SqliteTransaction transaction) { }
        public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction)
        {
            if (++commits == phase) { fail(); }
        }
    }
}

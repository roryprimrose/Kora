using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.Storage;
using Microsoft.Data.Sqlite;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SchemaElevenMigrationPreservesExactOriginalAndAuditBytesAndAdmitsTerminalRevisionThree(bool interruptedMigration)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await FinishInitial(fixture);
        var original = await Submit(fixture, fixture.Request.SessionId, "legacy original bytes");
        var audits = AuditText(fixture);
        fixture.Mutate("ALTER TABLE conversation_messages RENAME TO migration_fixture_messages; "
            + HostInteractionSchema.LegacyConversationSchema[1]
            + "; INSERT INTO conversation_messages SELECT * FROM migration_fixture_messages; "
            + "DROP TABLE migration_fixture_messages; PRAGMA user_version=11;");
        if (interruptedMigration)
        {
            fixture.Reopen(new AnswerCheckpoint(static () => { }, () => throw new IOException("migration interrupted")));
            await fixture.Store.Invoking(store => store.InitializeAsync(fixture.Token).AsTask()).Should().ThrowAsync<IOException>();
            using var unchanged = new SqliteConnection("Data Source=" + fixture.DatabasePath + ";Pooling=False");
            unchanged.Open();
            using var version = unchanged.CreateCommand();
            version.CommandText = "PRAGMA user_version;";
            version.ExecuteScalar().Should().Be(11L);
        }
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        AuditText(fixture).Should().Be(audits);
        (await fixture.Store.ReadConversationMessageAsync(original.Message.Request.SessionId,
            original.Message.Request.RequestId, fixture.Token)).Should().Be(original.Message);
        var request = Fresh(fixture.Request.SessionId);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await fixture.Store.RetainConversationMessageAsync(request, new(1), 0, "new original", static () => true, fixture.Token);
        await fixture.Store.BeginConversationExecutionAsync(request, provenance, 0, static () => true, fixture.Token);
        var completed = await fixture.Store.CompleteConversationExecutionAsync(request,
            new(ModelTurnOutcome.Succeeded, ModelTurnReason.None, provenance, new("genuine answer", null)),
            static () => true, fixture.Token);
        completed.Message.Revision.Should().Be(3);
        completed.Message.AssistantResponse!.Body.Should().Be("genuine answer");
        completed.Task.State.Should().Be(HostTaskState.Succeeded);
    }
}

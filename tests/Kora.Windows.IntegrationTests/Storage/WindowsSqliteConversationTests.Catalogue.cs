using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteConversationTests
{
    [Fact]
    public async Task SavedDraftRevisionByteAndScalarRangesComposeWithEveryMetadataQueryAndLifecycleWithoutBodyReads()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var saved = await Save(f, await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "🙂");
        saved = await Save(f, saved, "🙂");
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        await service.RenameAsync(saved.Session.SessionId, new(1), 0, new("private needle"),
            RequestOrigin.LocalUi, f.Token);
        using var viewer = HostActivity.BeginRoot(Fresh(f.Request.SessionId), HostActivityLayer.Desktop, HostOperation.Request);
        foreach (var filter in new[]
        {
            SavedDraftMetadataFilter.Any, new(2, null, null), new(null, 4, null),
            new(null, null, 4), new(2, 4, 4), new(null, 0, 16384),
            new(1, null, null), new(null, 5, null), new(null, null, 3), new(null, 0, 0),
            new(2, 4, 4, 1, 1), new(null, null, null, 1, null), new(null, null, null, null, 1),
            new(2, 4, 4, 0, 4096), new(null, null, null, 2, null), new(null, null, null, null, 0),
            new(2, 4, 4, 0, 0), new(null, null, null, 4096, 4096),
        })
        {
            foreach (var lifecycle in Enum.GetValues<SessionListFilter>())
            {
                foreach (var kind in new SessionListSearchKind?[] { null, SessionListSearchKind.NameSubstring, SessionListSearchKind.ExactId })
                {
                    var query = kind is null ? null : new SessionListSearch(kind.Value, lifecycle,
                        kind == SessionListSearchKind.NameSubstring ? "needle" : saved.Session.SessionId.Value.ToString("D"));
                    var page = await f.Store.ReadSavedDraftPage(lifecycle, null, 1, () => true, f.Token, query, filter);
                    page.MetadataFilter.Should().Be(filter);
                    page.Scanned.Should().Be(1);
                    page.Next.Should().BeNull();
                    var matches = lifecycle != SessionListFilter.Done && filter.Matches(new(Guid.NewGuid(), new(Guid.NewGuid()),
                        saved.Session, null, 2, 4, 1, 1, 1, new string('a', 64)));
                    page.Records.Length.Should().Be(matches ? 1 : 0);
                    JsonSerializer.Serialize(page).Should().NotContain("\"Body\"").And.NotContain("🙂");
                }
            }
        }
        saved = await Save(f, saved, new string('é', 4096));
        var bytes = Encoding.UTF8.GetByteCount(saved.Draft.Body!);
        var boundary = await f.Store.ReadSavedDraftPage(SessionListFilter.All, null, 1, () => true, f.Token,
            metadataFilter: new(saved.Draft.Revision, bytes, bytes, 4096, 4096));
        var boundaryRow = boundary.Records.Should().ContainSingle().Which;
        boundaryRow.Utf8Bytes.Should().Be(bytes);
        boundaryRow.UnicodeScalars.Should().Be(4096);
        var control = Fresh(saved.Session.SessionId);
        using (var root = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.RecordControlIntentAsync(control, f.Token);
            await f.Store.ChangeIdleLifecycleAsync(control, new(1), false, () => true, f.Token);
            var task = (await f.Tasks.ReadTaskAsync(control.TaskId, f.Token))!;
            await f.Tasks.CommitAsync(task.Next(HostTaskState.Succeeded), task.Revision.Value, f.Token);
        }
        foreach (var lifecycle in Enum.GetValues<SessionListFilter>())
        {
            foreach (var kind in new SessionListSearchKind?[] { null, SessionListSearchKind.NameSubstring, SessionListSearchKind.ExactId })
            {
                var query = kind is null ? null : new SessionListSearch(kind.Value, lifecycle,
                    kind == SessionListSearchKind.NameSubstring ? "needle" : saved.Session.SessionId.Value.ToString("D"));
                var page = await f.Store.ReadSavedDraftPage(lifecycle, null, 1, () => true, f.Token, query,
                    new(saved.Draft.Revision, bytes, bytes, 4096, 4096));
                page.Records.Length.Should().Be(lifecycle == SessionListFilter.Active ? 0 : 1);
                page.Records.Should().OnlyContain(row => !row.Session.IsActive && row.UnicodeScalars == 4096);
            }
        }
    }

    [Fact]
    public async Task SavedDraftSearchUsesOnlyCurrentNameOrSeparateExactIdentityNeverBodyOrImplicitRetargeting()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var first = await Save(f, await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "PRIVATE_BODY_NEEDLE_88231");
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        await FinishInitial(f);
        var second = await Save(f, await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "second saved body");
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        await service.RenameAsync(first.Session.SessionId, new(1), 0, new(second.Session.SessionId.Value.ToString("D")),
            RequestOrigin.LocalUi, f.Token);
        using var viewer = HostActivity.BeginRoot(Fresh(first.Session.SessionId), HostActivityLayer.Desktop, HostOperation.Request);
        async Task<SavedDraftPage> Read(SessionListSearchKind kind, string text, SessionListFilter filter = SessionListFilter.All) =>
            await f.Store.ReadSavedDraftPage(filter, null, 1, () => true, f.Token, new(kind, filter, text));
        (await Read(SessionListSearchKind.NameSubstring, second.Session.SessionId.Value.ToString("D")))
            .Records.Should().ContainSingle().Which.Session.SessionId.Should().Be(first.Session.SessionId);
        var exact = await Read(SessionListSearchKind.ExactId, second.Session.SessionId.Value.ToString("D"));
        exact.Records.Should().ContainSingle().Which.Session.SessionId.Should().Be(second.Session.SessionId,
            "an ID-shaped name never establishes identity and an unnamed session remains exactly searchable");
        exact.Scanned.Should().Be(1);
        (await Read(SessionListSearchKind.NameSubstring, "PRIVATE_BODY_NEEDLE_88231")).Records.Should().BeEmpty();
        var unknown = await Read(SessionListSearchKind.ExactId, Guid.NewGuid().ToString("D"));
        unknown.Records.Should().BeEmpty();
        unknown.Next.Should().BeNull();
        unknown.Scanned.Should().Be(0);
        await service.RenameAsync(first.Session.SessionId, new(1), 1, new("Private café 🙂 100%_"),
            RequestOrigin.LocalUi, f.Token);
        var audits = f.Count("security_audit_events");
        var tasks = f.Count("host_tasks");
        var clock = await f.Store.ReadRetentionAsync(first.Session.SessionId, f.Token);
        var match = await Read(SessionListSearchKind.NameSubstring, "café 🙂 100%_");
        match.Records.Should().ContainSingle().Which.Session.SessionId.Should().Be(first.Session.SessionId);
        (await Read(SessionListSearchKind.NameSubstring, "Café")).Records.Should().BeEmpty();
        (await Read(SessionListSearchKind.NameSubstring, "café", SessionListFilter.Done)).Records.Should().BeEmpty();
        (await Read(SessionListSearchKind.ExactId, first.Session.SessionId.Value.ToString("D"), SessionListFilter.Active))
            .Records.Should().ContainSingle();
        JsonSerializer.Serialize(match).Should().NotContain("PRIVATE_BODY").And.NotContain("\"Body\"")
            .And.NotContain("\"QueryText\"", "only query kind/digest, not a query object, is transported");
        f.Count("security_audit_events").Should().Be(audits);
        f.Count("host_tasks").Should().Be(tasks);
        (await f.Store.ReadRetentionAsync(first.Session.SessionId, f.Token)).Should().Be(clock);
        await Save(f, second, null);
        var cleared = await Read(SessionListSearchKind.ExactId, second.Session.SessionId.Value.ToString("D"));
        cleared.Records.Should().BeEmpty();
        cleared.Next.Should().BeNull();
        cleared.Scanned.Should().Be(1);
        var changed = () => f.Store.ResolveSavedDraft(match.Records.Single(), () => true, f.Token).AsTask();
        await changed.Should().ThrowAsync<InvalidOperationException>();
        f.Count("conversation_messages").Should().Be(0);
        AuditText(f).Should().NotContain("PRIVATE_BODY_NEEDLE_88231");
    }

    [Fact]
    public async Task SavedDraftCatalogueProjectsOnlyBoundedSqlMetadataAndExactNavigationDoesNotReadBodyOrRenewDone()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        const string body = "PRIVATE_SAVED_BODY_827364🙂";
        var saved = await Save(f, await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), body);
        var control = Fresh(f.Request.SessionId);
        using (var root = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.RecordControlIntentAsync(control, f.Token);
            await f.Store.ChangeIdleLifecycleAsync(control, new(1), false, () => true, f.Token);
            var task = (await f.Tasks.ReadTaskAsync(control.TaskId, f.Token))!;
            await f.Tasks.CommitAsync(task.Next(HostTaskState.Succeeded), task.Revision.Value, f.Token);
        }
        var projection = new DraftMetadataCheckpoint();
        f.Reopen(projection);
        await f.Store.InitializeAsync(f.Token);
        var clock = await f.Store.ReadRetentionAsync(saved.Session.SessionId, f.Token);
        var tasks = f.Count("host_tasks");
        var audits = f.Count("security_audit_events");
        using var viewer = HostActivity.BeginRoot(Fresh(f.Request.SessionId), HostActivityLayer.Desktop, HostOperation.Request);
        projection.Read = command =>
        {
            using var row = command.ExecuteReader();
            row.FieldCount.Should().Be(13);
            row.Read().Should().BeTrue();
            for (var column = 0; column < row.FieldCount; column++)
            {
                row.GetValue(column).ToString().Should().NotContain(body);
                row.GetName(column).Should().NotBe("payload").And.NotBe("Body");
            }
            row.GetInt64(6).Should().Be(Encoding.UTF8.GetByteCount(body));
            row.GetInt64(7).Should().Be(body.EnumerateRunes().Count());
        };
        var page = await f.Store.ReadSavedDraftPage(SessionListFilter.All, null, 1, () => true, f.Token);
        var exact = page.Records.Single();
        exact.Session.IsActive.Should().BeFalse();
        exact.Session.Generation.Value.Should().Be(2);
        exact.DraftRevision.Should().Be(saved.Draft.Revision);
        var target = await f.Store.ResolveSavedDraft(exact, () => true, f.Token);
        target.Authority.Should().Be(exact.Session);
        JsonSerializer.Serialize(page).Should().NotContain("PRIVATE_SAVED_BODY").And.NotContain("\"Body\"");
        (await f.Store.ReadSavedDraftPage(SessionListFilter.Active, null, 1, () => true, f.Token)).Records.Should().BeEmpty();
        (await f.Store.ReadSavedDraftPage(SessionListFilter.Done, null, 1, () => true, f.Token)).Records.Should().ContainSingle();
        f.Count("host_tasks").Should().Be(tasks);
        f.Count("security_audit_events").Should().Be(audits);
        (await f.Store.ReadRetentionAsync(exact.Session.SessionId, f.Token)).Should().Be(clock);
        projection.Calls.Should().Be(4);
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("replace")]
    [InlineData("rename")]
    [InlineData("control")]
    [InlineData("owner")]
    [InlineData("cancel")]
    [InlineData("restart")]
    public async Task SavedDraftExactNavigationRefusesStaleOrRevokedSources(string boundary)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var saved = await Save(f, await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "ordinary saved draft");
        using var viewer = HostActivity.BeginRoot(Fresh(f.Request.SessionId), HostActivityLayer.Desktop, HostOperation.Request);
        var exact = (await f.Store.ReadSavedDraftPage(SessionListFilter.All, null, 1, () => true, f.Token,
            metadataFilter: new(saved.Draft.Revision, 1, 16384, 1, 4096))).Records.Single();
        if (boundary is "clear" or "replace") { await Save(f, saved, boundary is "clear" ? null : "new saved draft"); }
        if (boundary is "rename")
        {
            var control = Fresh(exact.Session.SessionId);
            using var root = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Request);
            await f.Store.RecordControlIntentAsync(control, f.Token);
            await f.Store.RenameSessionAsync(control, exact.Session.Generation, 0, new("new private name"), () => true, f.Token);
        }
        if (boundary is "restart") { f.Reopen(); await f.Store.InitializeAsync(f.Token); }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        if (boundary is "cancel") { cancellation.Cancel(); }
        var selected = boundary is "control" ? exact with { DraftRevision = exact.DraftRevision + 1 } : exact;
        var navigation = () => f.Store.ResolveSavedDraft(selected, () => boundary is not "owner", cancellation.Token).AsTask();
        await navigation.Should().ThrowAsync<Exception>();
        f.Count("conversation_messages").Should().Be(0);
    }

    [Theory]
    [InlineData("missing-row")]
    [InlineData("missing-audit")]
    [InlineData("foreign-session")]
    [InlineData("body-type")]
    [InlineData("body-length")]
    [InlineData("revision-type")]
    [InlineData("version")]
    [InlineData("task")]
    [InlineData("null-save")]
    [InlineData("session")]
    public async Task SavedDraftCatalogueReportsBrokenAuthorityAsUnavailableNotEmpty(string boundary)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        await Save(f, await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "ordinary saved draft");
        using (var connection = f.OpenRaw())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys=OFF;";
            command.ExecuteNonQuery();
            command.CommandText = boundary switch
            {
                "missing-row" => "DELETE FROM conversation_drafts;",
                "missing-audit" => "DELETE FROM security_audit_events WHERE sequence=(SELECT audit_sequence FROM conversation_drafts);",
                "foreign-session" => "UPDATE conversation_drafts SET payload=json_set(payload,'$.SessionId.Value','11111111-1111-1111-1111-111111111111');",
                "body-type" => "UPDATE conversation_drafts SET payload=json_set(payload,'$.Body',17);",
                "body-length" => "UPDATE conversation_drafts SET payload=json_set(payload,'$.Body',replace(hex(zeroblob(9000)),'00','a'));",
                "revision-type" => "UPDATE conversation_drafts SET payload=json_set(payload,'$.Revision','1');",
                "version" => "UPDATE conversation_drafts SET payload=json_set(payload,'$.Version',2);",
                "null-save" => "UPDATE conversation_drafts SET payload=json_set(payload,'$.Body',NULL);",
                "session" => "DELETE FROM work_sessions;",
                _ => "DELETE FROM host_tasks WHERE task_id=(SELECT json_extract(envelope,'$.Request.TaskId.Value') FROM security_audit_events WHERE sequence=(SELECT audit_sequence FROM conversation_drafts));",
            };
            command.ExecuteNonQuery();
        }
        using var viewer = HostActivity.BeginRoot(Fresh(f.Request.SessionId), HostActivityLayer.Desktop, HostOperation.Request);
        var read = () => f.Store.ReadSavedDraftPage(SessionListFilter.All, null, 1, () => true, f.Token).AsTask();
        await read.Should().ThrowAsync<Exception>();
        var queryRead = () => f.Store.ReadSavedDraftPage(SessionListFilter.All, null, 1, () => true, f.Token,
            new(SessionListSearchKind.ExactId, SessionListFilter.All, f.Request.SessionId.Value.ToString("D"))).AsTask();
        await queryRead.Should().ThrowAsync<Exception>("metadata queries retain every original source/provenance check");
        var metadataRead = () => f.Store.ReadSavedDraftPage(SessionListFilter.Done, null, 1, () => true, f.Token,
            metadataFilter: new(long.MaxValue, 0, 0, 0, 0)).AsTask();
        await metadataRead.Should().ThrowAsync<Exception>("irrelevant metadata rows never bypass validation");
    }

    [Fact]
    public async Task SavedDraftCatalogueExcludesNullEmptyNeverSavedAndQuestionOnlyWithoutSchemaChanges()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        using var viewer = HostActivity.BeginRoot(Fresh(f.Request.SessionId), HostActivityLayer.Desktop, HostOperation.Request);
        var empty = await f.Store.ReadSavedDraftPage(SessionListFilter.All, null, 1, () => true, f.Token);
        empty.Records.Should().BeEmpty();
        empty.Scanned.Should().Be(0);
        var saved = await Save(f, await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "ordinary");
        var cleared = await Save(f, saved, null);
        var nullPage = await f.Store.ReadSavedDraftPage(SessionListFilter.All, null, 1, () => true, f.Token);
        nullPage.Records.Should().BeEmpty();
        nullPage.Scanned.Should().Be(1);
        await Save(f, cleared, string.Empty);
        (await f.Store.ReadSavedDraftPage(SessionListFilter.All, null, 1, () => true, f.Token)).Records.Should().BeEmpty();
        using var connection = f.OpenRaw();
        using var schema = connection.CreateCommand();
        schema.CommandText = "SELECT sql FROM sqlite_master WHERE name='conversation_drafts';";
        schema.ExecuteScalar()!.ToString().Should().NotContain("catalogue").And.NotContain("captured");
    }

    [Fact]
    public async Task SavedDraftCatalogueDoesNotDiscoverIndependentQuestionAnswerDrafts()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var question = await f.RunAsync(() => f.Questions.CreateAsync(f.Request,
            new("Question only", Kora.Core.Interaction.QuestionKind.Text, [], maximumTextLength: 64),
            f.Time.Now.AddMinutes(5), f.Token));
        await f.RunAsync(() => f.Questions.DraftAsync(question.Question!.Key,
            new([], "PRIVATE_QUESTION_DRAFT"), Kora.Core.Hosting.RequestOrigin.LocalUi, f.Token));
        using var viewer = HostActivity.BeginRoot(Fresh(f.Request.SessionId), HostActivityLayer.Desktop, HostOperation.Request);
        var page = await f.Store.ReadSavedDraftPage(SessionListFilter.All, null, 1, () => true, f.Token);
        page.Records.Should().BeEmpty();
        page.Scanned.Should().Be(0);
        f.Count("conversation_drafts").Should().Be(0);
        f.Count("host_questions").Should().Be(1);
    }

    private sealed class DraftMetadataCheckpoint : IHostInteractionTransactionCheckpoint
    {
        internal Action<SqliteCommand>? Read { get; set; }
        internal int Calls { get; private set; }
        public void BeforeAudit(SqliteConnection connection, SqliteTransaction transaction) { }
        public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction) { }
        public void BeforeConversationDraftMetadataRead(SqliteCommand command) { Calls++; Read?.Invoke(command); }
    }
}

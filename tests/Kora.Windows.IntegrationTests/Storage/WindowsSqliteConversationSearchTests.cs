using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class WindowsSqliteConversationSearchTests
{
    [Fact]
    public async Task ActualHundredMessageBoundCrossesSourcePagesWithoutInventingMatchesOrChangingActivity()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var session = f.Request.SessionId;
        for (var index = 0; index < ConversationText.MaximumSessionMessages; index++)
        { await WindowsSqliteConversationTests.Submit(f, session, index % 2 == 0 ? "word original" : "other"); }
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        var before = await f.Store.ReadRetentionAsync(session, f.Token);
        var audits = WindowsSqliteConversationTests.AuditText(f);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var first = await service.SearchConversationAsync(session, "word", null, 25, f.Token);
        var next = await service.SearchConversationAsync(session, "WORD", first.Next, 25, f.Token);
        var tail = await service.SearchConversationAsync(session, "word", next.Next, 25, f.Token);
        first.Messages.Should().HaveCount(25);
        next.Messages.Should().HaveCount(25);
        tail.Messages.Should().BeEmpty();
        tail.Scanned.Should().Be(1);
        tail.Next.Should().BeNull();
        first.Messages.Concat(next.Messages).Select(row => row.Sequence)
            .Should().Equal(Enumerable.Range(0, 50).Select(index => (long)index * 2 + 1));
        var all = await service.SearchConversationAsync(session, "word", null, 50, f.Token);
        all.Messages.Should().HaveCount(50);
        (await service.SearchConversationAsync(session, "absent", null, 25, f.Token)).Scanned.Should().Be(100);
        (await f.Store.ReadRetentionAsync(session, f.Token)).Should().Be(before);
        WindowsSqliteConversationTests.AuditText(f).Should().Be(audits);
    }

    [Fact]
    public async Task RejectedCredentialRequestHasNoBodyToMatchAndPassiveSearchNeverAddsIntent()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var session = f.Request.SessionId;
        const string candidate = "api_key=syntheticCredentialRejectedMarker";
        var saved = await f.Store.ReadConversationDraftAsync(session, f.Token);
        await f.Invoking(fixture => WindowsSqliteConversationTests.Save(fixture, saved, candidate))
            .Should().ThrowAsync<ConversationCredentialException>();
        var audits = WindowsSqliteConversationTests.AuditText(f);
        var tasks = f.Count("host_tasks");
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        foreach (var query in new[] { "syntheticCredentialRejectedMarker", "Denied", "credential" })
        {
            var result = await service.SearchConversationAsync(session, query, null, 25, f.Token);
            result.Messages.Should().BeEmpty();
            result.Scanned.Should().Be(0);
        }
        f.Count("conversation_messages").Should().Be(0);
        f.Count("host_tasks").Should().Be(tasks);
        WindowsSqliteConversationTests.AuditText(f).Should().Be(audits).And.NotContain("syntheticCredentialRejectedMarker");
    }

    [Fact]
    public async Task ActualRetainedUserBodiesAndTypedHostOutcomesSearchWithoutAnyContentOrAuditMutation()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var session = f.Request.SessionId;
        const string body = "  actual USER café e\u0301🙂\r\n ending  ";
        var original = await WindowsSqliteConversationTests.Submit(f, session, body);
        await WindowsSqliteConversationTests.Save(f, await f.Store.ReadConversationDraftAsync(session, f.Token),
            "saved-only absentmarker");
        var before = await f.Store.ReadRetentionAsync(session, f.Token);
        var audit = WindowsSqliteConversationTests.AuditText(f);
        var counts = new[] { f.Count("host_tasks"), f.Count("conversation_messages"), f.Count("conversation_drafts"),
            f.Count("security_audit_events"), f.Count("host_questions"), f.Count("scoped_grants") };
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, access);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        foreach (var query in new[] { "CAFE\u0301", "Unavailable", "UnregisteredModel" })
        {
            var page = await service.SearchConversationAsync(session, query, null, 25, f.Token);
            page.Messages.Single().Should().Be(original.Message);
            page.Messages.Single().Body.Should().Be(body);
            page.Scanned.Should().Be(1);
            page.Next.Should().BeNull();
        }
        foreach (var query in new[] { "absentmarker", "caf", "' OR 1=1; DROP TABLE conversation_messages;",
            session.Value.ToString("D"), "Succeeded" })
        {
            (await service.SearchConversationAsync(session, query, null, 25, f.Token)).Messages.Should().BeEmpty();
        }
        (await f.Store.ReadRetentionAsync(session, f.Token)).Should().Be(before);
        WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
        new[] { f.Count("host_tasks"), f.Count("conversation_messages"), f.Count("conversation_drafts"),
            f.Count("security_audit_events"), f.Count("host_questions"), f.Count("scoped_grants") }.Should().Equal(counts);
    }

    [Fact]
    public async Task ActualByteBoundedPagesPreserveCompleteBodyOriginalSnapshotAndDeferredMatchingRow()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var session = f.Request.SessionId;
        var body = "word " + new string('\u0800', 4091);
        for (var index = 0; index < 5; index++) { await WindowsSqliteConversationTests.Submit(f, session, body); }
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var page = await service.SearchConversationAsync(session, "word", null, 50, f.Token);
        page.Messages.Should().HaveCount(2);
        page.Scanned.Should().Be(2);
        var rows = page.Messages.ToList();
        await WindowsSqliteConversationTests.Submit(f, session, "word later append excluded");
        while (page.Next is { } cursor)
        {
            page = await service.SearchConversationAsync(session, "WORD word", cursor, 50, f.Token);
            page.Ceiling.Should().Be(5);
            ConversationSearchPage.Serialize(page).Length.Should().BeLessThanOrEqualTo(65536);
            rows.AddRange(page.Messages);
        }
        rows.Select(message => message.Sequence).Should().Equal(1, 2, 3, 4, 5);
        rows.Should().OnlyContain(message => message.Body == body);
    }

    [Fact]
    public async Task TerminalUpdateInvalidatesEvenPreviouslyExhaustedSearchSnapshotAndFreshSearchObservesTruth()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var session = f.Request.SessionId;
        await WindowsSqliteConversationTests.Submit(f, session, "word first");
        var pending = WindowsSqliteConversationTests.Fresh(session);
        using var root = HostActivity.BeginRoot(pending, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RetainConversationMessageAsync(pending, new(1), 0, "word pending", static () => true, f.Token);
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        var page = await service.SearchConversationAsync(session, "word", null, 1, f.Token);
        await f.Store.CompleteConversationSubmissionAsync(pending, Kora.Core.Dependencies.ModelTurnOutcome.Unavailable,
            Kora.Core.Dependencies.ModelTurnReason.UnregisteredModel, static () => true, f.Token);
        await service.Invoking(reader => reader.SearchConversationAsync(session, "word", page.Next, 1, f.Token))
            .Should().ThrowAsync<ConversationSnapshotChangedException>();
        var fresh = await service.SearchConversationAsync(session, "Unavailable", null, 25, f.Token);
        fresh.Messages.Should().HaveCount(2);
        fresh.Next.Should().BeNull();
    }

    [Fact]
    public async Task DoneRemainsReadableWithoutResumeAndOldGenerationOrWrongSessionCursorCannotDisclose()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var session = f.Request.SessionId;
        await WindowsSqliteConversationTests.Submit(f, session, "word original");
        await WindowsSqliteConversationTests.Submit(f, session, "word second");
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        ConversationSearchPage first;
        using (var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request))
        { first = await service.SearchConversationAsync(session, "word", null, 1, f.Token); }
        await service.ChangeLifecycleAsync(session, new(1), false, RequestOrigin.LocalUi, f.Token);
        using var read = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        await service.Invoking(reader => reader.SearchConversationAsync(session, "word", first.Next, 1, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        var done = await service.SearchConversationAsync(session, "word", null, 25, f.Token);
        done.Session.IsActive.Should().BeFalse();
        done.Session.Generation.Value.Should().Be(2);
        done.Messages.Should().HaveCount(2).And.OnlyContain(message => message.Generation.Value == 1);
        var wrong = first.Next! with { Conversation = first.Next!.Conversation with { SessionId = new(Guid.NewGuid()) } };
        await service.Invoking(reader => reader.SearchConversationAsync(session, "word", wrong, 1, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        (await f.Store.ReadMetadataAsync(session, f.Token)).Authority.Should().Be(done.Session);
    }
}

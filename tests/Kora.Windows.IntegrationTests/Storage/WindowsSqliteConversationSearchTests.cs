using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed class WindowsSqliteConversationSearchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalRangesUsePrivateExactSessionReadAdmissionWithoutSavedDraftsOrHiddenSessions(bool done)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var session = f.Request.SessionId;
        await WindowsSqliteConversationTests.Submit(f, session, "marker Denied");
        var exact = await WindowsSqliteConversationTests.Submit(f, session, "marker Unavailable");
        await WindowsSqliteConversationTests.Submit(f, session, "marker other");
        await WindowsSqliteConversationTests.Save(f, await f.Store.ReadConversationDraftAsync(session, f.Token), "hidden draft marker");
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, access);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        if (done) { await service.ChangeLifecycleAsync(session, new(1), false, RequestOrigin.LocalUi, f.Token); }
        var retention = await f.Store.ReadRetentionAsync(session, f.Token);
        var audit = WindowsSqliteConversationTests.AuditText(f);
        var range = new ConversationRangeFilter(2, 2, exact.Message.SubmittedAt, exact.Message.SubmittedAt,
            exact.Message.Generation.Value, exact.Message.Generation.Value, exact.Message.Revision, exact.Message.Revision);
        var page = await service.SearchConversationAsync(session, "marker", null, 25, f.Token,
            ConversationSearchRole.All, new(ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel), range);
        page.Messages.Should().Equal(exact.Message);
        page.Scanned.Should().Be(3);
        page.Range.Should().Be(range);
        page.Next.Should().BeNull();
        (await service.SearchConversationAsync(session, "hidden draft", null, 25, f.Token, range: range)).Messages.Should().BeEmpty();
        (await service.SearchConversationAsync(session, "marker", null, 25, f.Token, range: new(100))).Messages.Should().BeEmpty();
        (await f.Store.ReadRetentionAsync(session, f.Token)).Should().Be(retention);
        WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
        access.CanInspect = false;
        await service.Invoking(value => value.SearchConversationAsync(session, "marker", null, 25, f.Token, range: range))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ActualTerminalPredicatesExcludePendingAndStatusMimickingUserWithoutActivityOrAuditMutation()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var session = f.Request.SessionId;
        await WindowsSqliteConversationTests.Submit(f, session, "marker Denied CallerCancelled");
        var pending = WindowsSqliteConversationTests.Fresh(session);
        using var root = HostActivity.BeginRoot(pending, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RetainConversationMessageAsync(pending, new(1), 0, "marker Unavailable UnregisteredModel", static () => true, f.Token);
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        var activity = await f.Store.ReadRetentionAsync(session, f.Token);
        var audit = WindowsSqliteConversationTests.AuditText(f);
        var terminal = new ConversationTerminalFilter(ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel, HostTaskState.Unavailable);
        foreach (var role in Enum.GetValues<ConversationSearchRole>())
        {
            var range = new ConversationRangeFilter(MinimumGeneration: 1, MaximumGeneration: long.MaxValue, MinimumRevision: 2, MaximumRevision: 2);
            var page = await service.SearchConversationAsync(session, "Unavailable", null, 25, f.Token, role, terminal, range);
            page.Messages.Should().HaveCount(role == ConversationSearchRole.USER ? 0 : 1);
            page.Scanned.Should().Be(2);
            page.Role.Should().Be(role);
            page.Terminal.Should().Be(terminal);
            page.Next.Should().BeNull();
            var pendingOnly = await service.SearchConversationAsync(session, "marker", null, 25, f.Token, role,
                range: range with { MinimumRevision = 1, MaximumRevision = 1 });
            pendingOnly.Messages.Should().HaveCount(role == ConversationSearchRole.HOST ? 0 : 1);
            pendingOnly.Messages.Should().OnlyContain(message => message.Revision == 1);
        }
        (await service.SearchConversationAsync(session, "marker", null, 25, f.Token, ConversationSearchRole.All,
            terminal with { Outcome = ModelTurnOutcome.Denied })).Messages.Should().BeEmpty();
        (await f.Store.ReadRetentionAsync(session, f.Token)).Should().Be(activity);
        WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
        f.Count("conversation_messages").Should().Be(2);
    }

    [Theory]
    [InlineData(ConversationSearchRole.All, "Unavailable", 2)]
    [InlineData(ConversationSearchRole.USER, "Unavailable", 1)]
    [InlineData(ConversationSearchRole.HOST, "Unavailable", 2)]
    [InlineData(ConversationSearchRole.USER, "UnregisteredModel", 0)]
    [InlineData(ConversationSearchRole.HOST, "original", 0)]
    public async Task ActualRoleSearchPreservesPrivateSourceBodiesRetentionAndAudit(
        ConversationSearchRole role, string query, int expected)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "Unavailable original");
        await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "other");
        var clock = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
        var audit = WindowsSqliteConversationTests.AuditText(f);
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var page = await service.SearchConversationAsync(f.Request.SessionId, query, null, 25, f.Token, role);
        page.Messages.Should().HaveCount(expected);
        page.Role.Should().Be(role);
        page.Scanned.Should().Be(2);
        page.Next.Should().BeNull();
        (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(clock);
        WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
        f.Count("conversation_messages").Should().Be(2);
    }

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
        var terminal = new ConversationTerminalFilter(ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel, HostTaskState.Unavailable);
        var page = await service.SearchConversationAsync(session, "word", null, 1, f.Token, terminal: terminal);
        await f.Store.CompleteConversationSubmissionAsync(pending, Kora.Core.Dependencies.ModelTurnOutcome.Unavailable,
            Kora.Core.Dependencies.ModelTurnReason.UnregisteredModel, static () => true, f.Token);
        await service.Invoking(reader => reader.SearchConversationAsync(session, "word", page.Next, 1, f.Token, terminal: terminal))
            .Should().ThrowAsync<ConversationSnapshotChangedException>();
        var fresh = await service.SearchConversationAsync(session, "Unavailable", null, 25, f.Token, terminal: terminal);
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
        var terminal = new ConversationTerminalFilter(ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel, HostTaskState.Unavailable);
        ConversationSearchPage first;
        using (var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request))
        { first = await service.SearchConversationAsync(session, "word", null, 1, f.Token, terminal: terminal); }
        await service.ChangeLifecycleAsync(session, new(1), false, RequestOrigin.LocalUi, f.Token);
        using var read = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        await service.Invoking(reader => reader.SearchConversationAsync(session, "word", first.Next, 1, f.Token, terminal: terminal))
            .Should().ThrowAsync<InvalidOperationException>();
        var done = await service.SearchConversationAsync(session, "word", null, 25, f.Token, terminal: terminal);
        done.Session.IsActive.Should().BeFalse();
        done.Session.Generation.Value.Should().Be(2);
        done.Messages.Should().HaveCount(2).And.OnlyContain(message => message.Generation.Value == 1);
        var wrong = first.Next! with { Conversation = first.Next!.Conversation with { SessionId = new(Guid.NewGuid()) } };
        await service.Invoking(reader => reader.SearchConversationAsync(session, "word", wrong, 1, f.Token, terminal: terminal))
            .Should().ThrowAsync<InvalidOperationException>();
        (await f.Store.ReadMetadataAsync(session, f.Token)).Authority.Should().Be(done.Session);
    }
}

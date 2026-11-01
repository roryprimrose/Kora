using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed class WindowsSqliteAnswerReviewTests
{
    [Fact]
    public async Task ExactAcknowledgementSurvivesRestartAndUndoWithoutMarkingOtherConversationsOffscreenOrNewAnswers()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var firstSession = f.Request.SessionId;
        var first = await Answer(f, firstSession, "first");
        var offscreen = await Answer(f, firstSession, "offscreen");
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        await WindowsSqliteConversationTests.FinishInitial(f);
        var other = await Answer(f, f.Request.SessionId, "other topic");
        var target = await f.Store.ReadMetadataAsync(firstSession, f.Token);
        var retention = await f.Store.ReadRetentionAsync(firstSession, f.Token);
        var queue = await f.Store.ReadQueueAsync(firstSession, f.Token);
        var beforeAudit = WindowsSqliteConversationTests.AuditText(f);
        var tasks = f.Count("host_tasks");
        var page = await f.Store.ReadAnswerReviewsAsync(target, ConversationAnswerReviewFilter.NotMarkedReviewed, null, 1, f.Token);
        page.NotMarkedReviewed.Should().Be(2);
        page.Rows.Single().RequestId.Should().Be(first.Request.RequestId);
        page.Next.Should().NotBeNull();
        var exact = await f.Store.ReadAnswerReviewMessageAsync(page, page.Rows[0], f.Token);
        exact.Should().Be(first);
        var next = await f.Store.ReadAnswerReviewsAsync(target, page.Filter, page.Next, 1, f.Token);
        next.Rows.Single().RequestId.Should().Be(offscreen.Request.RequestId);
        f.Count("conversation_answer_reviews").Should().Be(0);
        f.Count("host_tasks").Should().Be(tasks);
        WindowsSqliteConversationTests.AuditText(f).Should().Be(beforeAudit);
        f.Time.Now = f.Time.Now.AddHours(1);
        var saved = await Mark(f, page, page.Rows[0], true);
        saved.ReviewRevision.Should().Be(1);
        saved.MarkedReviewed.Should().BeTrue();
        (await f.Store.ReadRetentionAsync(firstSession, f.Token)).Should().Be(retention);
        (await f.Store.ReadQueueAsync(firstSession, f.Token)).Should().BeEquivalentTo(queue);
        f.Count("conversation_messages").Should().Be(3);
        f.Count("scoped_grants").Should().Be(0);
        f.Count("perpetual_grants").Should().Be(0);
        var newer = await Answer(f, firstSession, "new background answer");
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        page = await f.Store.ReadAnswerReviewsAsync(target, page.Filter, null, 25, f.Token);
        page.NotMarkedReviewed.Should().Be(2);
        page.TotalAnswers.Should().Be(3);
        page.Rows.Select(row => row.RequestId).Should().Equal(offscreen.Request.RequestId, newer.Request.RequestId);
        var otherPage = await f.Store.ReadAnswerReviewsAsync(await f.Store.ReadMetadataAsync(other.Request.SessionId, f.Token),
            page.Filter, null, 25, f.Token);
        otherPage.NotMarkedReviewed.Should().Be(1);
        otherPage.Rows.Single().ReviewRevision.Should().Be(0);
        var all = await f.Store.ReadAnswerReviewsAsync(target, ConversationAnswerReviewFilter.All, null, 25, f.Token);
        await Mark(f, all, all.Rows[0], false);
        (await f.Store.ReadAnswerReviewsAsync(target, page.Filter, null, 25, f.Token)).NotMarkedReviewed.Should().Be(3);
        (await f.Store.ReadConversationMessageAsync(firstSession, first.Request.RequestId, f.Token)).Should().Be(first);
        WindowsSqliteConversationTests.AuditText(f).Should().NotContain("private answer body");
        JsonSerializer.Serialize(page).Should().NotContain("private answer body");
    }

    [Theory]
    [InlineData("new-head")]
    [InlineData("revision")]
    [InlineData("wrong-session")]
    [InlineData("wrong-turn")]
    [InlineData("metadata")]
    [InlineData("removed")]
    [InlineData("control")]
    [InlineData("cancel")]
    public async Task StaleOrUnauthorizedExactChoicesNeverWriteAReviewFlag(string change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await Answer(f, f.Request.SessionId, "original");
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var page = await f.Store.ReadAnswerReviewsAsync(target, ConversationAnswerReviewFilter.All, null, 25, f.Token);
        var row = page.Rows.Single();
        if (change is "new-head") { await Answer(f, f.Request.SessionId, "arrived after query"); }
        if (change is "revision") { row = row with { MessageRevision = row.MessageRevision - 1 }; }
        if (change is "wrong-session") { page = page with { Target = target with { Authority = new(new(Guid.NewGuid()), target.Authority.Generation, true) } }; }
        if (change is "wrong-turn") { row = row with { TurnId = Guid.NewGuid() }; }
        if (change is "metadata") { page = page with { Target = target with { Metadata = new(target.Authority.SessionId, new(1), new("substitute")) } }; }
        if (change is "removed")
        {
            var preview = await f.Store.PreviewDispositionAsync(target.Authority.SessionId, new(1), 0, f.Token);
            var control = WindowsSqliteConversationTests.Fresh(target.Authority.SessionId);
            using var root = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Request);
            await f.Store.RecordControlIntentAsync(control, f.Token);
            await f.Store.DisposeSessionAsync(control, preview, static () => true, f.Token);
        }
        var operation = () => Mark(f, page, row, true, admitted: change is not "control", cancelled: change is "cancel");
        await operation.Should().ThrowAsync<Exception>();
        f.Count("conversation_answer_reviews").Should().Be(0);
    }

    [Theory]
    [InlineData("payload")]
    [InlineData("missing")]
    [InlineData("source-after-budget")]
    public async Task UnknownOrMalformedStateIsUnavailableBeforeFilteringOrPageBudget(string fault)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await Answer(f, f.Request.SessionId, "first");
        var second = await Answer(f, f.Request.SessionId, "second");
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var page = await f.Store.ReadAnswerReviewsAsync(target, ConversationAnswerReviewFilter.All, null, 25, f.Token);
        await Mark(f, page, page.Rows[0], true);
        if (fault is "payload") { f.Mutate("UPDATE conversation_answer_reviews SET payload=json_set(payload,'$.Reviewed',0);"); }
        if (fault is "missing") { f.Mutate("DELETE FROM conversation_answer_reviews;"); }
        if (fault is "source-after-budget")
        {
            f.Mutate("UPDATE conversation_messages SET payload=json_set(payload,'$.Body','substitution') WHERE sequence=2;");
        }
        var read = () => f.Store.ReadAnswerReviewsAsync(target, ConversationAnswerReviewFilter.NotMarkedReviewed, null, 1, f.Token).AsTask();
        await read.Should().ThrowAsync<InvalidDataException>();
        second.Request.SessionId.Should().Be(target.Authority.SessionId);
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("commit-control")]
    [InlineData("readback")]
    public async Task AuditCommitAndReadbackFailuresNeverReportConfirmedSuccess(string failure)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await Answer(f, f.Request.SessionId, "original");
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var page = await f.Store.ReadAnswerReviewsAsync(target, ConversationAnswerReviewFilter.All, null, 25, f.Token);
        var armed = false;
        var live = true;
        f.Reopen(new InteractionTransactionCheckpoint
        {
            Audit = (_, _) => { if (armed && failure is "audit") { throw new IOException("required audit unavailable"); } },
            Commit = (_, _) => { if (armed) { live = failure is not "commit-control"; } },
        });
        await f.Store.InitializeAsync(f.Token);
        var control = WindowsSqliteConversationTests.Fresh(target.Authority.SessionId);
        using var root = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RecordControlIntentAsync(control, f.Token);
        armed = true;
        var admissions = 0;
        bool Admitted()
        {
            admissions++;
            return live && (failure is not "readback" || admissions < 3);
        }
        var operation = () => f.Store.SetAnswerReviewedAsync(control, page, page.Rows[0], true, Admitted, f.Token).AsTask();
        await operation.Should().ThrowAsync<Exception>();
        f.Count("conversation_answer_reviews").Should().Be(failure is "readback" ? 1 : 0);
    }

    [Fact]
    public async Task DispositionRemovesOwnedReviewMetadataAndCopiesAndInvalidatesPreviouslyObservedRows()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await Answer(f, f.Request.SessionId, "delete");
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var page = await f.Store.ReadAnswerReviewsAsync(target, ConversationAnswerReviewFilter.All, null, 25, f.Token);
        await Mark(f, page, page.Rows[0], true);
        var preview = await f.Store.PreviewDispositionAsync(target.Authority.SessionId, new(1), 0, f.Token);
        var control = WindowsSqliteConversationTests.Fresh(target.Authority.SessionId);
        using var root = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RecordControlIntentAsync(control, f.Token);
        await f.Store.DisposeSessionAsync(control, preview, static () => true, f.Token);
        f.Count("conversation_answer_reviews").Should().Be(0);
        f.Count("conversation_messages").Should().Be(0);
        Encoding.UTF8.GetString(File.ReadAllBytes(f.DatabasePath)).Should().NotContain("private answer body");
        new FileInfo(f.DatabasePath + "-journal").Length.Should().Be(0);
        await f.Store.Invoking(store => store.ReadAnswerReviewMessageAsync(page, page.Rows[0], f.Token).AsTask())
            .Should().ThrowAsync<InvalidOperationException>();
        WindowsSqliteConversationTests.AuditText(f).Should().Contain("session.answer-review.set");
    }

    [Fact]
    public async Task DoneInspectionAndAcknowledgementNeverResumeOrRenewAndRetentionDeletionStillWinsOverFlags()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var perpetual = await f.GrantAsync("perpetual");
        await WindowsSqliteConversationTests.FinishInitial(f);
        await Answer(f, f.Request.SessionId, "retention-owned");
        var session = f.Request.SessionId;
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        await service.ChangeLifecycleAsync(session, new(1), false, RequestOrigin.LocalUi, f.Token);
        var target = await f.Store.ReadMetadataAsync(session, f.Token);
        var clock = await f.Store.ReadRetentionAsync(session, f.Token);
        f.Time.Now = f.Time.Now.AddHours(2);
        var page = await f.Store.ReadAnswerReviewsAsync(target, ConversationAnswerReviewFilter.NotMarkedReviewed, null, 25, f.Token);
        await f.Store.ReadAnswerReviewMessageAsync(page, page.Rows[0], f.Token);
        await Mark(f, page, page.Rows[0], true);
        (await f.Store.ReadMetadataAsync(session, f.Token)).Should().Be(target);
        target.Authority.IsActive.Should().BeFalse();
        (await f.Store.ReadRetentionAsync(session, f.Token)).Should().Be(clock);
        f.Time.Now = clock.DeleteDue;
        using (var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Retention))
        {
            var deleted = await f.Store.ApplyRetentionAsync(static () => true,
                static (_, _) => ValueTask.CompletedTask, f.Token);
            deleted.Deleted.Should().Be(1);
        }
        f.Count("conversation_answer_reviews").Should().Be(0);
        f.Count("conversation_messages").Should().Be(0);
        (await f.Store.ReadGrantsAsync(f.Token)).Should().ContainSingle().Which.Should().Be(perpetual);
        WindowsSqliteConversationTests.AuditText(f).Should().Contain("session.answer-review.set");
        Encoding.UTF8.GetString(File.ReadAllBytes(f.DatabasePath)).Should().NotContain("private answer body");
        new FileInfo(f.DatabasePath + "-journal").Length.Should().Be(0);
    }

    [Theory]
    [InlineData("filter")]
    [InlineData("head")]
    [InlineData("review-head")]
    [InlineData("after")]
    [InlineData("new-answer")]
    [InlineData("changed-mark")]
    public async Task ContinuationsCannotBecomeAnotherQueryOrSilentlySkipChangedResults(string change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await Answer(f, f.Request.SessionId, "first");
        await Answer(f, f.Request.SessionId, "second");
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var page = await f.Store.ReadAnswerReviewsAsync(target, ConversationAnswerReviewFilter.NotMarkedReviewed, null, 1, f.Token);
        var cursor = page.Next!;
        cursor = change switch
        {
            "filter" => cursor with { Filter = ConversationAnswerReviewFilter.All },
            "head" => cursor with { HeadRevision = cursor.HeadRevision - 1 },
            "review-head" => cursor with { ReviewRevision = 1 },
            "after" => cursor with { After = 0 },
            _ => cursor,
        };
        if (change is "new-answer") { await Answer(f, f.Request.SessionId, "later"); }
        if (change is "changed-mark") { await Mark(f, page, page.Rows[0], true); }
        await f.Store.Invoking(store => store.ReadAnswerReviewsAsync(target, page.Filter, cursor, 1, f.Token).AsTask())
            .Should().ThrowAsync<InvalidOperationException>();
        var refreshed = await f.Store.ReadAnswerReviewsAsync(target, page.Filter, null, 50, f.Token);
        refreshed.NotMarkedReviewed.Should().Be(change is "new-answer" ? 3 : change is "changed-mark" ? 1 : 2);
        JsonSerializer.SerializeToUtf8Bytes(refreshed).Length.Should().BeLessThanOrEqualTo(65536);
    }

    [Theory]
    [InlineData(0, ConversationAnswerReviewFilter.All)]
    [InlineData(51, ConversationAnswerReviewFilter.All)]
    [InlineData(25, (ConversationAnswerReviewFilter)99)]
    public async Task InvalidQueryBoundsAreExplicitRefusals(int limit, ConversationAnswerReviewFilter filter)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        await f.Store.Invoking(store => store.ReadAnswerReviewsAsync(target, filter, null, limit, f.Token).AsTask())
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task SchemaThirteenConfirmedLegacyAbsenceMigratesWithoutActivityAuditOrMessageChanges()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var original = await Answer(f, f.Request.SessionId, "legacy");
        var audit = WindowsSqliteConversationTests.AuditText(f);
        var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
        f.DropEmptyAnswerReviewSchemaForLegacy();
        f.Mutate("PRAGMA user_version=13;");
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var page = await f.Store.ReadAnswerReviewsAsync(target, ConversationAnswerReviewFilter.NotMarkedReviewed, null, 25, f.Token);
        page.NotMarkedReviewed.Should().Be(1);
        page.Rows.Single().ReviewRevision.Should().Be(0);
        WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
        (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
        (await f.Store.ReadConversationMessageAsync(f.Request.SessionId, original.Request.RequestId, f.Token)).Should().Be(original);
    }

    internal static async Task<ConversationMessage> Answer(InteractionStorageFixture f, HostId<SessionIdentity> session, string label)
    {
        var target = await f.Store.ReadMetadataAsync(session, f.Token);
        var request = WindowsSqliteConversationTests.Fresh(session);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RetainConversationMessageAsync(request, target.Authority.Generation, target.Metadata?.Revision.Value ?? 0,
            "original " + label, static () => true, f.Token);
        await f.Store.BeginConversationExecutionAsync(request, provenance, target.Metadata?.Revision.Value ?? 0, static () => true, f.Token);
        return (await f.Store.CompleteConversationExecutionAsync(request,
            new(ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome, provenance, new("private answer body " + label, null)),
            static () => true, f.Token)).Message;
    }

    internal static async Task<ConversationAnswerReviewRow> Mark(InteractionStorageFixture f, ConversationAnswerReviewPage page,
        ConversationAnswerReviewRow row, bool reviewed, bool admitted = true, bool cancelled = false)
    {
        var control = WindowsSqliteConversationTests.Fresh(page.Target.Authority.SessionId);
        using var root = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RecordControlIntentAsync(control, f.Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        if (cancelled) { await cancellation.CancelAsync(); }
        return await f.Store.SetAnswerReviewedAsync(control, page, row, reviewed, () => admitted, cancellation.Token);
    }
}

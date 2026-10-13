using AwesomeAssertions;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteConversationTests
{
    [Fact]
    public async Task MoreThan64DraftBearingSessionsHaveNoAggregateQuotaOrEvictionAndClearPreservesRevision()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        ConversationDraftSnapshot? first = null;
        for (var index = 0; index < 70; index++)
        {
            if (index != 0)
            {
                f.Request = InteractionStorageFixture.NewRequest();
                await f.AdmitAsync(newSession: true);
                await FinishInitial(f);
            }
            var saved = await Save(f, await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "draft-" + index);
            first ??= saved;
        }
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        await FinishInitial(f);
        var additional = await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token);
        await Save(f, additional, "another explicitly saved draft");
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        var ordered = (await f.Store.ReadSessionsAsync(null, 50, f.Token)).Records
            .Select(row => row.SessionId).ToList();
        var secondSessions = await f.Store.ReadSessionsAsync(ordered.Last().Value, 50, f.Token);
        ordered.AddRange(secondSessions.Records.Select(row => row.SessionId));
        ordered.Sort((left, right) => string.CompareOrdinal(left.Value.ToString("D"), right.Value.ToString("D")));
        const string name = "Private café 🙂 100%_";
        foreach (var target in ordered.TakeLast(2))
        {
            await service.RenameAsync(target, new(1), 0, new(name), RequestOrigin.LocalUi, f.Token);
        }
        using (var viewer = HostActivity.BeginRoot(Fresh(f.Request.SessionId), HostActivityLayer.Desktop, HostOperation.Request))
        {
            var audits = f.Count("security_audit_events");
            var excluded = await f.Store.ReadSavedDraftPage(Kora.Core.Storage.SessionListFilter.Done, null, 1, () => true, f.Token);
            excluded.Records.Should().BeEmpty();
            excluded.Scanned.Should().Be(50);
            excluded.Next.Should().NotBeNull("filtered nonmatches consume bounded slots, not a false empty inventory");
            var excludedTail = await f.Store.ReadSavedDraftPage(Kora.Core.Storage.SessionListFilter.Done, excluded.Next, 1, () => true, f.Token);
            excludedTail.Records.Should().BeEmpty();
            excludedTail.Scanned.Should().Be(21);
            excludedTail.Next.Should().BeNull();
            string? cursor = null;
            var discovered = new HashSet<HostId<SessionIdentity>>();
            do
            {
                var catalogue = await f.Store.ReadSavedDraftPage(Kora.Core.Storage.SessionListFilter.All, cursor, 1, () => true, f.Token);
                catalogue.Records.Length.Should().BeLessThanOrEqualTo(25);
                catalogue.Scanned.Should().BeLessThanOrEqualTo(50);
                foreach (var row in catalogue.Records) { discovered.Add(row.Session.SessionId).Should().BeTrue(); }
                cursor = catalogue.Next;
                if (discovered.Count == 25 && cursor is not null)
                {
                    var wrongControl = () => f.Store.ReadSavedDraftPage(Kora.Core.Storage.SessionListFilter.All, cursor, 2, () => true, f.Token).AsTask();
                    await wrongControl.Should().ThrowAsync<InvalidDataException>();
                    var wrongFilter = () => f.Store.ReadSavedDraftPage(Kora.Core.Storage.SessionListFilter.Done, cursor, 1, () => true, f.Token).AsTask();
                    await wrongFilter.Should().ThrowAsync<InvalidDataException>();
                    var tampered = () => f.Store.ReadSavedDraftPage(Kora.Core.Storage.SessionListFilter.All, cursor[..^1] + "x", 1, () => true, f.Token).AsTask();
                    await tampered.Should().ThrowAsync<InvalidDataException>();
                }
            } while (cursor is not null);
            discovered.Should().HaveCount(71, "catalogue continuation is not the removed 64-session profile quota");
            var query = new SessionListSearch(SessionListSearchKind.NameSubstring, SessionListFilter.All, "café 🙂 100%_");
            var noMatch = await f.Store.ReadSavedDraftPage(SessionListFilter.All, null, 1, () => true, f.Token, query);
            noMatch.Records.Should().BeEmpty();
            noMatch.Scanned.Should().Be(50);
            noMatch.Next.Should().NotBeNull("bounded nonmatches advance; this is not search exhaustion");
            var matched = await f.Store.ReadSavedDraftPage(SessionListFilter.All, noMatch.Next, 1, () => true, f.Token, query);
            matched.Records.Select(row => row.Session.SessionId).Should().Equal(ordered.TakeLast(2));
            matched.Scanned.Should().Be(21);
            matched.Next.Should().BeNull();
            SavedDraftPage.Size(matched).Should().BeLessThanOrEqualTo(SavedDraftPage.MaximumBytes);
            foreach (var alternate in new SessionListSearch?[]
            {
                null,
                new(SessionListSearchKind.NameSubstring, SessionListFilter.All, "Private"),
                new(SessionListSearchKind.ExactId, SessionListFilter.All, ordered[^1].Value.ToString("D")),
            })
            {
                var switched = () => f.Store.ReadSavedDraftPage(SessionListFilter.All, noMatch.Next, 1, () => true, f.Token, alternate).AsTask();
                await switched.Should().ThrowAsync<InvalidDataException>("a query change cannot reuse a cursor, even with the same matches");
            }
            var exactQuery = new SessionListSearch(SessionListSearchKind.ExactId, SessionListFilter.All, ordered[^1].Value.ToString("D"));
            var exactPage = await f.Store.ReadSavedDraftPage(SessionListFilter.All, null, 1, () => true, f.Token, exactQuery);
            exactPage.Records.Should().ContainSingle().Which.Session.SessionId.Should().Be(ordered[^1]);
            exactPage.Scanned.Should().Be(1, "the admitted exact metadata candidate uses the existing primary key seek");
            exactPage.Next.Should().BeNull();
            var mismatch = () => f.Store.ReadSavedDraftPage(SessionListFilter.Done, null, 1, () => true, f.Token, query).AsTask();
            await mismatch.Should().ThrowAsync<InvalidDataException>();
            f.Count("security_audit_events").Should().Be(audits);
            await service.RenameAsync(ordered[0], new(1), 0, new(name), RequestOrigin.LocalUi, f.Token);
            var stale = () => f.Store.ReadSavedDraftPage(SessionListFilter.All, noMatch.Next, 1, () => true, f.Token, query).AsTask();
            await stale.Should().ThrowAsync<InvalidDataException>("even a rename behind the cursor changes the conservative authority head");
            var refreshed = await f.Store.ReadSavedDraftPage(SessionListFilter.All, null, 1, () => true, f.Token, query);
            refreshed.Records.Should().ContainSingle().Which.Session.SessionId.Should().Be(ordered[0]);
        }
        (await f.Store.ReadConversationDraftAsync(first!.Session.SessionId, f.Token)).Draft.Body.Should().Be("draft-0");
        await Save(f, first, "exact revision overwrite");
        var current = await f.Store.ReadConversationDraftAsync(first.Session.SessionId, f.Token);
        await Save(f, current, null);
        var additionalCurrent = await f.Store.ReadConversationDraftAsync(additional.Session.SessionId, f.Token);
        await Save(f, additionalCurrent, "new explicit draft");
        f.Count("conversation_drafts").Should().Be(71, "there is no aggregate draft-bearing session quota and clearing retains its strict revision");
    }

    [Fact]
    public async Task PerSession100MessageLimitDoesNotLimitOtherSmallSessionsAndPagesRemainBounded()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var first = f.Request.SessionId;
        for (var item = 0; item < 100; item++)
        {
            var receipt = await Submit(f, first, "original-" + item);
            receipt.Message.Sequence.Should().Be(item + 1);
        }
        await this.Invoking(_ => Submit(f, first, "session overflow"))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*100 per session*");
        for (var session = 0; session < 70; session++)
        {
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await FinishInitial(f);
            await Submit(f, f.Request.SessionId, "independent small session " + session);
        }
        var page = await f.Store.ReadConversationPageAsync(first, null, 50, f.Token);
        page.Messages[0].Body.Should().Be("original-0");
        page.Messages.Should().HaveCount(50);
        var tail = await f.Store.ReadConversationPageAsync(first, page.Next, 50, f.Token);
        tail.Messages[^1].Body.Should().Be("original-99");
        f.Count("conversation_messages").Should().Be(170);
    }
}

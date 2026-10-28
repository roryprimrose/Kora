using System.Text;

using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Memory;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteMemoryTests
{
    [WindowsFact]
    public async Task Native_memory_only_disposition_erases_owned_cells_and_committed_journal_before_receipt()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var perpetual = await fixture.GrantAsync("perpetual");
        var other = fixture.Request.SessionId;
        using var unrelatedService = Service(fixture);
        var unrelatedCandidate = new MemoryCandidate(MemoryContentClass.ExplicitFact, "KEEP_UNRELATED_MEMORY_7EB864E8");
        var unrelated = await AdmitDispositionMemory(fixture, unrelatedService, unrelatedCandidate, MemoryScope.Session(other));
        await FinishIntent(fixture);
        var workspace = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        var empty = await workspace.CreateAsync(new("Memory only"), RequestOrigin.LocalUi, fixture.Token);
        fixture.Request = InteractionStorageFixture.NewRequest(empty.Authority.SessionId);
        const string sentinel = "OWNED_MEMORY_DISPOSITION_7EB864E8_";
        var candidate = new MemoryCandidate(MemoryContentClass.ExplicitFact,
            string.Concat(Enumerable.Repeat(sentinel, 12)));
        using var service = Service(fixture);
        var admitted = await AdmitDispositionMemory(fixture, service, candidate, MemoryScope.Session(empty.Authority.SessionId));
        admitted.Candidate.Should().Be(candidate);
        await NewIntent(fixture);
        var boundary = await fixture.RunAsync(() => fixture.Store.ReadMemoryBoundaryAsync(fixture.Request, 1, fixture.Token));
        await FinishIntent(fixture);
        using var profileService = Service(fixture, MemoryScopeKind.DeviceProfile);
        var profileCandidate = new MemoryCandidate(MemoryContentClass.WorkflowPreference, "KEEP_PROFILE_MEMORY_7EB864E8");
        var profile = await AdmitDispositionMemory(fixture, profileService, profileCandidate, MemoryScope.DeviceProfile(boundary.Profile));
        Encoding.UTF8.GetString(File.ReadAllBytes(fixture.DatabasePath)).Should().Contain(sentinel);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        workspace = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        var preview = await fixture.RunAsync(() => new ValueTask<SessionDispositionPreview>(
            workspace.PreviewDispositionAsync(empty.Authority.SessionId, new(1), 1, fixture.Token)));
        preview.Attachments.Should().Be(0);
        preview.ProviderProfiles.Should().Be(0);
        preview.ConversationMessages.Should().Be(0);
        preview.SavedConversationDrafts.Should().Be(0);
        var receipt = await workspace.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, fixture.Token);
        receipt.Removed.Should().Be(preview);
        AssertDispositionErased(fixture, sentinel);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        var tombstone = (await fixture.Store.ReadSessionAsync(empty.Authority.SessionId, fixture.Token))!;
        tombstone.IsActive.Should().BeFalse();
        tombstone.Generation.Value.Should().Be(2);
        using (var connection = fixture.OpenRaw())
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT payload FROM reviewed_memory WHERE memory_id=$id;";
            read.Parameters.AddWithValue("$id", admitted.Id.Value.ToString("D"));
            ((string)read.ExecuteScalar()!).Should().Contain("\"Candidate\":null").And.Contain("\"Receipt\":null")
                .And.Contain("\"Retention\":3");
        }
        fixture.Count("reviewed_memory").Should().Be(3);
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().ContainSingle().Which.Should().Be(perpetual);
        fixture.Request = InteractionStorageFixture.NewRequest(other);
        using var restarted = Service(fixture);
        (await Run(fixture, () => restarted.UseAsync(unrelated.Id, unrelated.Revision, MemoryDestination.Local, fixture.Token)))
            .Use!.Candidate.Should().Be(unrelatedCandidate);
        using var restartedProfile = Service(fixture, MemoryScopeKind.DeviceProfile);
        (await Run(fixture, () => restartedProfile.UseAsync(profile.Id, profile.Revision, MemoryDestination.Local, fixture.Token)))
            .Use!.Candidate.Should().Be(profileCandidate);
        var replay = () => workspace.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, fixture.Token);
        await replay.Should().ThrowAsync<InvalidOperationException>();
        var late = () => workspace.RenameAsync(empty.Authority.SessionId, tombstone.Generation, 0,
            new("Cannot reuse"), RequestOrigin.LocalUi, fixture.Token);
        await late.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_name_or_completed_question_history_disposition_clears_all_owned_content(bool questionContent)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var sentinels = new[] { "REMOVE_NAME_7EB864E8", "REMOVE_QUESTION_7EB864E8", "REMOVE_DRAFT_7EB864E8", "REMOVE_ANSWER_7EB864E8" };
        if (questionContent)
        {
            var question = (await fixture.RunAsync(() => fixture.Questions.CreateAsync(fixture.Request,
                new(sentinels[1], QuestionKind.Text, [], maximumTextLength: 128), fixture.Time.Now.AddMinutes(5), fixture.Token))).Question!;
            var draft = (await fixture.RunAsync(() => fixture.Questions.DraftAsync(question.Key, new([], sentinels[2]),
                RequestOrigin.LocalUi, fixture.Token))).Question!;
            await fixture.RunAsync(() => fixture.Questions.SubmitAsync(draft.Key, new([], sentinels[3]), RequestOrigin.LocalUi, fixture.Token));
        }
        await FinishIntent(fixture);
        var workspace = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        await workspace.RenameAsync(fixture.Request.SessionId, new(1), 0, new(sentinels[0]), RequestOrigin.LocalUi, fixture.Token);
        var originalHistory = await fixture.Store.ReadHistoryAsync(fixture.Request.SessionId, null, 50, fixture.Token);
        if (questionContent)
        {
            originalHistory.Records.Should().Contain(row => row.Answer == sentinels[3]);
        }
        Encoding.UTF8.GetString(File.ReadAllBytes(fixture.DatabasePath)).Should().Contain(sentinels[0]);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        workspace = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        var preview = await fixture.RunAsync(() => new ValueTask<SessionDispositionPreview>(
            workspace.PreviewDispositionAsync(fixture.Request.SessionId, new(1), 1, fixture.Token)));
        preview.Attachments.Should().Be(0);
        preview.ProviderProfiles.Should().Be(0);
        preview.ConversationMessages.Should().Be(0);
        preview.SavedConversationDrafts.Should().Be(0);
        preview.Questions.Should().Be(questionContent ? 1 : 0);
        await workspace.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, fixture.Token);
        AssertDispositionErased(fixture, sentinels);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        var history = await fixture.Store.ReadHistoryAsync(fixture.Request.SessionId, null, 50, fixture.Token);
        history.Disposed.Should().BeTrue();
        foreach (var original in originalHistory.Records)
        {
            var redacted = history.Records.Single(row => row.Id == original.Id);
            redacted.ProvenanceDigest.Should().Be(original.ProvenanceDigest);
            redacted.Question.Should().BeNull();
            redacted.Answer.Should().BeNull();
        }
    }

    private static async Task<MemoryRecord> AdmitDispositionMemory(InteractionStorageFixture fixture,
        Kora.Application.Memory.MemoryAdmissionService service, MemoryCandidate candidate, MemoryScope scope)
    {
        var proposal = await Run(fixture, () => service.ProposeAsync(candidate, scope, MemoryProposalOrigin.User, fixture.Token));
        proposal.Outcome.Should().Be(MemoryOutcome.Succeeded);
        var proposed = proposal.Record!;
        var review = await Run(fixture, () => service.ReviewAsync(proposed.Id, proposed.Revision, true, fixture.Token));
        review.Outcome.Should().Be(MemoryOutcome.Succeeded);
        var reviewed = review.Record!;
        var admission = await Run(fixture, () => service.AdmitAsync(reviewed.Id, reviewed.Revision, fixture.Token));
        admission.Outcome.Should().Be(MemoryOutcome.Succeeded);
        return admission.Record!;
    }

    private static void AssertDispositionErased(InteractionStorageFixture fixture, params string[] sentinels)
    {
        foreach (var path in new[] { fixture.DatabasePath, fixture.DatabasePath + "-journal" })
        {
            var bytes = File.ReadAllBytes(path);
            foreach (var sentinel in sentinels)
            {
                bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(sentinel)).Should().Be(-1,
                    "owned cells, free pages and journal must not retain fixture sentinel {0} in {1}", sentinel, path);
            }
        }
        new FileInfo(fixture.DatabasePath + "-journal").Length.Should().Be(0);
    }
}

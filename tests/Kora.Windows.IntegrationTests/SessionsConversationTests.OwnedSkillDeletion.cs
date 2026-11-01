using AwesomeAssertions;
using Kora.Application.Skills;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Skills;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task ActualAdmittedSourceDeletionInvalidatesReviewedFutureUseWithoutErasingIndependentGrantsOrHistory()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await f.GrantAsync("session");
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f, adapter: new HistoryAdapter());
            await using var admission = new OwnedSkillControlAdmission(f.Store, f.Store, new(f.Tasks));
            var store = new WindowsOwnedDeclarativeSkillStore(f.Paths);
            var document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "complete literal guidance");
            await store.Publish(document, static () => true, operation => Task.FromResult(operation()), f.Token);
            var authority = new OwnedSkillAuthoringAuthority(RequestOrigin.LocalUi, static () => true);
            var use = new OwnedSkillInvocationWorkflow(store, admission, composed.Access, f.Store, composed.Workspace,
                composed.Conversation, composed.Audit, f.Time);
            using var review = await use.ReviewAsync("example", "1.0.0", authority, static () => true, f.Token);
            var grants = f.Count("scoped_grants");
            var messages = f.Count("conversation_messages");
            grants.Should().Be(1);
            var deletion = new OwnedSkillDeletionWorkflow(store, admission, composed.Audit, f.Time);
            (await deletion.Delete("example", "1.0.0", authority, static () => true, new DeletionReview(), f.Token)).Should().BeTrue();
            await use.Invoking(value => value.EnableAsync(review, authority, static () => true, f.Token)).Should().ThrowAsync<InvalidDataException>();
            review.IsCurrent.Should().BeFalse();
            (await store.List(f.Token)).Should().BeEmpty();
            f.Count("scoped_grants").Should().Be(grants);
            f.Count("conversation_messages").Should().Be(messages);
        });
    }

    private sealed class DeletionReview : IOwnedSkillDeletionReview
    {
        public Task<bool> ReviewDeletion(OwnedSkillDeletionSnapshot source, CancellationToken token) => Task.FromResult(true);
        public Task<bool> ConfirmDeletion(OwnedSkillDeletionSnapshot source, CancellationToken token) => Task.FromResult(true);
    }
}

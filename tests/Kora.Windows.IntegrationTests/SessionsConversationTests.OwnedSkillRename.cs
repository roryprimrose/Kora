using AwesomeAssertions;
using Kora.Application.Skills;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Skills;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenameRetiresOldEnabledUseAndNeverTransfersItToNewIdentityOrRestoredBytes(bool retainedIntent)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new HistoryAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            using var paths = new OwnedStorageFixture();
            var store = new WindowsOwnedDeclarativeSkillStore(paths);
            var document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "Original guidance");
            var saved = await store.Publish(document, static () => true, operation => Task.FromResult(operation()), f.Token);
            await using var admission = new OwnedSkillControlAdmission(f.Store, f.Store, new(f.Tasks));
            var use = new OwnedSkillInvocationWorkflow(store, admission, composed.Access, f.Store,
                composed.Workspace, composed.Conversation, composed.Audit, f.Time);
            var authority = new OwnedSkillAuthoringAuthority(RequestOrigin.LocalUi, static () => true);
            using var oldReview = await use.ReviewAsync("example", "1.0.0", authority, static () => true, f.Token);
            await use.EnableAsync(oldReview, authority, static () => true, f.Token);
            oldReview.IsEnabled.Should().BeTrue();
            var proposal = new OwnedSkillRename(saved, "renamed");
            if (retainedIntent)
            { store.Checkpoint = point => { if (point is "rename-destination-verified") { throw new IOException("injected boundary"); } }; }
            var rename = () => store.Rename(proposal, false, static () => true, operation => Task.FromResult(operation()), f.Token);
            if (retainedIntent) { await rename.Should().ThrowAsync<OwnedSkillRenameUnconfirmedException>(); }
            else { await rename(); }
            if (retainedIntent)
            { await use.Invoking(value => value.ValidateAsync(oldReview, authority, static () => true, f.Token)).Should().ThrowAsync<InvalidOperationException>(); }
            else { await use.Invoking(value => value.ValidateAsync(oldReview, authority, static () => true, f.Token)).Should().ThrowAsync<InvalidDataException>(); }
            oldReview.IsCurrent.Should().BeFalse();
            oldReview.IsEnabled.Should().BeFalse();
            if (retainedIntent)
            {
                store.Checkpoint = null;
                await store.Rename(proposal, true, static () => true, operation => Task.FromResult(operation()), f.Token);
            }
            using var newReview = await use.ReviewAsync("renamed", "1.0.0", authority, static () => true, f.Token);
            newReview.IsEnabled.Should().BeFalse();
            await store.Publish(document, static () => true, operation => Task.FromResult(operation()), f.Token);
            await use.Invoking(value => value.EnableAsync(oldReview, authority, static () => true, f.Token)).Should().ThrowAsync<InvalidOperationException>();
            adapter.Calls.Should().Be(0);
            f.Count("conversation_messages").Should().Be(0);
        });
    }
}

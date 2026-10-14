using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class DetailWindowControllerTests
{
    [Fact]
    public async Task ConversationSelectionRevocationLeavesIndependentHistoryReceiptViewerOpen()
    {
        using var f = new Fixture();
        var conversation = ConversationContent();
        var session = conversation.ConversationMessage!.Request.SessionId;
        var history = new AdmittedDetailContent(new(new(Guid.NewGuid()), 1), DetailContentKind.PlainText,
            DetailContentOrigin.SessionHistory, DetailSensitivity.DisclosureConfirmationRequired,
            "History receipt", "Retained receipt", "independent exact receipt", historySession: session);
        f.Controller.BindHistoryAccess(() => true);
        f.Controller.OpenHistoryDetail(history, null);
        await f.Controller.OpenConversationDetailAsync(conversation, null, () => true,
            _ => Task.FromResult(conversation), CancellationToken.None);
        f.Controller.RevokeConversationSession(session);
        f.Views[0].Cleared.Should().BeFalse();
        f.Views[0].State.Content.Should().Be(history);
        f.Views[1].Cleared.Should().BeTrue();
        f.Controller.RevokeSession(session);
        f.Views[0].Cleared.Should().BeTrue();
    }

    [WindowsFact]
    public async Task ActualRetainedConversationRevokesBeforeDueRetentionDeletionAndCannotReopenPurgedSource()
    {
        using var storage = new InteractionStorageFixture();
        await storage.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(storage);
        var request = new HostRequest(new(Guid.NewGuid()), storage.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        ConversationMessage record;
        using (var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            await storage.Store.RetainConversationMessageAsync(request, new(1), 0,
                "exact original private USER\r\n<script>inert</script>", () => true, storage.Token);
            record = (await storage.Store.CompleteConversationSubmissionAsync(request, ModelTurnOutcome.Unavailable,
                ModelTurnReason.UnregisteredModel, () => true, storage.Token)).Message;
        }
        var service = WindowsSqliteSessionWorkspaceTests.Service(storage, new());
        var target = await storage.Store.ReadMetadataAsync(request.SessionId, storage.Token);
        async Task<AdmittedDetailContent> Resolve(CancellationToken token)
        {
            using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Storage);
            return await service.ReadConversationDetailAsync(target, record, () => true, token);
        }
        var content = await Resolve(storage.Token);
        using var f = new Fixture();
        await f.Controller.OpenConversationDetailAsync(content, null, () => true, Resolve, storage.Token);
        var view = f.Views.Single();
        var generation = view.State.Generation;
        await f.Controller.RevalidateConversationDetailsAsync();
        view.State.Content.Should().NotBeNull();
        var clock = await storage.Store.ReadRetentionAsync(request.SessionId, storage.Token);
        storage.Time.Now = clock.DeleteDue;
        using var retention = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Application, HostOperation.Retention);
        var batch = await storage.Store.ApplyRetentionAsync(() => true, (session, _) =>
        {
            f.Controller.RevokeSession(session);
            view.State.Content.Should().BeNull("revocation precedes physical retained-body deletion");
            return ValueTask.CompletedTask;
        }, storage.Token);
        batch.Deleted.Should().Be(1);
        view.Cleared.Should().BeTrue();
        view.State.ActiveText.Should().BeEmpty();
        view.State.CompleteRender(generation, "late old USER", "Rendered", false).Should().BeFalse();
        await Resolve(storage.Token).Invoking(task => task).Should().ThrowAsync<InvalidOperationException>();
        await f.Controller.CopyAsync(content.Reference, generation, true);
        f.Clipboard.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task ConversationDetailsAreInertExactBoundedDeduplicatedAndNeverAuthorizeCopy()
    {
        using var f = new Fixture();
        var content = ConversationContent();
        var reads = 0;
        Task<AdmittedDetailContent> Resolve(CancellationToken token) { reads++; return Task.FromResult(content); }
        var status = await f.Controller.OpenConversationDetailAsync(content, null, () => true, Resolve, CancellationToken.None);
        status.Should().Contain("plain text");
        await f.Controller.OpenConversationDetailAsync(content, null, () => true, Resolve, CancellationToken.None);
        var view = f.Views.Single();
        var admittedReads = reads;
        view.State.Content!.ConversationMessage.Should().Be(content.ConversationMessage);
        view.State.ActiveText.Should().Be(content.Source);
        view.State.Search("script").Should().BeTrue();
        view.State.SetSource(true);
        view.State.ActiveText.Should().Be(content.Source);
        reads.Should().Be(admittedReads, "Find scans only the admitted in-memory snapshot, without retained reads");
        await f.Controller.CopyAsync(content.Reference, view.State.Generation, true);
        await f.Controller.CopyAsync(content.Reference, view.State.Generation, true, 0, 5);
        f.Clipboard.Writes.Should().BeEmpty();
        for (var index = 1; index < NativeDetailProfile.MaximumOpenViewers; index++)
        {
            var another = ConversationContent();
            await f.Controller.OpenConversationDetailAsync(another, null, () => true,
                _ => Task.FromResult(another), CancellationToken.None);
        }
        f.Views.Should().HaveCount(8);
        var overflow = ConversationContent();
        (await f.Controller.OpenConversationDetailAsync(overflow, null, () => true,
            _ => Task.FromResult(overflow), CancellationToken.None)).Should().Contain("close a viewer");
        (await f.Controller.OpenConversationDetailAsync(content, null, () => true, Resolve, CancellationToken.None))
            .Should().Contain("Activated");
        f.Controller.RevokeConversationSession(content.ConversationMessage!.Request.SessionId);
        view.State.Content.Should().BeNull();
        view.State.SearchQuery.Should().BeEmpty();
        view.Cleared.Should().BeTrue();
        f.Views.Skip(1).Should().OnlyContain(other => !other.Cleared);
        await f.Controller.RevalidateConversationDetailsAsync();
        f.Controller.Dispose();
        f.Views.Should().OnlyContain(other => other.Cleared);
    }

    [Theory]
    [InlineData("private")]
    [InlineData("owner")]
    [InlineData("source")]
    [InlineData("deleted")]
    public async Task OpenConversationViewerRevokesOnPrivateOwnershipSourceAndRetentionLoss(string fault)
    {
        using var f = new Fixture();
        var content = ConversationContent();
        var admitted = true;
        var lost = false;
        Task<AdmittedDetailContent> Resolve(CancellationToken token) => lost
            ? fault is "deleted" ? throw new InvalidOperationException("source unavailable") : Task.FromResult(ConversationContent())
            : Task.FromResult(content);
        await f.Controller.OpenConversationDetailAsync(content, null, () => admitted, Resolve, CancellationToken.None);
        var view = f.Views.Single();
        var generation = view.State.Generation;
        if (fault is "private") { f.Accessible = false; }
        else if (fault is "owner") { admitted = false; }
        else { lost = true; }
        await f.Controller.RevalidateConversationDetailsAsync();
        view.Cleared.Should().BeTrue();
        view.State.ActiveText.Should().BeEmpty();
        view.State.Content.Should().BeNull();
        view.State.CompleteRender(generation, "late", "Rendered", false).Should().BeFalse();
        await f.Controller.CopyAsync(content.Reference, generation, true);
        f.Clipboard.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("dispose")]
    [InlineData("delete")]
    [InlineData("selection")]
    [InlineData("source")]
    [InlineData("cancel")]
    public async Task ConversationOpeningFencesDelayedAndFinalPublication(string fault)
    {
        using var f = new Fixture();
        var content = ConversationContent();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var admitted = true;
        using var cancellation = new CancellationTokenSource();
        async Task<AdmittedDetailContent> Resolve(CancellationToken token)
        {
            if (++calls == 2)
            {
                started.SetResult();
                await release.Task;
            }
            return fault is "source" && calls == 2 ? ConversationContent() : content;
        }
        var opening = f.Controller.OpenConversationDetailAsync(content, null, () => admitted, Resolve, cancellation.Token);
        await started.Task;
        f.Views.Single().Shown.Should().BeFalse("final exact source revalidation precedes native publication");
        if (fault is "privacy") { f.Controller.ClearForPrivacy(); }
        if (fault is "dispose") { f.Controller.Dispose(); }
        if (fault is "delete") { f.Controller.RevokeSession(content.ConversationMessage!.Request.SessionId); }
        if (fault is "selection") { admitted = false; }
        if (fault is "cancel") { cancellation.Cancel(); }
        release.SetResult();
        await opening.Invoking(task => task).Should().ThrowAsync<Exception>();
        f.Views.Single().Cleared.Should().BeTrue();
        f.Views.Single().State.Content.Should().BeNull();
        f.Views.Single().Shown.Should().BeFalse();
    }

    private static AdmittedDetailContent ConversationContent()
    {
        var message = new ConversationMessage(HostRequest.Create(RequestOrigin.LocalUi), new(1), 1, 2,
            DateTimeOffset.UnixEpoch, "  USER e\u0301 😀\r\n\t<script>run()</script> [x](https://example.test)  ",
            ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel);
        return new(new(new(message.Request.RequestId.Value), message.Revision), DetailContentKind.PlainText,
            DetailContentOrigin.RetainedConversation, DetailSensitivity.DisclosureConfirmationRequired,
            "Retained original USER", "Separate typed HOST; no assistant answer or clipboard authority",
            message.Body, conversationMessage: message);
    }
}

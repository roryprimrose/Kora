using Avalonia.Controls;
using Avalonia.Threading;
using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Core.Conversation;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LastViewProtectsAllSessionBuffersEvenWhenSelectedInputIsEmpty(bool discard)
    {
        var decision = discard ? ComposerDraftDecision.Discard : ComposerDraftDecision.Cancel;
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var original = f.Request.SessionId;
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            var window = new SessionsWindow(model, owner);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
                model.ComposerBody = "unsent in another topic";
                model.SessionFilter = SessionListFilter.Done;
                model.ComposerBody.Should().BeEmpty();
                model.HasUnsentMessages.Should().BeTrue();
                var decisions = 0;
                model.DecideComposerDraft = () => { decisions++; return Task.FromResult(decision); };
                window.Close();
                await owner.CloseOperation;
                decisions.Should().Be(1);
                window.IsVisible.Should().Be(decision == ComposerDraftDecision.Cancel);
                if (decision == ComposerDraftDecision.Cancel)
                {
                    model.SessionFilter = SessionListFilter.All;
                    await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
                    model.ComposerBody.Should().Be("unsent in another topic");
                }
                else { model.HasUnsentMessages.Should().BeFalse(); }
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("conversation_messages").Should().Be(0);
            }
            finally { owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitDiscardRequiresDecisionAndLeavesOtherTopicAndLegacyTextUnchanged(bool discard)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var original = f.Request.SessionId;
        var legacy = await WindowsSqliteConversationTests.Save(f,
            await f.Store.ReadConversationDraftAsync(original, f.Token), "legacy retained");
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
        model.ComposerBody = "first input";
        await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != original));
        model.ComposerBody = "second input";
        model.DecideComposerDraft = () => Task.FromResult(discard ? ComposerDraftDecision.Discard : ComposerDraftDecision.Cancel);
        await model.DiscardComposerBufferAsync();
        model.ComposerBody.Should().Be(discard ? "" : "second input");
        await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
        model.ComposerBody.Should().Be("first input");
        (await f.Store.ReadConversationDraftAsync(original, f.Token)).Should().Be(legacy);
    }

    [Fact]
    public async Task ChangedLegacyRevisionNeverImportsNewerBodyAndRequiresFreshExactRead()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var legacy = await WindowsSqliteConversationTests.Save(f,
            await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "old legacy");
        var reader = new SearchReader(f.Store.ReadConversationPageAsync, f.Store.ReadConversationDraftAsync, f.Store.ReadMetadataAsync);
        await using var composed = new Composition(f, conversationReader: reader);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        reader.DraftReads.Should().Be(0);
        reader.AfterDraftRead = async () =>
        {
            reader.AfterDraftRead = null;
            await WindowsSqliteConversationTests.Save(f, legacy, "changed source");
        };
        await model.RecoverLegacyUnsentAsync();
        model.ComposerBody.Should().BeEmpty();
        model.ConversationStatus.Should().Contain("Legacy recovery unavailable");
        model.CanCompose.Should().BeTrue("an optional legacy recovery failure does not revoke ordinary conversation admission");
        model.CanRecoverConversationDraft.Should().BeTrue();
        await model.RecoverLegacyUnsentAsync();
        model.ComposerBody.Should().Be("changed source");
        f.Count("conversation_messages").Should().Be(0);
    }

    [Fact]
    public async Task OrdinaryHistoryAndNextRemainReadableWithoutAnyLegacyReadAndRecoveryFailureLeavesHistoryIntact()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        for (var index = 0; index < 26; index++)
        { await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "original " + index); }
        var reader = new SearchReader(f.Store.ReadConversationPageAsync, f.Store.ReadConversationDraftAsync, f.Store.ReadMetadataAsync)
        { DenyDraftReads = true };
        await using var composed = new Composition(f, conversationReader: reader);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.ComposerBody = "independent unsent input";
        await model.ReadConversationAsync();
        model.ConversationMessages.Should().HaveCount(25);
        model.CanNextConversation.Should().BeTrue();
        await model.ReadConversationAsync(next: true);
        model.ConversationMessages.Should().ContainSingle().Which.Body.Should().Be("original 25");
        model.ComposerBody.Should().Be("independent unsent input");
        model.CanCompose.Should().BeTrue();
        reader.DraftReads.Should().Be(0);
        model.DecideComposerDraft = () => Task.FromResult(ComposerDraftDecision.Discard);
        await model.DiscardComposerBufferAsync();
        var history = model.ConversationMessages;
        await model.RecoverLegacyUnsentAsync();
        reader.DraftReads.Should().Be(1);
        model.ConversationStatus.Should().Contain("Legacy recovery unavailable");
        model.ConversationMessages.Should().Equal(history);
        model.ComposerBody.Should().BeEmpty();
        model.CanCompose.Should().BeTrue();
        await model.ReadConversationAsync();
        model.ConversationMessages.Should().HaveCount(25);
        reader.DraftReads.Should().Be(1, "history refresh never retries legacy recovery");
        f.Count("conversation_drafts").Should().Be(0);
    }

    [Fact]
    public async Task MissingLegacyTextIsExplicitAndIndependentOfHistoryReadWithNoWrites()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var reader = new SearchReader(f.Store.ReadConversationPageAsync, f.Store.ReadConversationDraftAsync, f.Store.ReadMetadataAsync);
        await using var composed = new Composition(f, conversationReader: reader);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        var audit = WindowsSqliteConversationTests.AuditText(f);
        var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
        await model.RecoverLegacyUnsentAsync();
        model.ConversationStatus.Should().Contain("No existing legacy text");
        model.ComposerBody.Should().BeEmpty();
        model.CanCompose.Should().BeTrue();
        reader.Reads.Should().Be(0, "explicit recovery does not implicitly read history");
        reader.DraftReads.Should().Be(1);
        WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
        (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
        f.Count("conversation_drafts").Should().Be(0);
        f.Count("conversation_messages").Should().Be(0);
    }

    [Fact]
    public async Task UnconfirmedSubmissionHoldSurvivesTopicSwitchAndPassiveRefreshWithoutRetargeting()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var original = f.Request.SessionId;
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            SessionsViewModel? model = null;
            SessionWorkspaceEntry? other = null;
            f.Reopen(new SelectionCheckpoint(() => model!.SelectAsync(other).IsCompletedSuccessfully.Should().BeTrue()));
            await f.Store.InitializeAsync(f.Token);
            await using var composed = new Composition(f);
            model = composed.Model;
            await model.RefreshAsync();
            other = model.Sessions.Single(row => row.Authority.SessionId != original);
            await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
            var terminal = new TaskCompletionSource<(SessionWorkspaceEntry Target, ConversationTurnCompletion Completion)>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void Completed(SessionWorkspaceEntry target, ConversationTurnCompletion completion)
            {
                terminal.TrySetResult((target, completion));
            }
            composed.Conversation.SubmittedConversationCompleted += Completed;
            try
            {
                model.ComposerBody = "held exact input";
                await model.SubmitConversationAsync();
                var completed = await terminal.Task.WaitAsync(f.Token);
                completed.Target.Authority.SessionId.Should().Be(original);
                completed.Completion.Receipt.Should().BeNull();
                completed.Completion.Failure.Should().BeOfType<ConversationSubmissionUnconfirmedException>();
                Dispatcher.UIThread.RunJobs();
                model.CanReadConversation.Should().BeTrue("the hold must outlive the busy operation");
                model.CanSubmitConversation.Should().BeFalse();
                model.SelectedSessionRecord!.Authority.SessionId.Should().Be(original);
                model.ComposerBody.Should().Be("held exact input");
                await model.SelectAsync(other);
                model.ComposerBody.Should().BeEmpty();
                model.CanCompose.Should().BeTrue("the unconfirmed request cannot hold or retarget another topic");
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
                model.ComposerBody.Should().Be("held exact input");
                await model.ReadConversationAsync();
                model.CanSubmitConversation.Should().BeFalse();
                await model.SubmitConversationAsync();
                model.CanSubmitConversation.Should().BeFalse();
                model.ComposerBody.Should().Be("held exact input");
                f.Count("conversation_messages").Should().Be(0);
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
            }
            finally { composed.Conversation.SubmittedConversationCompleted -= Completed; }
        });
    }
}

using Avalonia.Controls;
using AwesomeAssertions;
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
        await using var composed = new Composition(f);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        await WindowsSqliteConversationTests.Save(f, legacy, "changed source");
        await model.RecoverLegacyUnsentAsync();
        model.ComposerBody.Should().BeEmpty();
        model.CanSubmitConversation.Should().BeFalse();
        model.CanRecoverConversationDraft.Should().BeFalse();
        await model.ReadConversationAsync();
        await model.RecoverLegacyUnsentAsync();
        model.ComposerBody.Should().Be("changed source");
        f.Count("conversation_messages").Should().Be(0);
    }

    [Fact]
    public async Task UnconfirmedSubmissionHoldSurvivesTopicSwitchAndPassiveRefreshWithoutRetargeting()
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
        model.ComposerBody = "held exact input";
        await model.SubmitConversationAsync();
        model.CanSubmitConversation.Should().BeFalse();
        await model.SelectAsync(other);
        model.ComposerBody.Should().BeEmpty();
        await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
        model.ComposerBody.Should().Be("held exact input");
        await model.ReadConversationAsync();
        model.CanSubmitConversation.Should().BeFalse();
        f.Count("conversation_messages").Should().Be(0);
    }
}

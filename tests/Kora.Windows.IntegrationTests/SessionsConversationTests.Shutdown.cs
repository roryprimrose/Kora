using Avalonia.Controls;
using AwesomeAssertions;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("cancel")]
    [InlineData("failure")]
    [InlineData("permission")]
    [InlineData("control")]
    [InlineData("source")]
    [InlineData("close")]
    public async Task SessionsShutdownCannotAcceptCancelledFailedOrRevokedDiscard(string fault)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        var model = composed.Model;
        var owner = new SessionsPresentationOwner(model);
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.ComposerBody = "original unsent input";
        model.DecideComposerDraft = () =>
        {
            if (fault is "failure") { throw new IOException("Synthetic native prompt failure."); }
            if (fault is "permission") { composed.Access.CanControl = false; }
            if (fault is "control") { composed.Access.ControlRevision++; }
            if (fault is "source") { model.RevokeSessionList(); }
            if (fault is "close") { model.Close(); }
            return Task.FromResult(fault is "cancel" ? ComposerDraftDecision.Cancel : ComposerDraftDecision.Discard);
        };
        (await owner.PrepareShutdown()).Should().BeFalse();
        if (fault is "cancel" or "failure" or "permission") { model.ComposerBody.Should().Be("original unsent input"); }
        if (fault is "failure") { model.ConversationStatus.Should().Contain("unconfirmed"); }
        f.Count("conversation_drafts").Should().Be(0);
        f.Count("conversation_messages").Should().Be(0);
        owner.Revoke();
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("aba")]
    [InlineData("control")]
    [InlineData("close")]
    public async Task ShutdownReceiptCannotAuthorizeLaterInputOrSource(string change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.ComposerBody = "reviewed input";
        model.DecideComposerDraft = () => Task.FromResult(ComposerDraftDecision.Discard);
        (await model.ConfirmUnsentDiscardAsync(all: true)).Should().BeTrue();
        var receipt = model.CaptureUnsentShutdownEligibility();
        receipt().Should().BeTrue();
        if (change is "edit") { model.ComposerBody = "newer input"; }
        if (change is "aba") { model.ComposerBody = "newer input"; model.ComposerBody = string.Empty; }
        if (change is "control") { composed.Access.ControlRevision++; }
        if (change is "close") { model.Close(); }
        receipt().Should().BeFalse();
    }

    [Fact]
    public async Task HeldConversationSourceCannotBecomeSuccessfulVoluntaryShutdownEvenWithEmptyInput()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var reader = new SearchReader(f.Store.ReadConversationPageAsync) { DenyReads = true };
        await using var composed = new Composition(f, conversationReader: reader);
        var model = composed.Model;
        var owner = new SessionsPresentationOwner(model);
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        model.DecideComposerDraft = () => throw new InvalidOperationException("A held source must not acquire discard authority.");
        (await owner.PrepareShutdown()).Should().BeFalse();
        model.ConversationStatus.Should().Contain("unconfirmed");
        owner.Revoke();
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("aba")]
    [InlineData("control")]
    [InlineData("permission")]
    [InlineData("source")]
    public async Task NewRequestDiscardRevalidatesBufferAndSourceAfterAsyncPrompt(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            using var composer = composed.NewComposer();
            var source = true;
            var window = new NewRequestWindow(new(composer), () => Task.CompletedTask, _ => { }, () => () => source);
            window.Show();
            try
            {
                composer.Body = "original";
                var decision = new TaskCompletionSource<ComposerDraftDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
                window.DecideDraft = () => decision.Task;
                var closing = window.PrepareShutdown();
                closing.IsCompleted.Should().BeFalse();
                if (change is "edit") { composer.Body = "new input"; }
                if (change is "aba") { composer.Body = "new input"; composer.Body = "original"; }
                if (change is "control") { composed.Access.ControlRevision++; }
                if (change is "permission") { composed.Access.CanControl = false; }
                if (change is "source") { source = false; }
                decision.SetResult(ComposerDraftDecision.Discard);
                var complete = async () => await closing;
                await complete.Should().ThrowAsync<OperationCanceledException>();
                window.IsVisible.Should().BeTrue();
                composer.Body.Should().Be(change is "edit" ? "new input" : "original");
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("conversation_messages").Should().Be(0);
            }

            finally { window.ClosePrivate(); }
        });
    }

    [Fact]
    public async Task PrivateRetirementDuringNewRequestDiscardDoesNotBecomeVoluntaryConfirmation()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            using var composer = composed.NewComposer();
            var window = new NewRequestWindow(new(composer), () => Task.CompletedTask, _ => { });
            window.Show();
            composer.Body = "private input";
            var decision = new TaskCompletionSource<ComposerDraftDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
            window.DecideDraft = () => decision.Task;
            var closing = window.PrepareShutdown();
            window.ClosePrivate();
            decision.SetResult(ComposerDraftDecision.Discard);
            (await closing).Should().BeFalse();
            composer.Body.Should().BeEmpty();
            window.IsVisible.Should().BeFalse();
        });
    }

    [Theory]
    [InlineData(WindowCloseReason.ApplicationShutdown)]
    [InlineData(WindowCloseReason.OSShutdown)]
    public async Task MandatoryShutdownClearsBothConversationSurfacesAndNewRequestWithoutDiscardPrompt(WindowCloseReason reason)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            var sessions = new SessionsWindow(model, owner);
            var compact = new CompactSessionWindow(model, owner);
            using var composer = composed.NewComposer();
            var request = new NewRequestWindow(new(composer), () => Task.CompletedTask, _ => { });
            sessions.Show(); compact.Show(); request.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.ComposerBody = "private topic input";
                composer.Body = "private new input";
                model.DecideComposerDraft = () => throw new InvalidOperationException("Mandatory retirement cannot prompt.");
                request.DecideDraft = () => throw new InvalidOperationException("Mandatory retirement cannot prompt.");
                NativeShutdownPolicy.RequiresImmediateRetirement(reason).Should().BeTrue();
                owner.Revoke();
                request.ClosePrivate();
                model.ComposerBody.Should().BeEmpty();
                composer.Body.Should().BeEmpty();
                model.HasUnsentMessages.Should().BeFalse();
                model.CanRead.Should().BeFalse();
                f.Count("conversation_drafts").Should().Be(0);
            }
            finally { owner.Revoke(); sessions.Close(); compact.Close(); request.ClosePrivate(); }
        });
    }

    [Theory]
    [InlineData(WindowCloseReason.WindowClosing)]
    [InlineData(WindowCloseReason.OwnerWindowClosing)]
    [InlineData(WindowCloseReason.Undefined)]
    public void OrdinaryWindowCloseRetainsDiscardReviewPolicy(WindowCloseReason reason) =>
        NativeShutdownPolicy.RequiresImmediateRetirement(reason).Should().BeFalse();
}

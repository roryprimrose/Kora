using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Application.KnowledgeSources;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;
using Kora.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.KnowledgeSources;

[Collection("Host tracing")]
public sealed class SessionKnowledgeSourceWorkflowTests
{
    [Fact]
    public async Task SeparateBindingReopenQuestionAndAnswerRetainGenuineCitationsInOriginalConversationWithoutCopies()
    {
        await using var f = new Fixture();
        using var root = Root();
        (await f.Workflow.ReadBindingAsync(f.Target, Token)).Should().BeNull();
        (await f.Workflow.ListSourcesAsync(Token)).Should().Contain(f.Source.Source);
        f.Source.Requests.Should().BeEmpty();
        var binding = await f.Bind();
        binding.Source.Should().Be(f.Source.Revision.Reference);
        (await f.Workflow.ReadBindingAsync(f.Target, Token)).Should().Be(binding);
        var readCount = f.Source.Requests.Count;
        await f.Workflow.RevokeAsync();
        (await f.Workflow.ReadBindingAsync(f.Target, Token)).Should().Be(binding);
        f.Source.Requests.Count.Should().Be(readCount, "metadata reopen cannot read source bytes");
        var opened = await f.Workflow.OpenSourceAsync(f.Target, binding, () => true, Token);
        opened.Revision.Should().BeSameAs(f.Source.Revision);
        var review = (await f.Workflow.PrepareQuestionAsync(opened, "alpha?", () => true, Token)).Review!;
        review.Target.CreatesSession.Should().BeFalse();
        review.Target.Request.SessionId.Should().Be(f.Target.Authority.SessionId);
        f.Retained.Adapter.Calls.Should().Be(0);
        (await f.Workflow.AnswerAsync(review, () => true, Token)).Outcome.Should().Be(LocalFileAnswerOutcome.Denied);
        f.Retained.Adapter.Calls.Should().Be(0, "question review is not independent model confirmation");
        using var confirmation = Root();
        var answer = await f.Workflow.AnswerAsync(review, () => true, Token);
        answer.Outcome.Should().Be(LocalFileAnswerOutcome.Succeeded);
        f.Retained.CreatedSessions.Should().Be(0);
        f.Retained.Message!.Request.SessionId.Should().Be(f.Target.Authority.SessionId);
        f.Retained.Message.Body.Should().Be("alpha?");
        f.Retained.Message.AssistantResponse!.Body.Should().Contain("alpha retained claim");
        foreach (var citation in answer.Claims.SelectMany(claim => claim.Citations))
        { f.Retained.Message.AssistantResponse.Body.Should().Contain(review.DescribeCitation(citation)); }
        await f.Workflow.DetachAsync(f.Target, binding, () => true, Token);
        f.Retained.Binding.Should().BeNull();
        f.Retained.Message.AssistantResponse.Should().NotBeNull();
        f.Source.OriginalReads.Should().Be(0);
        f.Source.Mutations.Should().Be(0);
        f.Retained.Models.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("same-input")]
    [InlineData("clone")]
    [InlineData("expired")]
    [InlineData("clock-rollback")]
    [InlineData("revoked")]
    public async Task BindingNeedsCurrentExactReviewAndIndependentOriginalUserConfirmation(string fault)
    {
        await using var f = new Fixture();
        using var input = Root();
        var review = await f.Workflow.ReviewBindingAsync(f.Target, f.Source.Source, () => true, Token);
        if (fault is "expired") { f.Source.Clock.Now = review.ExpiresAt; }
        if (fault is "clock-rollback") { f.Source.Clock.Now = review.ExpiresAt.AddMinutes(-3); }
        if (fault is "revoked") { await f.Workflow.RevokeAsync(); }
        async Task Confirm()
        {
            if (fault is "same-input") { await f.Workflow.ConfirmBindingAsync(review, () => true, Token); }
            else
            {
                using var confirmation = Root();
                await f.Workflow.ConfirmBindingAsync(fault is "clone" ? review with { } : review, () => true, Token);
            }
        }
        await FluentActions.Awaiting(Confirm).Should().ThrowAsync<InvalidOperationException>();
        f.Retained.Binding.Should().BeNull();
        f.Retained.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("session")]
    [InlineData("inactive")]
    [InlineData("slot")]
    [InlineData("source-access")]
    public async Task BindingReviewRevalidatesTargetSlotAndSourceAcrossAwaitedInspection(string fault)
    {
        await using var f = new Fixture();
        using var root = Root();
        if (fault is "session") { f.Retained.Target = f.Target with { Metadata = new(f.Target.Authority.SessionId, new(1), new("Changed")) }; }
        if (fault is "inactive") { f.Retained.Target = f.Target with { Authority = f.Target.Authority with { IsActive = false } }; }
        if (fault is "slot") { f.Retained.Binding = f.Descriptor(); }
        if (fault is "source-access")
        {
            var validations = 0;
            f.Source.At = stage =>
            {
                if (stage is "validate" && ++validations == 2) { f.Source.InvalidAccess = true; }
            };
        }
        await FluentActions.Awaiting(() => f.Workflow.ReviewBindingAsync(f.Target, f.Source.Source, () => true, Token))
            .Should().ThrowAsync<Exception>();
        if (fault is "source-access") { f.Source.DisposedSelections.Should().Be(1); }
        f.Retained.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("new-revision")]
    [InlineData("unavailable")]
    [InlineData("binding")]
    [InlineData("missing-source")]
    [InlineData("stable-new-revision")]
    public async Task ReopenNeverSubstitutesNewRevisionOrUsesRevokedBinding(string fault)
    {
        await using var f = new Fixture();
        using var root = Root();
        var binding = await f.Bind();
        if (fault is "new-revision") { f.Source.ChangedRevision = true; }
        if (fault is "unavailable") { f.Source.Enabled = false; }
        if (fault is "binding") { f.Retained.Binding = null; }
        if (fault is "missing-source") { f.Source.Inventory = []; }
        if (fault is "stable-new-revision")
        { f.Source.FixedRevision = new(f.Source.Revision.Review, f.Source.Revision.Files, new(f.Source.Source.Id, Guid.NewGuid())); }
        var opening = FluentActions.Awaiting(() => f.Workflow.OpenSourceAsync(f.Target, binding, () => true, Token));
        if (fault is "new-revision") { await opening.Should().ThrowAsync<InvalidDataException>(); }
        else { await opening.Should().ThrowAsync<InvalidOperationException>(); }
        f.Retained.Adapter.Calls.Should().Be(0);
        f.Source.OriginalReads.Should().Be(0);
    }

    [Fact]
    public async Task SourceAndBindingChangesBetweenOpenReviewAndConfirmationCannotDiscloseOrCreateReplacementConversation()
    {
        await using var f = new Fixture();
        using var root = Root();
        var binding = await f.Bind();
        var opened = await f.Workflow.OpenSourceAsync(f.Target, binding, () => true, Token);
        f.Source.ChangedRevision = true;
        var changed = await f.Workflow.PrepareQuestionAsync(opened, "alpha?", () => true, Token);
        changed.Review.Should().BeNull();
        changed.Outcome.Should().Be(LocalFileAnswerOutcome.Unavailable);
        f.Source.ChangedRevision = false;
        var review = (await f.Workflow.PrepareQuestionAsync(opened, "alpha?", () => true, Token)).Review!;
        f.Retained.Binding = null;
        using var confirmation = Root();
        var result = await f.Workflow.AnswerAsync(review, () => true, Token);
        result.Claims.Should().BeEmpty();
        result.Outcome.Should().Be(LocalFileAnswerOutcome.Unavailable);
        f.Retained.Adapter.Calls.Should().Be(0);
        f.Retained.CreatedSessions.Should().Be(0);
    }

    [Fact]
    public async Task UnopenedOrRetiredSourceCannotPrepareQuestion()
    {
        await using var f = new Fixture();
        using var root = Root();
        var binding = await f.Bind();
        var opened = await f.Workflow.OpenSourceAsync(f.Target, binding, () => true, Token);
        await f.Workflow.RevokeAsync();
        await FluentActions.Awaiting(() => f.Workflow.PrepareQuestionAsync(opened, "alpha?", () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("model-input")]
    [InlineData("terminal-input")]
    [InlineData("cancelled")]
    [InlineData("disposed")]
    public async Task UnknownOrWithdrawnOriginalAuthorityFailsClosed(string fault)
    {
        await using var f = new Fixture();
        using var root = Root(fault is "model-input" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        if (fault is "terminal-input") { root.Complete(HostOperationOutcome.Completed); }
        if (fault is "disposed") { await f.Workflow.DisposeAsync(); }
        using var cancellation = new CancellationTokenSource();
        if (fault is "cancelled") { await cancellation.CancelAsync(); }
        await FluentActions.Awaiting(() => f.Workflow.ReviewBindingAsync(f.Target, f.Source.Source,
            () => fault is not "privacy", cancellation.Token)).Should().ThrowAsync<Exception>();
        f.Source.Requests.Should().BeEmpty();
        f.Retained.Binding.Should().BeNull();
    }

    [Fact]
    public async Task RevocationRetainsSourceOwnershipUntilCancellationIgnoringCanonicalModelPhysicallyJoins()
    {
        await using var f = new Fixture();
        using var root = Root();
        var binding = await f.Bind();
        var opened = await f.Workflow.OpenSourceAsync(f.Target, binding, () => true, Token);
        var review = (await f.Workflow.PrepareQuestionAsync(opened, "alpha?", () => true, Token)).Review!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Retained.Adapter.BeforeReply = async cancellation =>
        {
            using var registration = cancellation.Register(() => cancelled.TrySetResult());
            entered.SetResult();
            await release.Task;
        };
        using var confirmation = Root();
        var answering = f.Workflow.AnswerAsync(review, () => true, Token);
        await entered.Task.WaitAsync(Token);
        var disposed = f.Source.DisposedSelections;
        var retiring = f.Workflow.RevokeAsync();
        await cancelled.Task.WaitAsync(Token);
        retiring.IsCompleted.Should().BeFalse();
        f.Source.DisposedSelections.Should().Be(disposed);
        release.SetResult();
        await retiring;
        var result = await answering;
        result.Outcome.Should().Be(LocalFileAnswerOutcome.Stale);
        result.Claims.Should().BeEmpty();
        f.Source.DisposedSelections.Should().Be(disposed + 1);
        f.Retained.Models.IsQuiescent.Should().BeTrue();
        f.Retained.Message?.AssistantResponse.Should().BeNull();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("inactive")]
    [InlineData("missing-session")]
    [InlineData("missing-conversation")]
    public async Task ExistingConversationAnswerRequiresActiveTargetAndCompleteCanonicalComposition(string fault)
    {
        using var tracing = new KnowledgeSourceAnswerTests();
        using var source = new KnowledgeSourceAnswerTests.Fixture();
        await using var retained = new KnowledgeSourceAnswerTests.RetainedFixture(source, fault);
        var target = new SessionWorkspaceEntry(new(new(Guid.NewGuid()), new(1), fault is not "inactive"), null);
        using var root = Root();
        await FluentActions.Awaiting(() => retained.Workflow.PrepareForSessionAsync(source.Source, source.Revision.Reference,
            target, "alpha?", () => true, _ => Task.CompletedTask, Token)).Should().ThrowAsync<InvalidOperationException>();
        source.Requests.Should().BeEmpty();
        retained.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task MissingPrivateAssociationStoreIsAnExplicitFailureNotAnEmptyAssociation()
    {
        await using var f = new Fixture();
        using var root = Root();
        var workspace = new SessionWorkspaceService(null!, new(f.Retained), f.Retained, NullLogger<SessionWorkspaceService>.Instance);
        await FluentActions.Awaiting(() => workspace.ReadKnowledgeSourceBindingAsync(f.Target.Authority.SessionId, Token))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*association store is unavailable*");
        f.Source.Requests.Should().BeEmpty();
        f.Retained.Adapter.Calls.Should().Be(0);
    }
    private static HostActivity Root(RequestOrigin origin = RequestOrigin.LocalUi) =>
        HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly KnowledgeSourceAnswerTests tracing = new();
        internal KnowledgeSourceAnswerTests.Fixture Source { get; } = new();
        internal KnowledgeSourceAnswerTests.RetainedFixture Retained { get; }
        internal SessionKnowledgeSourceWorkflow Workflow { get; }
        internal SessionWorkspaceEntry Target { get; } = new(new(new(Guid.NewGuid()), new(1), true), null);
        internal Fixture()
        {
            Retained = new(Source, "supported") { Target = Target };
            Workflow = new(Retained.Workspace, Source.Manager, Source, Retained.Workflow, Source.Clock);
        }
        internal SessionKnowledgeSourceBinding Descriptor() =>
            new(Guid.NewGuid(), Guid.NewGuid(), Target.Authority.SessionId, Target.Authority.Generation, new(1),
                Source.Revision.Reference, Source.Source.Version);
        internal async Task<SessionKnowledgeSourceBinding> Bind()
        {
            SessionKnowledgeSourceWorkflow.BindingReview review;
            using (Root()) { review = await Workflow.ReviewBindingAsync(Target, Source.Source, () => true, Token); }
            using (Root()) { return await Workflow.ConfirmBindingAsync(review, () => true, Token); }
        }
        public async ValueTask DisposeAsync()
        {
            await Workflow.DisposeAsync();
            await Retained.DisposeAsync();
            Source.Dispose();
            tracing.Dispose();
        }
    }
}

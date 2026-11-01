using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Fact]
    public async Task AnswerReviewMetadataAndExactDetailsArePassiveAndDeliberateFlagHasItsOwnOriginalControl()
    {
        using var f = ReviewFixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var target = new SessionWorkspaceEntry(f.Session, null);
        var page = await f.Service.ReadAnswerReviewsAsync(target, ConversationAnswerReviewFilter.NotMarkedReviewed,
            null, 25, static () => true, f.Token);
        page.Should().BeSameAs(f.AnswerReviewPage);
        var row = page.Rows.Single();
        var detail = await f.Service.ReadAnswerReviewDetailAsync(page, row, static () => true, f.Token);
        detail.Source.Should().Be("inert retained answer");
        detail.ConversationMessage.Should().Be(f.Messages[0]);
        f.TaskWrites.Should().BeEmpty();
        var updated = await f.Service.SetAnswerReviewedAsync(page, row, true, static () => true, f.Token);
        updated.MarkedReviewed.Should().BeTrue();
        updated.ReviewRevision.Should().Be(1);
        f.TaskWrites.Should().HaveCount(2);
        f.TaskWrites.Should().OnlyContain(task => task.Request == f.Request);
        f.TaskWrites[0].State.Should().Be(HostTaskState.IntentRecorded);
        f.TaskWrites[1].State.Should().Be(HostTaskState.Succeeded);
        f.Messages[0].AssistantResponse!.Body.Should().Be("inert retained answer");
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("invocation")]
    [InlineData("session")]
    [InlineData("admission")]
    [InlineData("late-read")]
    [InlineData("late-detail")]
    [InlineData("control")]
    [InlineData("control-revision")]
    public async Task AnswerReviewOriginalSourceAndAwaitFencesRefuseWithoutAnyMutation(string failure)
    {
        using var f = ReviewFixture();
        var request = failure switch
        {
            "origin" => new(f.Request.RequestId, f.Request.SessionId, f.Request.TaskId, RequestOrigin.ActivatedVoice),
            "invocation" => new(f.Request.RequestId, f.Request.SessionId, f.Request.TaskId, RequestOrigin.LocalUi, new(Guid.NewGuid())),
            "session" => new(f.Request.RequestId, new(Guid.NewGuid()), f.Request.TaskId, RequestOrigin.LocalUi),
            _ => f.Request,
        };
        var admitted = failure is not "admission";
        f.AfterAnswerReviewRead = failure is "late-read" ? () => admitted = false : null;
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var page = f.AnswerReviewPage!;
        Func<Task> operation;
        if (failure is "late-detail")
        {
            f.BeforeExactRead = _ => admitted = false;
            operation = () => f.Service.ReadAnswerReviewDetailAsync(page, page.Rows[0], () => admitted, f.Token);
        }
        else if (failure is "control" or "control-revision")
        {
            f.BeforeAnswerReviewSource = () =>
            {
                if (failure is "control") { f.CanControl = false; }
                else { f.AdvanceRevision(); }
            };
            operation = () => f.Service.SetAnswerReviewedAsync(page, page.Rows[0], true, () => admitted, f.Token);
        }
        else
        {
            operation = () => f.Service.ReadAnswerReviewsAsync(page.Target, page.Filter, null, 25, () => admitted, f.Token);
        }
        await operation.Should().ThrowAsync<InvalidOperationException>();
        f.TaskWrites.Should().BeEmpty();
        f.AnswerReviewWrites.Should().Be(0);
    }

    [Fact]
    public async Task MissingAnswerReviewAuthorityIsUnavailableNotAnEmptySuccessfulInventory()
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        await f.Service.Invoking(service => service.ReadAnswerReviewsAsync(new(f.Session, null),
            ConversationAnswerReviewFilter.NotMarkedReviewed, null, 25, static () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*answer-review store*");
        f.TaskWrites.Should().BeEmpty();
    }

    private static ConversationFixture ReviewFixture()
    {
        var f = new ConversationFixture();
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), f.Request, ModelProviderSelection.OllamaCandidate);
        var message = new ConversationMessage(f.Request, new(1), 1, 3, DateTimeOffset.UtcNow, "original",
            ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome)
        {
            Version = 2,
            Execution = provenance,
            ExecutionMetadataRevision = 0,
            AssistantResponse = new(provenance, "inert retained answer"),
        };
        f.Messages = [message];
        f.AnswerReviewPage = new(new(f.Session, null), ConversationAnswerReviewFilter.NotMarkedReviewed,
            3, 0, 1, 1, [new(f.Request.RequestId, provenance.Turn.Value, 1, 3, message.SubmittedAt, 0, false)], null);
        return f;
    }

    private sealed partial class ConversationFixture : ISessionAnswerReviewStore
    {
        internal ConversationAnswerReviewPage? AnswerReviewPage { get; set; }
        internal Action? AfterAnswerReviewRead { get; set; }
        internal Action? BeforeAnswerReviewSource { get; set; }
        internal int AnswerReviewWrites { get; private set; }

        public ValueTask<ConversationAnswerReviewPage> ReadAnswerReviewsAsync(SessionWorkspaceEntry target,
            ConversationAnswerReviewFilter filter, ConversationAnswerReviewCursor? cursor, int limit, CancellationToken token)
        {
            AfterAnswerReviewRead?.Invoke();
            return ValueTask.FromResult(AnswerReviewPage!);
        }
        public ValueTask<ConversationMessage> ReadAnswerReviewMessageAsync(ConversationAnswerReviewPage page,
            ConversationAnswerReviewRow row, CancellationToken token)
        {
            BeforeAnswerReviewSource?.Invoke();
            return ValueTask.FromResult(Messages.Single(message => message.Request.RequestId == row.RequestId));
        }
        public async ValueTask<ConversationAnswerReviewRow> SetAnswerReviewedAsync(HostRequest control,
            ConversationAnswerReviewPage page, ConversationAnswerReviewRow row, bool reviewed, Func<bool> admitted, CancellationToken token)
        {
            admitted().Should().BeTrue();
            AnswerReviewWrites++;
            await CommitAsync(new(control, new(2), HostTaskState.Succeeded), 1, token);
            return row with { ReviewRevision = row.ReviewRevision + 1, MarkedReviewed = reviewed };
        }
    }
}

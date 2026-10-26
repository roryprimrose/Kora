using System.Text;
using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Dependencies;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class HostedClaudeSessionRequestTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealClaudeHttpStreamReachesCanonicalProvisionalPresentationBeforeTerminalAndNeverRetainsPreview(bool truncate)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var c = new Composition(f, streamAnswer: true);
        await c.InitializeAsync();
        var first = ClaudeMessagesRuntimeTests.StreamStart + ClaudeMessagesRuntimeTests.StreamText("safe provisional");
        var stream = new ClaudeMessagesRuntimeTests.ProgressiveStream(Encoding.UTF8.GetBytes(first
            + (truncate ? "" : ClaudeMessagesRuntimeTests.StreamText(" complete") + ClaudeMessagesRuntimeTests.StreamEnd)),
            1, Encoding.UTF8.GetByteCount(first));
        c.Http.Respond = request => request.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal)
            ? ClaudeAccountBrokerTests.Fixture.Json("{\"input_tokens\":25}") : ClaudeMessagesRuntimeTests.StreamingResponse(stream);
        var reviews = new List<Kora.Application.Dependencies.HostedTransmissionReview>();
        c.Reviews.ClaudeOffered += review =>
        {
            using var presentation = review.Presentation();
            c.Reviews.Confirm(review, true, RequestOrigin.ActivatedVoice).Should().BeFalse();
            c.Reviews.Confirm(review, true, RequestOrigin.LocalUi).Should().BeTrue();
            c.Reviews.Confirm(review, true, RequestOrigin.LocalUi).Should().BeFalse("consent is single-use");
            presentation.Complete(HostOperationOutcome.Completed);
            reviews.Add(review);
        };
        var updates = new List<ConversationAnswerPresentation>();
        c.Conversation.ProvisionalAnswerChanged += async (update, token) =>
        {
            update.IsCurrent().Should().BeTrue();
            var stored = await f.Store.ReadConversationMessageAsync(f.Request.SessionId, update.Target.Request.RequestId, token);
            stored!.AssistantResponse.Should().BeNull();
            stored.Outcome.Should().BeNull();
            updates.Add(update);
        };
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var pending = c.Conversation.SubmitClaudeAnswerStreamAsync(target, "  exact original\r\nClaude input  ", () => true, f.Token);
        await stream.Paused.Task.WaitAsync(f.Token);
        try
        {
            pending.IsCompleted.Should().BeFalse();
            c.Turns.IsQuiescent.Should().BeFalse();
            updates.Should().ContainSingle().Which.Update.Answer.Should().Be("safe provisional");
            reviews.Count.Should().Be(2);
            reviews[0].Should().NotBeSameAs(reviews[1]);
            var stored = await f.Store.ReadConversationMessageAsync(f.Request.SessionId, updates[0].Target.Request.RequestId, f.Token);
            stored!.AssistantResponse.Should().BeNull();
            stored.Outcome.Should().BeNull();
        }
        finally { stream.Release.TrySetResult(); }
        var receipt = await pending;
        receipt.Message.Body.Should().Be("  exact original\r\nClaude input  ");
        receipt.Message.Request.Should().Be(updates[0].Target.Request);
        receipt.Message.Outcome.Should().Be(truncate ? ModelTurnOutcome.Unknown : ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse?.Body.Should().Be(truncate ? null : "safe provisional complete");
        updates.Should().OnlyContain(value => !value.IsCurrent());
        stream.Disposed.Should().BeTrue();
        c.Turns.IsQuiescent.Should().BeTrue();
        WindowsSqliteConversationTests.AuditText(f).Should().NotContain("safe provisional")
            .And.NotContain("Claude input").And.NotContain(ClaudeAccountBrokerTests.Fixture.Credential);
    }
}

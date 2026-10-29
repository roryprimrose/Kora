using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Windows.IntegrationTests.Dependencies;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class HostedClaudeSessionRequestTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostedAnswerCancellationOrDeadlineKeepsPhysicalContinuationOwnedUntilActualJoin(bool cancel)
    {
        using var f = new InteractionStorageFixture();
        f.Time.UseFrozenTimers = true;
        await f.InitializeAsync(); await WindowsSqliteConversationTests.FinishInitial(f);
        await using var c = new Composition(f); await c.InitializeAsync();
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var messages = 0;
        c.Http.Send = (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal))
            { return Task.FromResult(ClaudeAccountBrokerTests.Fixture.Json("""{"input_tokens":25}""")); }
            if (++messages == 1) { return Task.FromResult(ClaudeAccountBrokerTests.Fixture.Json(QuestionProposal())); }
            entered.TrySetResult(); return release.Task;
        };
        c.Reviews.ClaudeOffered += review =>
        { using var presentation = review.Presentation(); c.Reviews.Confirm(review, true, RequestOrigin.LocalUi).Should().BeTrue(); };
        var operation = c.Conversation.SubmitClaudeWithClarificationAsync(target, "original question", () => true,
            async (request, _, spec, expiry, _, token) =>
            {
                await f.Store.PublishTrustedSnapshotAsync(request, f.Policy, null, 0, token);
                var initial = (await f.Questions.CreateAsync(request, spec, expiry, token)).Question!;
                return new(initial, await f.Questions.SubmitAsync(initial.Key, new(["option-1"]), RequestOrigin.LocalUi, token));
            }, f.Token);
        try
        {
            (await Task.WhenAny(entered.Task, operation).WaitAsync(f.Token)).Should().BeSameAs(entered.Task);
            if (cancel)
            {
                var running = (await c.Conversation.ReadRunningConversationAsync(target, () => true, f.Token))!;
                (await c.Conversation.CancelRunningConversationAsync(running.Target, () => true, f.Token))
                    .Phase.Should().Be(Kora.Core.Conversation.ConversationExecutionPhase.Cancelling);
            }
            else { c.TurnTime.FireDeadline(HostedTurnAuthorization.MaximumDuration); }
            operation.IsCompleted.Should().BeFalse();
            c.Runtime.IsQuiescent.Should().BeFalse();
            c.Turns.IsQuiescent.Should().BeFalse();
        }
        finally { release.TrySetResult(ClaudeAccountBrokerTests.Fixture.Json(ClaudeMessagesRuntimeTests.Answer)); }
        var receipt = await operation;
        receipt.Message.Outcome.Should().Be(cancel ? ModelTurnOutcome.Cancelled : ModelTurnOutcome.TimedOut);
        receipt.Message.AssistantResponse.Should().BeNull();
        receipt.Task.IsTerminal.Should().BeTrue();
        c.Http.Requests.Count(item => item.Method is "POST").Should().Be(4);
        c.Runtime.IsQuiescent.Should().BeTrue();
        c.Turns.IsQuiescent.Should().BeTrue();
    }

    internal static string QuestionProposal(int selections = 1) => Proposal(
        """{"type":"tool_use","id":"question_1","name":"ask_user","input":{"prompt":"Which format?","options":"""
        + (selections == 0 ? "[]" : """["Compact","Detailed"]""")
        + ""","minimum":1,"maximum":""" + Math.Max(1, selections).ToString(System.Globalization.CultureInfo.InvariantCulture) + "}}");

    [Theory]
    [InlineData(0, RequestOrigin.LocalUi, 4)]
    [InlineData(1, RequestOrigin.ActivatedVoice, 4)]
    [InlineData(2, RequestOrigin.LocalUi, 4)]
    [InlineData(1, RequestOrigin.LocalUi, 0)]
    [InlineData(1, RequestOrigin.LocalUi, 1)]
    [InlineData(1, RequestOrigin.ActivatedVoice, 2)]
    [InlineData(1, RequestOrigin.LocalUi, 3)]
    public async Task HostedQuestionUsesCanonicalStoredAnswerAndFourSeparatePhysicalSendConsents(int selections, RequestOrigin channel, int approvals)
    {
        using var f = new InteractionStorageFixture();
        f.Time.UseFrozenTimers = true;
        await f.InitializeAsync(); await WindowsSqliteConversationTests.FinishInitial(f);
        await using var c = new Composition(f); await c.InitializeAsync();
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        const string original = "  unchanged original\r\nhosted question  ";
        var messages = 0;
        c.Http.Respond = request => ClaudeAccountBrokerTests.Fixture.Json(request.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal)
            ? """{"input_tokens":25}""" : ++messages == 1 ? QuestionProposal(selections) : ClaudeMessagesRuntimeTests.Answer);
        var offered = 0;
        HostedTransmissionReview? previous = null;
        c.Reviews.ClaudeOffered += review =>
        {
            c.Http.Requests.Count(item => item.Method is "POST").Should().Be(offered);
            using var json = JsonDocument.Parse(review.Body.ToArray());
            json.RootElement.GetProperty("tools").GetArrayLength().Should().Be(1);
            json.RootElement.GetProperty("tools")[0].GetProperty("name").GetString().Should().Be("ask_user");
            using var presentation = review.Presentation();
            if (previous is not null) { c.Reviews.Confirm(previous, true, RequestOrigin.LocalUi).Should().BeFalse(); }
            c.Reviews.Confirm(review, true, RequestOrigin.ActivatedVoice).Should().BeFalse();
            c.Reviews.Confirm(review, ++offered <= approvals, RequestOrigin.LocalUi).Should().BeTrue();
            previous = review;
            presentation.Complete(HostOperationOutcome.Completed);
        };
        var asked = 0;
        var receipt = await c.Conversation.SubmitClaudeWithClarificationAsync(target, original, () => true,
            async (request, generation, spec, expiry, _, token) =>
            {
                asked++;
                c.Http.Requests.Count(item => item.Method is "POST").Should().Be(2);
                await f.Store.PublishTrustedSnapshotAsync(request, f.Policy, null, 0, token);
                var initial = (await f.Questions.CreateAsync(request, spec, expiry, token)).Question!;
                initial.SessionGeneration.Should().Be(generation);
                var draft = (await f.Questions.DraftAsync(initial.Key,
                    selections == 0 ? new([], "discarded draft") : new(["option-0"]), channel, token)).Question!;
                var completed = await f.Questions.SubmitAsync(draft.Key,
                    selections == 0 ? new([], "exact stored text") : selections == 1
                        ? new(["option-1"]) : new(["option-0", "option-1"]), channel, token);
                f.Time.Now += TimeSpan.FromSeconds(20);
                return new(initial, completed);
            }, f.Token);
        asked.Should().Be(approvals >= 2 ? 1 : 0);
        receipt.Message.Body.Should().Be(original);
        receipt.Message.Outcome.Should().Be(approvals == 4 ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.DeniedEgress);
        receipt.Message.AssistantResponse?.Body.Should().Be(approvals == 4 ? "genuine exact answer" : null);
        receipt.Task.IsTerminal.Should().BeTrue();
        var posts = c.Http.Requests.Where(item => item.Method is "POST").ToArray();
        posts.Should().HaveCount(approvals);
        if (approvals >= 3)
        {
            using var json = JsonDocument.Parse(posts[2].Body);
            var history = json.RootElement.GetProperty("messages");
            history.GetArrayLength().Should().Be(3);
            history[0].GetProperty("content").GetString().Should().Be(original);
            history[1].GetProperty("role").GetString().Should().Be("assistant");
            history[1].GetProperty("content")[0].GetProperty("name").GetString().Should().Be("ask_user");
            var result = history[2].GetProperty("content")[0];
            result.GetProperty("tool_use_id").GetString().Should().Be("question_1");
            using var answer = JsonDocument.Parse(result.GetProperty("content").GetString()!);
            answer.RootElement.GetProperty("channel").GetString().Should().Be(channel.ToString());
            answer.RootElement.GetProperty("choices").GetArrayLength().Should().Be(selections);
            if (selections == 0) { answer.RootElement.GetProperty("text").GetString().Should().Be("exact stored text"); }
            if (selections == 1) { answer.RootElement.GetProperty("choices")[0].GetString().Should().Be("option-1"); }
            answer.RootElement.EnumerateObject().Select(item => item.Name).Should().Equal("kind", "choices", "text", "channel");
        }
        (await f.Store.ReadConversationMessageAsync(f.Request.SessionId, receipt.Message.Request.RequestId, f.Token))
            .Should().Be(receipt.Message);
        c.Boundary.VersionReads.Should().Be(0);
        c.Turns.IsQuiescent.Should().BeTrue(); c.Runtime.IsQuiescent.Should().BeTrue();
        WindowsSqliteConversationTests.AuditText(f).Should().NotContain(original).And.NotContain("exact stored text")
            .And.NotContain("Which format?").And.NotContain("genuine exact answer");
    }

    [Fact]
    public async Task AnswerSubmissionAloneCannotTransmitWithoutNewPhysicalConsent()
    {
        using var f = new InteractionStorageFixture();
        f.Time.UseFrozenTimers = true;
        await f.InitializeAsync(); await WindowsSqliteConversationTests.FinishInitial(f);
        await using var c = new Composition(f); await c.InitializeAsync();
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        c.Http.Respond = request => ClaudeAccountBrokerTests.Fixture.Json(request.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal)
            ? """{"input_tokens":25}""" : QuestionProposal());
        var waiting = new TaskCompletionSource<HostedTransmissionReview>(TaskCreationOptions.RunContinuationsAsynchronously);
        var offered = 0;
        c.Reviews.ClaudeOffered += review =>
        {
            if (++offered == 3) { waiting.TrySetResult(review); return; }
            using var presentation = review.Presentation();
            c.Reviews.Confirm(review, true, RequestOrigin.LocalUi).Should().BeTrue();
        };
        var operation = c.Conversation.SubmitClaudeWithClarificationAsync(target, "original question", () => true,
            async (request, _, spec, expiry, _, token) =>
            {
                await f.Store.PublishTrustedSnapshotAsync(request, f.Policy, null, 0, token);
                var initial = (await f.Questions.CreateAsync(request, spec, expiry, token)).Question!;
                return new(initial, await f.Questions.SubmitAsync(initial.Key, new(["option-1"]), RequestOrigin.LocalUi, token));
            }, f.Token);
        var next = await Task.WhenAny(waiting.Task, operation).WaitAsync(f.Token);
        next.Should().BeSameAs(waiting.Task);
        operation.IsCompleted.Should().BeFalse();
        c.Http.Requests.Count(item => item.Method is "POST").Should().Be(2);
        var pending = await waiting.Task;
        using (var presentation = pending.Presentation())
        { c.Reviews.Confirm(pending, false, RequestOrigin.LocalUi).Should().BeTrue(); }
        var receipt = await operation;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
        receipt.Message.AssistantResponse.Should().BeNull();
        c.Http.Requests.Count(item => item.Method is "POST").Should().Be(2);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("draft")]
    [InlineData("foreign-question")]
    [InlineData("foreign-session")]
    [InlineData("foreign-generation")]
    [InlineData("substituted-answer")]
    [InlineData("expired")]
    [InlineData("account-revoked")]
    public async Task InvalidOrRevokedQuestionAnswerNeverCrossesTheHostedContinuationBoundary(string fault)
    {
        using var f = new InteractionStorageFixture();
        f.Time.UseFrozenTimers = true;
        await f.InitializeAsync(); await WindowsSqliteConversationTests.FinishInitial(f);
        await using var c = new Composition(f); await c.InitializeAsync();
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        c.Http.Respond = request => ClaudeAccountBrokerTests.Fixture.Json(request.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal)
            ? """{"input_tokens":25}""" : QuestionProposal());
        var offers = 0;
        c.Reviews.ClaudeOffered += review =>
        {
            offers++;
            using var presentation = review.Presentation();
            c.Reviews.Confirm(review, true, RequestOrigin.LocalUi).Should().BeTrue();
        };
        var receipt = await c.Conversation.SubmitClaudeWithClarificationAsync(target, "original question", () => true,
            async (request, _, spec, expiry, _, token) =>
            {
                await f.Store.PublishTrustedSnapshotAsync(request, f.Policy, null, 0, token);
                var initial = (await f.Questions.CreateAsync(request, spec, expiry, token)).Question!;
                var completed = fault switch
                {
                    "cancel" => await f.Questions.CancelAsync(initial.Key, token),
                    "draft" => await f.Questions.DraftAsync(initial.Key, new(["option-0"]), RequestOrigin.LocalUi, token),
                    _ => await f.Questions.SubmitAsync(initial.Key, new(["option-1"]), RequestOrigin.LocalUi, token),
                };
                if (fault is "foreign-question")
                { completed = completed with { Question = completed.Question! with { Key = new(request, new(Guid.NewGuid()), completed.Question.Key.Revision) } }; }
                if (fault is "foreign-session")
                { completed = completed with { Question = completed.Question! with { Key = new(HostRequest.Create(RequestOrigin.LocalUi), initial.Key.QuestionId, completed.Question.Key.Revision) } }; }
                if (fault is "foreign-generation")
                { completed = completed with { Question = completed.Question! with { SessionGeneration = new(99) } }; }
                if (fault is "substituted-answer")
                { completed = completed with { Question = completed.Question! with { Draft = new(["option-0"]) } }; }
                if (fault is "expired") { f.Time.Now = expiry; }
                if (fault is "account-revoked") { c.Accounts.Revoke(); }
                return new(initial, completed);
            }, f.Token);
        receipt.Message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse.Should().BeNull();
        receipt.Task.IsTerminal.Should().BeTrue();
        c.Http.Requests.Count(item => item.Method is "POST").Should().Be(2);
        offers.Should().Be(2);
        c.Boundary.VersionReads.Should().Be(0);
        c.Runtime.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("observation")]
    [InlineData("mixed")]
    [InlineData("repeat")]
    [InlineData("extra-field")]
    [InlineData("duplicate-field")]
    [InlineData("credential")]
    public async Task HostedQuestionCannotBorrowObservationAuthorityMixContentOrRepeat(string fault)
    {
        using var f = new InteractionStorageFixture();
        f.Time.UseFrozenTimers = true;
        await f.InitializeAsync(); await WindowsSqliteConversationTests.FinishInitial(f);
        await using var c = new Composition(f); await c.InitializeAsync();
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var wire = fault switch
        {
            "observation" => Proposal(VersionProposal),
            "mixed" => QuestionProposal().Replace("\"content\":[", "\"content\":[{\"type\":\"text\",\"text\":\"not final\"},", StringComparison.Ordinal),
            "extra-field" => QuestionProposal().Replace("\"minimum\":1", "\"approved\":true,\"minimum\":1", StringComparison.Ordinal),
            "duplicate-field" => QuestionProposal().Replace("\"minimum\":1", "\"minimum\":1,\"minimum\":1", StringComparison.Ordinal),
            "credential" => QuestionProposal().Replace("Which format?", "api_key=synthetic-only-value", StringComparison.Ordinal),
            _ => QuestionProposal(),
        };
        c.Http.Respond = request => ClaudeAccountBrokerTests.Fixture.Json(request.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal)
            ? """{"input_tokens":25}""" : wire);
        c.Reviews.ClaudeOffered += review =>
        { using var presentation = review.Presentation(); c.Reviews.Confirm(review, true, RequestOrigin.LocalUi).Should().BeTrue(); };
        var asks = 0;
        var receipt = await c.Conversation.SubmitClaudeWithClarificationAsync(target, "original question", () => true,
            async (request, _, spec, expiry, _, token) =>
            {
                asks++;
                await f.Store.PublishTrustedSnapshotAsync(request, f.Policy, null, 0, token);
                var initial = (await f.Questions.CreateAsync(request, spec, expiry, token)).Question!;
                return new(initial, await f.Questions.SubmitAsync(initial.Key, new(["option-1"]), RequestOrigin.LocalUi, token));
            }, f.Token);
        receipt.Message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse.Should().BeNull();
        asks.Should().Be(fault is "repeat" ? 1 : 0);
        c.Http.Requests.Count(item => item.Method is "POST").Should().Be(fault is "repeat" ? 4 : 2);
        c.Boundary.VersionReads.Should().Be(0);
    }
}

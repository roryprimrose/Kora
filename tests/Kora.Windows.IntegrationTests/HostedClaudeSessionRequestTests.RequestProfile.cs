using System.Text;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Dependencies;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class HostedClaudeSessionRequestTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, 2)]
    [InlineData(RequestOrigin.ActivatedVoice, 2)]
    [InlineData(RequestOrigin.ActivatedVoice, 1)]
    public async Task RequestOnlyClaudeModelUsesActualConfiguredAccountAndFreshPhysicalReviewsWithoutSaving(
        RequestOrigin origin, int approvals)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var c = new Composition(f);
        c.Http.ModelsJson = ClaudeAccountBrokerTests.Fixture.Page(
            [ClaudeAccountBrokerTests.Fixture.Model(), ClaudeAccountBrokerTests.Fixture.Model("request-model")]);
        await c.InitializeAsync();
        var catalogue = await c.Accounts.ObserveAsync(f.Token);
        var saved = c.Http.Profile(catalogue);
        var chosen = saved with { Model = new("request-model") };
        c.Http.Respond = message => ClaudeAccountBrokerTests.Fixture.Json(
            message.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal)
                ? "{\"input_tokens\":25}"
                : ClaudeMessagesRuntimeTests.Answer.Replace("\"model\":\"exact-model\"",
                    "\"model\":\"request-model\"", StringComparison.Ordinal));
        var preferences = new LocalProviderProfilePreferences(f.Paths);
        var before = preferences.Load()!.Serialize();
        var policy = await f.Store.ReadProviderProfile(f.Request.SessionId, f.Token);
        var commands = new SessionRequestCommandService(c.Workspace, c.Conversation, c.Access, f.Time);
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        const string body = "  original Claude request-only input\r\n  ";
        var review = (await commands.ExecuteAsync(new(SessionCommandOperation.RequestReview,
            f.Request.SessionId.Value, target.Authority.Generation.Value, target.Metadata?.Revision.Value ?? 0)
        { RequestBody = body, RequestLocalProfile = chosen }, origin, () => true, f.Token)).RequestReview!;
        var offers = 0;
        var expected = chosen;
        c.Reviews.ClaudeOffered += offered =>
        {
            offered.Profile.Should().Be(expected);
            Encoding.UTF8.GetString(offered.Body.AsSpan()).Should().Contain("\"" + expected.Model.Value + "\"");
            c.Http.Requests.Count(request => request.Method is "POST").Should().Be(Math.Min(offers, approvals));
            using var presentation = offered.Presentation();
            c.Reviews.Confirm(offered, ++offers <= approvals, RequestOrigin.LocalUi).Should().BeTrue();
            presentation.Complete(HostOperationOutcome.Completed);
        };
        var receipt = (await commands.ExecuteAsync(SessionCommand.Parse("session request confirm", "Kora")!,
            RequestOrigin.LocalUi, () => true, f.Token)).RequestSubmission!;
        receipt.Message.Outcome.Should().Be(approvals == 2 ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.DeniedEgress);
        receipt.Message.AssistantResponse?.Body.Should().Be(approvals == 2 ? "genuine exact answer" : null);
        receipt.Message.Request.Origin.Should().Be(origin);
        receipt.Message.Body.Should().Be(body);
        offers.Should().Be(2);
        if (approvals == 2)
        {
            expected = saved;
            await c.Conversation.SubmitClaudeAsync(target, "next inherited request", () => true, f.Token);
            offers.Should().Be(3, "the next inherited count request is independently disclosed and denied");
        }
        preferences.Load()!.Serialize().Should().Be(before);
        var after = await f.Store.ReadProviderProfile(f.Request.SessionId, f.Token);
        after.Revision.Should().Be(policy.Revision);
        after.Desired.Serialize().Should().Be(policy.Desired.Serialize());
        c.Turns.IsQuiescent.Should().BeTrue();
        WindowsSqliteConversationTests.AuditText(f).Should().NotContain(body).And.NotContain("genuine exact answer")
            .And.NotContain(ClaudeAccountBrokerTests.Fixture.Credential);
        review.LocalRequestProfile.Should().Be(chosen);
    }
}

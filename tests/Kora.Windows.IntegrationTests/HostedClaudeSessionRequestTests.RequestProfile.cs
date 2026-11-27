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
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task RequestOnlyClaudeModelUsesActualConfiguredAccountAndAutomaticPhysicalSendsWithoutSaving(
        RequestOrigin origin)
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

        var receipt = (await commands.ExecuteAsync(SessionCommand.Parse("session request confirm", "Kora")!,
            RequestOrigin.LocalUi, () => true, f.Token)).RequestSubmission!;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse?.Body.Should().Be("genuine exact answer");
        receipt.Message.Request.Origin.Should().Be(origin);
        receipt.Message.Body.Should().Be(body);
        IEnumerable<string> PostBodies() => c.Http.Requests.Where(request => request.Method is "POST")
            .Select(request => Encoding.UTF8.GetString(request.Body));
        PostBodies().Should().HaveCount(2).And.OnlyContain(text => text.Contains("\"" + chosen.Model.Value + "\""));
        var next = await c.Conversation.SubmitAsync(await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token),
            "next inherited request", () => true, f.Token);
        next.Message.Request.Origin.Should().Be(RequestOrigin.LocalUi);
        PostBodies().Skip(2).Should().HaveCount(2, "the next ordinary request inherits the saved profile")
            .And.OnlyContain(text => text.Contains("\"" + saved.Model.Value + "\""));
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

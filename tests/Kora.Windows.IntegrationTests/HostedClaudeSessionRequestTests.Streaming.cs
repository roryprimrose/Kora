using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Windows.IntegrationTests.Dependencies;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class HostedClaudeSessionRequestTests
{
    [Fact]
    public async Task SettingsRoutedClaudeSubmitStaysBufferedEvenWhenTheExactProfileIsStreamQualified()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var c = new Composition(f, streamAnswer: true);
        await c.InitializeAsync();
        var updates = 0;
        c.Conversation.ProvisionalAnswerChanged += (_, _) => { updates++; return ValueTask.CompletedTask; };
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var receipt = await c.Conversation.SubmitAsync(target, "  exact original\r\nClaude input  ", () => true, f.Token);
        receipt.Message.Body.Should().Be("  exact original\r\nClaude input  ");
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse?.Body.Should().Be("genuine exact answer");
        updates.Should().Be(0, "Settings-routed Claude answers are buffered in this increment");
        c.Http.Requests.Where(request => request.Method is "POST")
            .Should().HaveCount(2).And.OnlyContain(request => !System.Text.Encoding.UTF8.GetString(request.Body).Contains("\"stream\":true"));
        c.Turns.IsQuiescent.Should().BeTrue();
        WindowsSqliteConversationTests.AuditText(f).Should().NotContain("Claude input")
            .And.NotContain(ClaudeAccountBrokerTests.Fixture.Credential);
    }
}
using AwesomeAssertions;
using Kora.Application.Context;
using Kora.Application.Hosting.McpResultFollowUp;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("ordinary", "question", "{}", (int)McpResultSourceUseKind.QualifiedLocalOnly)]
    [InlineData("api_key=example", "question", "{}", (int)McpResultSourceUseKind.Denied)]
    [InlineData("ordinary", "api_key=example", "{}", (int)McpResultSourceUseKind.Denied)]
    [InlineData("ordinary", "question", """{"api\u005fkey":"example"}""", (int)McpResultSourceUseKind.Denied)]
    [InlineData("{broken", "question", "{}", (int)McpResultSourceUseKind.Unavailable)]
    public void MaintainedMcpSourcePolicyScreensOriginalQuestionAndWholeObservation(string text, string question,
        string json, int expected)
    {
        using var f = new Fixture();
        var observation = FollowUpObservation(f, text: text, json: json);
        IMcpResultSourceUse policy = LocalSourceUsePolicy.Instance;
        policy.Check(observation, question, RequestOrigin.LocalUi, Token).Kind.Should().Be((McpResultSourceUseKind)expected);
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SourceAndQuestionChannelsAreIndependentAndUnknownChannelsDeny(bool source)
    {
        using var f = new Fixture();
        var observation = FollowUpObservation(f, sourceOrigin: source ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        IMcpResultSourceUse policy = LocalSourceUsePolicy.Instance;
        policy.Check(observation, "question", source ? RequestOrigin.LocalUi : (RequestOrigin)999, Token)
            .Kind.Should().Be(McpResultSourceUseKind.Denied);
    }

    [Fact]
    public void MaintainedMcpPolicyHonorsCancellationAndRequiresExactInputs()
    {
        using var f = new Fixture();
        var observation = FollowUpObservation(f);
        IMcpResultSourceUse policy = LocalSourceUsePolicy.Instance;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        FluentActions.Invoking(() => policy.Check(observation, "question", RequestOrigin.LocalUi, cancellation.Token))
            .Should().Throw<OperationCanceledException>();
        FluentActions.Invoking(() => policy.Check(null!, "question", RequestOrigin.LocalUi, Token))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => policy.Check(observation, null!, RequestOrigin.LocalUi, Token))
            .Should().Throw<ArgumentNullException>();
    }
}

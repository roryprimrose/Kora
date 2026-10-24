using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Dependencies;

public sealed class UnavailableLocalModelReasonerTests
{
    [Fact]
    public async Task LegacyRequestCannotProduceAnAnswerActionOrGrantWithoutExactHostAdmission()
    {
        ILocalModelReasoner reasoner = new UnavailableLocalModelReasoner();
        var context = new LocalModelContext("Kora", true, null, [], []);
        var action = () => reasoner.ReasonAsync("private original request", context, null,
            TestContext.Current.CancellationToken);
        var failure = await action.Should().ThrowAsync<InvalidOperationException>();
        failure.Which.Message.Should().Contain("ModelTurnHost").And.NotContain("private original request");
    }

    [Fact]
    public async Task CancelledLegacyRequestPreservesCancellationWithoutAnyFallback()
    {
        ILocalModelReasoner reasoner = new UnavailableLocalModelReasoner();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var action = () => reasoner.ReasonAsync("request", new("Kora", false, null, [], []), null, cancellation.Token);
        var failure = await action.Should().ThrowAsync<OperationCanceledException>();
        failure.Which.CancellationToken.Should().Be(cancellation.Token);
    }
}

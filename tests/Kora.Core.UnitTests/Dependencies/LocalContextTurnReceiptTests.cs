using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class LocalContextTurnReceiptTests
{
    [Theory]
    [InlineData("model")]
    [InlineData("response")]
    [InlineData("answer")]
    public void AnswerIsAbsentWhenASuccessReceiptHasNoCompleteModelAnswer(string missing)
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var result = missing is "model" ? null : new ModelTurnResult(ModelTurnOutcome.Succeeded, ModelTurnReason.None,
            Response: missing is "response" ? null : new(null, null));
        var receipt = new LocalContextTurnReceipt(request, ModelTurnOutcome.Succeeded,
            LocalContextTurnReason.Completed, HostTaskState.Succeeded, result);

        receipt.Answer.Should().BeNull();
        receipt.Request.Should().BeSameAs(request);
        receipt.ModelResult.Should().BeSameAs(result);
    }

    [Theory]
    [InlineData(ModelTurnOutcome.Succeeded)]
    [InlineData(ModelTurnOutcome.Unknown)]
    [InlineData(ModelTurnOutcome.Unavailable)]
    [InlineData(ModelTurnOutcome.AuthenticationRequired)]
    [InlineData(ModelTurnOutcome.QuotaExceeded)]
    [InlineData(ModelTurnOutcome.TimedOut)]
    [InlineData(ModelTurnOutcome.Cancelled)]
    [InlineData(ModelTurnOutcome.DeniedEgress)]
    [InlineData(ModelTurnOutcome.Denied)]
    public void AnswerIsVisibleOnlyForTheSuccessfulReceiptOutcome(ModelTurnOutcome outcome)
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var result = new ModelTurnResult(ModelTurnOutcome.Succeeded, ModelTurnReason.None,
            Response: new("volatile answer", null));
        var receipt = new LocalContextTurnReceipt(request, outcome,
            LocalContextTurnReason.Completed, HostTaskState.Succeeded, result);

        receipt.Answer.Should().Be(outcome == ModelTurnOutcome.Succeeded ? "volatile answer" : null);
        receipt.ModelResult.Should().BeSameAs(result);
    }
}

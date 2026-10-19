using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class ModelContextAdmissionTests
{
    [Theory]
    [InlineData(ModelTurnOutcome.Denied)]
    [InlineData(ModelTurnOutcome.Cancelled)]
    [InlineData(ModelTurnOutcome.Unavailable)]
    [InlineData(ModelTurnOutcome.Unknown)]
    public void RejectedAdmissionPreservesExactCoreReceiptWithoutALease(ModelTurnOutcome outcome)
    {
        var receipt = new LocalContextTurnReceipt(HostRequest.Create(RequestOrigin.LocalUi),
            outcome, LocalContextTurnReason.SourceNotCurrent);
        var result = new ModelContextAdmission(receipt);
        result.Rejection.Should().BeSameAs(receipt);
        result.Turn.Should().BeNull();
    }

    [Fact]
    public void RejectedAdmissionCannotDescribeSuccess()
    {
        var receipt = new LocalContextTurnReceipt(HostRequest.Create(RequestOrigin.LocalUi),
            ModelTurnOutcome.Succeeded, LocalContextTurnReason.Completed);
        var create = () => new ModelContextAdmission(receipt);
        create.Should().Throw<InvalidDataException>();
    }
}
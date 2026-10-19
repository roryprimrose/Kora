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

    [Theory]
    [InlineData("origin")]
    [InlineData("request")]
    [InlineData("task")]
    public void ReviewedTargetCannotRelabelOriginOrAliasVolatileTaskIdentity(string alias)
    {
        var source = HostRequest.Create(RequestOrigin.ActivatedVoice);
        var proposed = new HostRequest(alias is "request" ? source.RequestId : new(Guid.NewGuid()),
            new(Guid.NewGuid()), alias is "task" ? source.TaskId : new(Guid.NewGuid()),
            alias is "origin" ? RequestOrigin.LocalUi : source.Origin);
        var create = () => LocalContextSessionTarget.ReviewedFresh(source, proposed);
        create.Should().Throw<InvalidDataException>();
    }
}
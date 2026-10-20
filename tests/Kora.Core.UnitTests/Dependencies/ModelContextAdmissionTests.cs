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

    [Fact]
    public void ExistingReviewedTargetKeepsSourceLineageDistinctFromDurableGeneration()
    {
        var source = HostRequest.Create(RequestOrigin.LocalUi);
        var durable = HostRequest.Create(RequestOrigin.LocalUi);
        var target = LocalContextSessionTarget.Existing(source, durable, new(2));
        target.SourceRequest.Should().BeSameAs(source);
        target.Request.Should().BeSameAs(durable);
        target.ExpectedGeneration.Should().Be(new HostRevision(2));
        target.CreatesSession.Should().BeFalse();
        target.TryConsume().Should().BeTrue();
        target.TryConsume().Should().BeFalse();
    }

    [Fact]
    public void SuccessfulReceiptWithMissingModelPayloadNeverInventsAnAnswer()
    {
        var receipt = new LocalContextTurnReceipt(HostRequest.Create(RequestOrigin.LocalUi), ModelTurnOutcome.Succeeded,
            LocalContextTurnReason.Completed);
        receipt.Answer.Should().BeNull();
        (receipt with { ModelResult = new(ModelTurnOutcome.Succeeded, ModelTurnReason.None) }).Answer.Should().BeNull();
        (receipt with { ModelResult = new(ModelTurnOutcome.Succeeded, ModelTurnReason.None, Response: new("exact answer", null)) })
            .Answer.Should().Be("exact answer");
        (receipt with { Outcome = ModelTurnOutcome.Denied,
            ModelResult = new(ModelTurnOutcome.Succeeded, ModelTurnReason.None, Response: new("must not disclose", null)) })
            .Answer.Should().BeNull();
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public void OriginalTargetsPreserveExactRequestAndValidateKnownGeneration(RequestOrigin origin)
    {
        var request = HostRequest.Create(origin);
        var fresh = LocalContextSessionTarget.ReviewedFresh(request);
        fresh.Request.Should().BeSameAs(request);
        fresh.SourceRequest.Should().BeSameAs(request);
        fresh.CreatesSession.Should().BeTrue();
        var existing = LocalContextSessionTarget.Existing(request, new(1));
        existing.CreatesSession.Should().BeFalse();
        var unknown = () => LocalContextSessionTarget.Existing(request, default);
        unknown.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void HostSystemCannotProposeAnOriginalUserTarget()
    {
        var create = () => LocalContextSessionTarget.ReviewedFresh(HostRequest.Create(RequestOrigin.HostSystem));
        create.Should().Throw<InvalidDataException>();
    }
}
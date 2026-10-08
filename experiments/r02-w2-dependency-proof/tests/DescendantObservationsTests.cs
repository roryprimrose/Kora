using AwesomeAssertions;
using ContainmentProof;

namespace W2Proof.Tests;

public sealed class DescendantObservationsTests
{
    [Fact]
    public void TrackedReportsDoNotRequireAdditionalTermination() =>
        DescendantObservations.ReportedChildrenAreTracked([12, 34], [12, 34, 56]).Should().BeTrue();

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(78)]
    public void UnknownReportedPidCannotClaimTrackedShutdown(int reported) =>
        DescendantObservations.ReportedChildrenAreTracked([12, reported], [12, 34, 56]).Should().BeFalse();
}

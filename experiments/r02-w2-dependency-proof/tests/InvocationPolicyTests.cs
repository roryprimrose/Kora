using AwesomeAssertions;
using ContainmentProof;

namespace W2Proof.Tests;

public sealed class InvocationPolicyTests
{
    [Fact]
    public void ExactOwnedTrialConsentIsAccepted() =>
        InvocationPolicy.RequireOwnedTrialConsent("consent-owned-scratch-local-network-synthetic-credential");

    [Theory]
    [InlineData("")]
    [InlineData("consent")]
    [InlineData("consent-filtered-buffered-events")]
    [InlineData("consent-application-endpoint-events-without-user-filter")]
    [InlineData("CONSENT-OWNED-SCRATCH-LOCAL-NETWORK-SYNTHETIC-CREDENTIAL")]
    public void MissingUnrelatedOrChangedConsentCannotStartTrials(string consent)
    {
        Action operation = () => InvocationPolicy.RequireOwnedTrialConsent(consent);
        operation.Should().Throw<ArgumentException>();
    }
}

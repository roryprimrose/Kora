using AwesomeAssertions;

using Kora.Core.Authorization;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Authorization;

public sealed class OperationGrantTests
{
    [Theory]
    [InlineData(OperationGrantScope.Once)]
    [InlineData(OperationGrantScope.Session)]
    [InlineData(OperationGrantScope.Perpetual)]
    public void RevokeRetainsBindingProvenanceAndUseHistoryWithoutExpiryOrScopeChange(OperationGrantScope scope)
    {
        var grant = Grant(scope) with { UseCount = 2, LastUsedAt = DateTimeOffset.UnixEpoch.AddDays(1) };
        var revoked = grant.Revoke("explicit-user-revocation");
        revoked.Should().Be(grant with { Revision = new(2), Status = OperationGrantStatus.Revoked, RevocationReason = "explicit-user-revocation" });
        revoked.Validate();
    }

    [Theory]
    [InlineData(OperationGrantStatus.Consumed)]
    [InlineData(OperationGrantStatus.Revoked)]
    public void RevokeNeverRestoresTerminalAuthority(OperationGrantStatus status)
    {
        var action = () => (Grant() with { Status = status }).Revoke("user-revocation");
        action.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("raw sensitive reason")]
    [InlineData("Uppercase")]
    [InlineData("user_0")]
    public void ValidateRejectsUnboundedOrNonContentFreeRevocationReasons(string reason)
    {
        var action = () => (Grant() with { RevocationReason = reason }).Validate();
        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ValidateBoundsRevocationMetadataAndRevisionArithmetic()
    {
        var grant = Grant();
        (grant with { RevocationReason = new string('a', 128) }).Validate();
        (grant with { RevocationReason = "reason.012-abc" }).Validate();
        var overlong = () => (grant with { RevocationReason = new string('a', 129) }).Validate();
        overlong.Should().Throw<InvalidDataException>();
        var invalid = () => grant.Revoke("invalid body");
        invalid.Should().Throw<InvalidDataException>();
        var overflow = () => (grant with { Revision = new(long.MaxValue) }).Revoke("reason");
        overflow.Should().Throw<OverflowException>();
    }

    private static OperationGrant Grant(OperationGrantScope scope = OperationGrantScope.Perpetual)
    {
        var request = new HostRequest(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()), RequestOrigin.LocalUi, new(Guid.NewGuid()));
        var digest = new string('a', 64);
        var binding = new ExactOperationBinding("bounded.read", "builtin", "inspect", digest, digest, digest,
            digest, digest, digest, digest, digest, digest, new(1));
        return new(new(Guid.NewGuid()), new(1),
            new(request, new(Guid.NewGuid()), new(1), binding, HostOperationEffect.BoundedRead, DateTimeOffset.UnixEpoch),
            scope, new(1), RequestOrigin.LocalUi, DateTimeOffset.UnixEpoch);
    }
}

using System.Globalization;

using AwesomeAssertions;

using Kora.Core.Authorization;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Authorization;

public sealed class ExactGrantQueryTests
{
    [Theory]
    [InlineData(OperationGrantScope.Perpetual, false, false, false, true)]
    [InlineData(OperationGrantScope.Session, false, false, false, false)]
    [InlineData(OperationGrantScope.Once, false, false, true, false)]
    [InlineData(OperationGrantScope.Perpetual, true, false, false, true)]
    [InlineData(OperationGrantScope.Session, true, true, false, true)]
    [InlineData(OperationGrantScope.Session, true, false, false, false)]
    [InlineData(OperationGrantScope.Once, true, true, false, false)]
    [InlineData(OperationGrantScope.Once, true, true, true, true)]
    [InlineData(OperationGrantScope.Once, true, false, true, false)]
    public void ManagementViewsNeverIncludeOtherSessionsTransientAuthority(OperationGrantScope scope,
        bool sessionView, bool sameSession, bool history, bool expected)
    {
        var target = new HostId<SessionIdentity>(Guid.NewGuid());
        var digest = new string('a', 64);
        var request = new HostRequest(new(Guid.NewGuid()), sameSession ? target : new(Guid.NewGuid()),
            new(Guid.NewGuid()), RequestOrigin.LocalUi, new(Guid.NewGuid()));
        var binding = new ExactOperationBinding("bounded.read", "source", "skill", digest, digest, digest, digest,
            digest, digest, digest, digest, digest, new(1));
        var grant = new OperationGrant(new(Guid.NewGuid()), new(1),
            new(request, new(Guid.NewGuid()), new(1), binding, HostOperationEffect.BoundedRead, DateTimeOffset.UnixEpoch),
            scope, new(1), RequestOrigin.LocalUi, DateTimeOffset.UnixEpoch);
        var query = new ExactGrantQuery(status: OperationGrantStatus.Active, managementView: true,
            managementSession: sessionView ? target : null, includeOnceHistory: history);
        query.Matches(grant).Should().Be(expected);
        query.Matches(grant with { Status = OperationGrantStatus.Revoked }).Should().BeFalse();
        query.Digest.Should().NotBe(ExactGrantQuery.Unfiltered.Digest);
        query.Digest.Should().NotBe(new ExactGrantQuery(status: OperationGrantStatus.Active,
            managementView: true, managementSession: new(Guid.NewGuid()), includeOnceHistory: history).Digest);
        new ExactGrantQuery(managementView: true, managementSession: target, includeOnceHistory: true).Digest
            .Should().NotBe(new ExactGrantQuery(managementView: true, managementSession: target).Digest);
    }

    [Fact]
    public void SessionManagementFlagsRequireManagementViewAndValidatedSessionIdentity()
    {
        var session = () => new ExactGrantQuery(managementSession: new(Guid.NewGuid()));
        session.Should().Throw<InvalidDataException>();
        var history = () => new ExactGrantQuery(includeOnceHistory: true);
        history.Should().Throw<InvalidDataException>();
        var empty = () => new ExactGrantQuery(managementView: true, managementSession: default(HostId<SessionIdentity>));
        empty.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("Any", "Any", null, null)]
    [InlineData("Once", "Active", OperationGrantScope.Once, OperationGrantStatus.Active)]
    [InlineData("Session", "Consumed", OperationGrantScope.Session, OperationGrantStatus.Consumed)]
    [InlineData("Perpetual", "Revoked", OperationGrantScope.Perpetual, OperationGrantStatus.Revoked)]
    public void NativeKnownStoredValuesHaveStableTypedQueryIdentity(string scope, string status,
        OperationGrantScope? expectedScope, OperationGrantStatus? expectedStatus)
    {
        var session = new HostId<SessionIdentity>(Guid.Parse("12345678-abcd-1234-abcd-123456789abc"));
        var query = ExactGrantQuery.FromInput(scope, status, "bounded.read", session.Value.ToString("D"));
        query.Scope.Should().Be(expectedScope);
        query.Status.Should().Be(expectedStatus);
        query.ActionId.Should().Be("bounded.read");
        query.OriginatingSession.Should().Be(session);
        query.Digest.Should().HaveLength(64).And.MatchRegex("^[a-f0-9]+$");
        query.Should().Be(new ExactGrantQuery(expectedScope, expectedStatus, "bounded.read", session));
        query.Digest.Should().NotBe(ExactGrantQuery.Unfiltered.Digest);
        ExactGrantQuery.FromInput("Any", "Any", "", "").Should().Be(ExactGrantQuery.Unfiltered);
    }

    [Theory]
    [InlineData("any", "Any", "", "")]
    [InlineData("0", "Any", "", "")]
    [InlineData("Any", "active", "", "")]
    [InlineData("Any", "1", "", "")]
    [InlineData("Any", "Any", " Bounded.read", "")]
    [InlineData("Any", "Any", "bounded.read\n", "")]
    [InlineData("Any", "Any", "bounded/read", "")]
    [InlineData("Any", "Any", "", "00000000-0000-0000-0000-000000000000")]
    [InlineData("Any", "Any", "", "12345678123412341234123456789abc")]
    [InlineData("Any", "Any", "", "12345678-ABCD-1234-ABCD-123456789ABC")]
    [InlineData("Any", "Any", "", " 12345678-abcd-1234-abcd-123456789abc")]
    [InlineData("Any", "Any", "", "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx")]
    [InlineData("Any", "Any", "", " ")]
    public void InvalidNativeFiltersAreDeniedRatherThanNormalizedOrReplacedWithUnfiltered(string scope,
        string status, string action, string origin)
    {
        var parse = () => ExactGrantQuery.FromInput(scope, status, action, origin);
        parse.Should().Throw<Exception>();
    }

    [Fact]
    public void UndefinedTypedValuesNullInputsEmptyIdentityAndIdentifierBoundsAreDenied()
    {
        var invalidScope = () => new ExactGrantQuery((OperationGrantScope)99);
        invalidScope.Should().Throw<InvalidDataException>();
        var invalidStatus = () => new ExactGrantQuery(status: (OperationGrantStatus)99);
        invalidStatus.Should().Throw<InvalidDataException>();
        var emptyIdentity = () => new ExactGrantQuery(originatingSession: default(HostId<SessionIdentity>));
        emptyIdentity.Should().Throw<InvalidDataException>();
        var oversize = () => new ExactGrantQuery(actionId: new string('a', 129));
        oversize.Should().Throw<ArgumentException>();
        new ExactGrantQuery(actionId: new string('a', 128)).ActionId.Should().HaveLength(128);
        var nullAction = () => ExactGrantQuery.FromInput("Any", "Any", null!, "");
        nullAction.Should().Throw<ArgumentNullException>();
        var nullOrigin = () => ExactGrantQuery.FromInput("Any", "Any", "", null!);
        nullOrigin.Should().Throw<ArgumentNullException>();
        var nullGrant = () => ExactGrantQuery.Unfiltered.Matches(null!);
        nullGrant.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EverySpecifiedPredicateIsRequiredAndOriginIsOnlyRecordedCorrelation(int mismatch)
    {
        var session = new HostId<SessionIdentity>(Guid.NewGuid());
        var digest = new string('a', 64);
        var request = new HostRequest(new(Guid.NewGuid()), session, new(Guid.NewGuid()), RequestOrigin.LocalUi, new(Guid.NewGuid()));
        var binding = new ExactOperationBinding("bounded.read", "source", "skill", digest, digest, digest, digest, digest, digest,
            digest, digest, digest, new(1));
        var grant = new OperationGrant(new(Guid.NewGuid()), new(1),
            new(request, new(Guid.NewGuid()), new(1), binding, HostOperationEffect.BoundedRead, DateTimeOffset.UnixEpoch),
            OperationGrantScope.Perpetual, new(1), RequestOrigin.LocalUi, DateTimeOffset.UnixEpoch, OperationGrantStatus.Revoked);
        var query = new ExactGrantQuery(mismatch == 1 ? OperationGrantScope.Once : OperationGrantScope.Perpetual,
            mismatch == 2 ? OperationGrantStatus.Active : OperationGrantStatus.Revoked,
            mismatch == 3 ? "source" : "bounded.read", mismatch == 4 ? new(Guid.NewGuid()) : session);
        query.Matches(grant).Should().Be(mismatch == 0);
        ExactGrantQuery.Unfiltered.Matches(grant).Should().BeTrue();
        var prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            new ExactGrantQuery(query.Scope, query.Status, query.ActionId, query.OriginatingSession).Should().Be(query);
            query.Matches(grant).Should().Be(mismatch == 0);
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }
}

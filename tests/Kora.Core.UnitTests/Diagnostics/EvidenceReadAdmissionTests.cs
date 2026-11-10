using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Diagnostics;

public sealed class EvidenceReadAdmissionTests
{
    private static readonly bool[] InvocationStates = [false, true];
    public static IEnumerable<object[]> Channels => Enum.GetValues<RequestOrigin>()
        .SelectMany(origin => Enum.GetValues<EvidenceSource>().SelectMany(source =>
            InvocationStates.Select(invoked => new object[] { origin, source, invoked })));

    [Theory]
    [MemberData(nameof(Channels))]
    public void OriginalChannelPolicyNeverBroadensVoiceToDiagnosticsOrInvocations(RequestOrigin origin, EvidenceSource source, bool invoked)
    {
        var request = new HostRequest(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()),
            origin, invoked ? new HostId<InvocationIdentity>(Guid.NewGuid()) : null);
        var expected = !invoked && (origin == RequestOrigin.LocalUi
            || origin == RequestOrigin.ActivatedVoice && source == EvidenceSource.AuthorityAudit);
        EvidenceReadAdmission.IsAllowed(request, source).Should().Be(expected);
    }

    [Fact]
    public void MissingRequestCannotEstablishOriginalUserAuthority() =>
        FluentActions.Invoking(() => EvidenceReadAdmission.IsAllowed(null!, EvidenceSource.AuthorityAudit))
            .Should().Throw<ArgumentNullException>();
}

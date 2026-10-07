using AwesomeAssertions;
using Kora.Core.Authorization;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Authorization;

public sealed class HostAuthorizationPolicyTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void Reuse_depends_on_protected_observation_and_independent_ignore_policy(bool protectedCall, bool ignore, bool allows)
    {
        new HostAuthorizationPolicy(true, true, protectedCall, ignore).AllowsReusableGrants.Should().Be(allows);
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi, false, true)]
    [InlineData(RequestOrigin.LocalUi, true, true)]
    [InlineData(RequestOrigin.ActivatedVoice, false, true)]
    [InlineData(RequestOrigin.ActivatedVoice, true, false)]
    [InlineData(RequestOrigin.HostSystem, false, false)]
    [InlineData(RequestOrigin.HostSystem, true, false)]
    [InlineData((RequestOrigin)99, false, false)]
    public void Settings_origin_is_not_relabelled_by_confirmation(RequestOrigin origin, bool protectedCall, bool allowed)
    {
        new HostAuthorizationPolicy(true, true, protectedCall, true).AllowsVoiceOrCallSettings(origin).Should().Be(allowed);
    }
}

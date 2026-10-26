using AwesomeAssertions;
using Kora.Core.Authorization;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Authorization;

public sealed class HostAuthorizationPolicyTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, true, false, false, true)]
    [InlineData(RequestOrigin.ActivatedVoice, true, false, false, true)]
    [InlineData(RequestOrigin.ActivatedVoice, true, true, false, false)]
    [InlineData(RequestOrigin.ActivatedVoice, true, true, true, true)]
    [InlineData(RequestOrigin.LocalUi, false, false, true, false)]
    [InlineData(RequestOrigin.HostSystem, true, false, true, false)]
    public void Approval_channel_does_not_change_grant_or_session_authority(
        RequestOrigin channel, bool unlocked, bool call, bool spoken, bool allowed) =>
        new HostAuthorizationPolicy(unlocked, true, call, true, spoken).AllowsApprovalChannel(channel).Should().Be(allowed);
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void Calls_do_not_withdraw_exact_grants_or_reinterpret_legacy_ignore_policy(bool protectedCall, bool ignore, bool allows)
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

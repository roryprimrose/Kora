using AwesomeAssertions;

using Kora.Core.Configuration;
using Kora.Core.Storage;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Configuration;

public sealed class SessionRetentionSettingsTests
{
    [Fact]
    public void Default_is_one_day_archive_and_thirty_day_deletion_and_activation_validates()
    {
        SessionRetentionSettings.Default.Should().Be(new SessionRetentionSettings(1, 30));
        var policy = new SessionRetentionPolicy();
        policy.Settings.Should().Be(SessionRetentionSettings.Default);
        policy.Activate(new(2, 60));
        policy.Settings.Should().Be(new SessionRetentionSettings(2, 60));
        policy.HoldUnavailable();
        policy.Invoking(value => _ = value.Settings).Should().Throw<InvalidDataException>();
        policy.Activate(SessionRetentionSettings.Default);
        policy.Settings.Should().Be(SessionRetentionSettings.Default);
        var id = new HostId<SessionIdentity>(Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;
        var clock = new SessionRetentionState(id, now, now.AddDays(1), now.AddDays(30), true, false);
        clock.SessionId.Should().Be(id);
        clock.LastMeaningfulActivity.Should().Be(now);
        clock.ArchiveDue.Should().Be(now.AddDays(1));
        clock.DeleteDue.Should().Be(now.AddDays(30));
        clock.Perpetual.Should().BeTrue();
        clock.Purged.Should().BeFalse();
        policy.Invoking(value => value.Activate(default)).Should().Throw<ArgumentOutOfRangeException>();
        policy.Settings.Should().Be(SessionRetentionSettings.Default);
    }

    [Theory]
    [InlineData("1", "2")]
    [InlineData("1", "30")]
    [InlineData("364", "365")]
    public void Canonical_whole_days_are_accepted(string archive, string delete)
    {
        SessionRetentionSettings.Parse(archive, delete).Validate();
    }

    [Theory]
    [InlineData("0", "30")]
    [InlineData("1", "1")]
    [InlineData("30", "2")]
    [InlineData("1", "366")]
    [InlineData("365", "365")]
    [InlineData("-1", "30")]
    [InlineData("01", "30")]
    [InlineData("1", "030")]
    [InlineData(" 1", "30")]
    [InlineData("1.0", "30")]
    [InlineData("1", "+30")]
    [InlineData("", "30")]
    [InlineData("1", "2147483648")]
    public void Invalid_days_are_never_clamped_or_defaulted(string archive, string delete)
    {
        var action = () => SessionRetentionSettings.Parse(archive, delete);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}

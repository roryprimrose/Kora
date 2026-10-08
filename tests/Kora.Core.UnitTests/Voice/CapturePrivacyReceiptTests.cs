using AwesomeAssertions;
using Kora.Core.Platform;

namespace Kora.Core.UnitTests.Voice;

public sealed class CapturePrivacyReceiptTests
{
    [Theory]
    [InlineData(499999, true)]
    [InlineData(500000, true)]
    [InlineData(500001, false)]
    [InlineData(501000, false)]
    [InlineData(-1, false)]
    public void Every_lock_receipt_uses_the_exact_500_ms_boundary(long elapsedTicks, bool expected)
    {
        var observation = new PrivacyObservation(Guid.NewGuid(), 1000000, 1000000);
        var receipt = new CapturePrivacyReceiptEventArgs(observation, 7, WindowsSessionState.Locked,
            true, true, false, 1000001, 0, observation.ObservedTimestamp + elapsedTicks, true);

        receipt.LockReleaseWithinTarget.Should().Be(expected);
        receipt.ObservedToReleaseMilliseconds.Should().Be(elapsedTicks / 1000d);
    }

    [Fact]
    public void Missing_release_or_idle_lock_is_not_a_timing_pass()
    {
        var observation = new PrivacyObservation(Guid.NewGuid(), 100, 1000);
        var missing = new CapturePrivacyReceiptEventArgs(observation, 7, WindowsSessionState.Locked,
            true, true, true, 101, 0, null, false);
        var idle = new CapturePrivacyReceiptEventArgs(observation, 7, WindowsSessionState.Locked,
            false, false, false, 101, 0, 102, true);

        missing.LockReleaseWithinTarget.Should().BeNull();
        missing.ObservedToReleaseMilliseconds.Should().BeNull();
        idle.LockReleaseWithinTarget.Should().BeNull();
        observation.OsEventToNotificationDelayMilliseconds.Should().BeNull();
    }

    [Fact]
    public void Failed_release_cannot_pass_even_when_a_timestamp_is_present()
    {
        var receipt = new CapturePrivacyReceiptEventArgs(new PrivacyObservation(Guid.NewGuid(), 100, 1000),
            7, WindowsSessionState.Locked, true, true, false, 101, 0, 102, false);

        receipt.LockReleaseWithinTarget.Should().BeFalse();
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void Closing_admission_does_not_hide_native_handle_release(long elapsedTicks, bool expected)
    {
        var receipt = new CapturePrivacyReceiptEventArgs(new PrivacyObservation(Guid.NewGuid(), 1000, 1000),
            7, WindowsSessionState.Locked, false, true, false, 1001, 0, 1000 + elapsedTicks, true);

        receipt.LockReleaseWithinTarget.Should().Be(expected);
        receipt.ObservedToReleaseMilliseconds.Should().Be(elapsedTicks);
    }

    [Fact]
    public void Previously_released_retiring_recorder_is_not_an_active_lock_timing_pass()
    {
        var receipt = new CapturePrivacyReceiptEventArgs(new PrivacyObservation(Guid.NewGuid(), 1000, 1000),
            7, WindowsSessionState.Locked, false, true, false, 999, 0, 999, true);

        receipt.ReleaseConfirmed.Should().BeTrue();
        receipt.LockReleaseWithinTarget.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_clock_frequency_is_rejected(long frequency)
    {
        var action = () => new PrivacyObservation(Guid.NewGuid(), 1, frequency);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Missing_observation_identity_is_rejected()
    {
        var action = () => new PrivacyObservation(Guid.Empty, 1, 1000);

        action.Should().Throw<ArgumentException>();
    }
}

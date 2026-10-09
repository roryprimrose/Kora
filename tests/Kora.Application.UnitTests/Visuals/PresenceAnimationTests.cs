using AwesomeAssertions;

using Kora.Application.Visuals;
using Kora.Core;

namespace Kora.Application.UnitTests.Visuals;

public sealed class PresenceAnimationTests
{
    [Theory]
    [InlineData(AssistantState.Hidden, 0xB8, 0xD9, 0xEC, 0)]
    [InlineData(AssistantState.Listening, 0x6A, 0xE1, 0xDA, 1)]
    [InlineData(AssistantState.Calculating, 0xAF, 0x9B, 0xFF, 1)]
    [InlineData(AssistantState.Waiting, 0xF0, 0xCC, 0x83, 1)]
    [InlineData(AssistantState.Executing, 0x80, 0xB7, 0xFF, 1)]
    [InlineData(AssistantState.Success, 0x9B, 0xDF, 0xAC, 1)]
    [InlineData(AssistantState.Failure, 0xF4, 0x9C, 0x9C, 1)]
    [InlineData(AssistantState.Information, 0xB8, 0xD9, 0xEC, 1)]
    [InlineData((AssistantState)999, 0xB8, 0xD9, 0xEC, 1)]
    public void Constructor_uses_the_state_palette_and_visibility(
        AssistantState state,
        byte red,
        byte green,
        byte blue,
        double opacity)
    {
        var animation = new PresenceAnimation(state);

        animation.Current.Should().Be(
            new PresenceVisualFrame(new PresenceColor(red, green, blue), opacity, 1));
    }

    [Fact]
    public void Advance_fades_between_hidden_and_visible_states()
    {
        var animation = new PresenceAnimation(AssistantState.Hidden);

        animation.Advance(
            AssistantState.Information,
            isSpeaking: false,
            speechOutputLevel: 0,
            PresenceAnimation.VisibilityTransitionDuration / 2).Should().BeTrue();
        animation.Current.Opacity.Should().Be(0.5);

        animation.Advance(
            AssistantState.Information,
            isSpeaking: false,
            speechOutputLevel: 0,
            PresenceAnimation.VisibilityTransitionDuration / 2).Should().BeTrue();
        animation.Current.Opacity.Should().Be(1);

        animation.Advance(
            AssistantState.Hidden,
            isSpeaking: false,
            speechOutputLevel: 0,
            PresenceAnimation.VisibilityTransitionDuration).Should().BeTrue();
        animation.Current.Opacity.Should().Be(0);
        animation.Current.Color.Should().Be(new PresenceColor(0xB8, 0xD9, 0xEC));
    }

    [Fact]
    public void Advance_fades_when_visibility_changes_without_changing_state()
    {
        var animation = new PresenceAnimation(AssistantState.Information);

        animation.Advance(
            AssistantState.Information,
            isSpeaking: false,
            speechOutputLevel: 0,
            PresenceAnimation.VisibilityTransitionDuration / 2,
            isVisible: false).Should().BeTrue();
        animation.Current.Opacity.Should().Be(0.5);

        animation.Advance(
            AssistantState.Information,
            isSpeaking: false,
            speechOutputLevel: 0,
            PresenceAnimation.VisibilityTransitionDuration / 2,
            isVisible: false).Should().BeTrue();
        animation.Current.Opacity.Should().Be(0);

        animation.Advance(
            AssistantState.Information,
            isSpeaking: false,
            speechOutputLevel: 0,
            PresenceAnimation.VisibilityTransitionDuration / 2).Should().BeTrue();
        animation.Current.Opacity.Should().Be(0.5);
    }

    [Fact]
    public void Advance_blends_to_the_new_state_color()
    {
        var animation = new PresenceAnimation(AssistantState.Failure);

        animation.Advance(
            AssistantState.Success,
            isSpeaking: false,
            speechOutputLevel: 0,
            PresenceAnimation.FrameInterval).Should().BeTrue();

        animation.Current.Color.Should().NotBe(new PresenceColor(0xF4, 0x9C, 0x9C));
        animation.Current.Color.Should().NotBe(new PresenceColor(0x9B, 0xDF, 0xAC));

        animation.Advance(
            AssistantState.Success,
            isSpeaking: false,
            speechOutputLevel: 0,
            PresenceAnimation.ColorTransitionDuration).Should().BeTrue();

        animation.Current.Color.Should().Be(new PresenceColor(0x9B, 0xDF, 0xAC));
    }

    [Fact]
    public void Advance_maps_speech_level_to_shrink_and_growth()
    {
        var animation = new PresenceAnimation(AssistantState.Information);

        animation.Advance(
            AssistantState.Information,
            isSpeaking: true,
            speechOutputLevel: -1,
            TimeSpan.FromSeconds(1)).Should().BeTrue();
        animation.Current.Scale.Should().Be(0.9);

        animation.Advance(
            AssistantState.Information,
            isSpeaking: true,
            speechOutputLevel: 2,
            TimeSpan.FromSeconds(1)).Should().BeTrue();
        animation.Current.Scale.Should().Be(1.12);

        animation.Advance(
            AssistantState.Information,
            isSpeaking: false,
            speechOutputLevel: double.NaN,
            TimeSpan.FromSeconds(1)).Should().BeTrue();
        animation.Current.Scale.Should().Be(1);
    }

    [Fact]
    public void Advance_with_no_elapsed_time_reports_no_change()
    {
        var animation = new PresenceAnimation(AssistantState.Information);

        animation.Advance(
            AssistantState.Information,
            isSpeaking: false,
            speechOutputLevel: 0,
            TimeSpan.Zero).Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(0, 1, 1)]
    [InlineData(50, 0, 0.95)]
    [InlineData(50, 1, 1.06)]
    [InlineData(100, 0, 0.9)]
    [InlineData(100, 1, 1.12)]
    [InlineData(200, 0, 0.8)]
    [InlineData(200, 1, 1.24)]
    public void Speech_scale_amount_controls_distance_from_resting_size(
        int amount,
        double level,
        double expectedScale)
    {
        var animation = new PresenceAnimation(AssistantState.Information);

        animation.Advance(
            AssistantState.Information,
            isSpeaking: true,
            level,
            TimeSpan.FromSeconds(1),
            isSpeechScalingEnabled: true,
            amount);

        animation.Current.Scale.Should().BeApproximately(expectedScale, 0.000001);
    }

    [Theory]
    [InlineData(false, 100)]
    [InlineData(true, 0)]
    public void Disabling_speech_scaling_or_setting_zero_returns_to_rest_without_changing_colour(
        bool enabled,
        int amount)
    {
        var animation = new PresenceAnimation(AssistantState.Information);
        animation.Advance(
            AssistantState.Information, true, 1, TimeSpan.FromSeconds(1));
        animation.Current.Scale.Should().Be(1.12);

        animation.Advance(
            AssistantState.Executing,
            true,
            1,
            TimeSpan.FromSeconds(1),
            enabled,
            amount);

        animation.Current.Scale.Should().Be(1);
        animation.Current.Color.Should().Be(new PresenceColor(0x80, 0xB7, 0xFF));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(201)]
    public void Advance_rejects_invalid_speech_scale_amount(int amount)
    {
        var animation = new PresenceAnimation(AssistantState.Information);
        var action = () => animation.Advance(
            AssistantState.Information, false, 0, TimeSpan.Zero, true, amount);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0, 0.9)]
    [InlineData(1, 1.12)]
    public void Speech_edges_ease_in_instead_of_jumping_to_the_target(double level, double target)
    {
        var animation = new PresenceAnimation(AssistantState.Information);

        animation.Advance(
            AssistantState.Information, true, level, PresenceAnimation.FrameInterval);

        var response = (animation.Current.Scale - 1) / (target - 1);
        response.Should().BeInRange(0.2, 0.3);
    }

    [Theory]
    [InlineData(0, 0.9)]
    [InlineData(1, 1.12)]
    public void Speech_smoothing_tracks_a_syllable_within_150_milliseconds(double level, double target)
    {
        var animation = new PresenceAnimation(AssistantState.Information);

        animation.Advance(
            AssistantState.Information, true, level, TimeSpan.FromMilliseconds(150));

        var response = (animation.Current.Scale - 1) / (target - 1);
        response.Should().BeInRange(0.9, 1);
    }

    [Fact]
    public void Small_speech_fluctuations_are_filtered_instead_of_snapping_each_frame()
    {
        var animation = new PresenceAnimation(AssistantState.Information);
        animation.Advance(AssistantState.Information, true, 5d / 11, TimeSpan.FromSeconds(1));
        var previousScale = animation.Current.Scale;
        for (var index = 0; index < 60; index++)
        {
            var level = (5d / 11) + (index % 2 == 0 ? 0.04 : -0.04);
            animation.Advance(AssistantState.Information, true, level, PresenceAnimation.FrameInterval);

            Math.Abs(animation.Current.Scale - previousScale).Should().BeLessThan(0.005);
            previousScale = animation.Current.Scale;
        }
    }

    [Fact]
    public void Short_syllables_remain_distinct_with_less_than_one_frame_of_peak_lag()
    {
        var animation = new PresenceAnimation(AssistantState.Information);
        for (var syllable = 0; syllable < 3; syllable++)
        {
            for (var frame = 0; frame < 6; frame++)
            {
                animation.Advance(AssistantState.Information, true, 1, TimeSpan.FromMilliseconds(10));
            }

            var peak = animation.Current.Scale;
            var peakDelayMilliseconds = 0;
            for (var frame = 1; frame <= 20; frame++)
            {
                animation.Advance(AssistantState.Information, true, 5d / 11, TimeSpan.FromMilliseconds(10));
                if (animation.Current.Scale > peak)
                {
                    peak = animation.Current.Scale;
                    peakDelayMilliseconds = frame * 10;
                }
            }

            peak.Should().BeGreaterThan(1.05);
            peakDelayMilliseconds.Should().BeLessThanOrEqualTo(30);
            animation.Current.Scale.Should().BeInRange(1, 1.003);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Speech_smoothing_is_independent_of_frame_partitioning(double level)
    {
        var singleFrame = new PresenceAnimation(AssistantState.Information);
        var regularFrames = new PresenceAnimation(AssistantState.Information);
        var irregularFrames = new PresenceAnimation(AssistantState.Information);

        singleFrame.Advance(AssistantState.Information, true, level, TimeSpan.FromMilliseconds(150));
        for (var index = 0; index < 15; index++)
        {
            regularFrames.Advance(AssistantState.Information, true, level, TimeSpan.FromMilliseconds(10));
        }
        foreach (var milliseconds in new[] { 17, 33, 50, 9, 41 })
        {
            irregularFrames.Advance(AssistantState.Information, true, level, TimeSpan.FromMilliseconds(milliseconds));
        }

        regularFrames.Current.Scale.Should().BeApproximately(singleFrame.Current.Scale, 0.000000000001);
        irregularFrames.Current.Scale.Should().BeApproximately(singleFrame.Current.Scale, 0.000000000001);
    }

    [Fact]
    public void Rapid_speech_reversals_do_not_overshoot_the_configured_range()
    {
        var animation = new PresenceAnimation(AssistantState.Information);
        for (var index = 0; index < 120; index++)
        {
            animation.Advance(
                AssistantState.Information, true, index % 2, PresenceAnimation.FrameInterval, true, 200);

            animation.Current.Scale.Should().BeInRange(0.8, 1.24);
        }
    }

    [Theory]
    [InlineData(false, true, 100)]
    [InlineData(true, false, 100)]
    [InlineData(true, true, 0)]
    public void Ending_or_disabling_speech_smoothly_settles_at_exact_resting_size(
        bool speaking,
        bool enabled,
        int amount)
    {
        var animation = new PresenceAnimation(AssistantState.Information);
        animation.Advance(AssistantState.Information, true, 1, TimeSpan.FromSeconds(1));

        animation.Advance(
            AssistantState.Information, speaking, 1, PresenceAnimation.FrameInterval, enabled, amount);

        animation.Current.Scale.Should().BeInRange(1.08, 1.12);
        animation.Advance(
            AssistantState.Information, speaking, 1, TimeSpan.FromSeconds(1), enabled, amount);
        animation.Current.Scale.Should().Be(1);
        animation.Advance(
            AssistantState.Information, speaking, 1, PresenceAnimation.FrameInterval, enabled, amount)
            .Should().BeFalse();
    }

    [Fact]
    public void Zero_elapsed_time_does_not_change_the_smoothing_history()
    {
        var baseline = new PresenceAnimation(AssistantState.Information);
        var animation = new PresenceAnimation(AssistantState.Information);

        animation.Advance(AssistantState.Information, true, 0, TimeSpan.Zero).Should().BeFalse();
        animation.Advance(AssistantState.Information, true, 1, PresenceAnimation.FrameInterval);
        baseline.Advance(AssistantState.Information, true, 1, PresenceAnimation.FrameInterval);

        animation.Current.Should().Be(baseline.Current);
    }

    [Fact]
    public void Advance_rejects_negative_elapsed_time()
    {
        var animation = new PresenceAnimation(AssistantState.Information);

        var action = () => animation.Advance(
            AssistantState.Information,
            isSpeaking: false,
            speechOutputLevel: 0,
            TimeSpan.FromMilliseconds(-1));

        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("elapsed");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Advance_rejects_non_finite_active_speech_levels(double speechOutputLevel)
    {
        var animation = new PresenceAnimation(AssistantState.Information);

        var action = () => animation.Advance(
            AssistantState.Information,
            isSpeaking: true,
            speechOutputLevel,
            TimeSpan.FromMilliseconds(1));

        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(nameof(speechOutputLevel));
    }
}
using AwesomeAssertions;

using Kora.Application.Visuals;
using Kora.Core;

namespace Kora.Application.UnitTests.Visuals;

public sealed class ConstellationAnimationTests
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
        var animation = new ConstellationAnimation(state);

        animation.Current.Should().Be(
            new ConstellationVisualFrame(new ConstellationColor(red, green, blue), opacity, 1));
    }

    [Fact]
    public void Advance_fades_between_hidden_and_visible_states()
    {
        var animation = new ConstellationAnimation(AssistantState.Hidden);

        animation.Advance(
            AssistantState.Information,
            isSpeaking: false,
            speechOutputLevel: 0,
            ConstellationAnimation.VisibilityTransitionDuration / 2).Should().BeTrue();
        animation.Current.Opacity.Should().Be(0.5);

        animation.Advance(
            AssistantState.Information,
            isSpeaking: false,
            speechOutputLevel: 0,
            ConstellationAnimation.VisibilityTransitionDuration / 2).Should().BeTrue();
        animation.Current.Opacity.Should().Be(1);

        animation.Advance(
            AssistantState.Hidden,
            isSpeaking: false,
            speechOutputLevel: 0,
            ConstellationAnimation.VisibilityTransitionDuration).Should().BeTrue();
        animation.Current.Opacity.Should().Be(0);
        animation.Current.Color.Should().Be(new ConstellationColor(0xB8, 0xD9, 0xEC));
    }

    [Fact]
    public void Advance_blends_to_the_new_state_color()
    {
        var animation = new ConstellationAnimation(AssistantState.Failure);

        animation.Advance(
            AssistantState.Success,
            isSpeaking: false,
            speechOutputLevel: 0,
            ConstellationAnimation.ColorTransitionDuration).Should().BeTrue();

        animation.Current.Color.Should().Be(new ConstellationColor(0x9B, 0xDF, 0xAC));
    }

    [Fact]
    public void Advance_maps_speech_level_to_shrink_and_growth()
    {
        var animation = new ConstellationAnimation(AssistantState.Information);

        animation.Advance(
            AssistantState.Information,
            isSpeaking: true,
            speechOutputLevel: -1,
            ConstellationAnimation.SpeechScaleTransitionDuration).Should().BeTrue();
        animation.Current.Scale.Should().Be(0.9);

        animation.Advance(
            AssistantState.Information,
            isSpeaking: true,
            speechOutputLevel: 2,
            ConstellationAnimation.SpeechScaleTransitionDuration).Should().BeTrue();
        animation.Current.Scale.Should().Be(1.12);

        animation.Advance(
            AssistantState.Information,
            isSpeaking: false,
            speechOutputLevel: double.NaN,
            ConstellationAnimation.SpeechScaleTransitionDuration).Should().BeTrue();
        animation.Current.Scale.Should().Be(1);
    }

    [Fact]
    public void Advance_with_no_elapsed_time_reports_no_change()
    {
        var animation = new ConstellationAnimation(AssistantState.Information);

        animation.Advance(
            AssistantState.Information,
            isSpeaking: false,
            speechOutputLevel: 0,
            TimeSpan.Zero).Should().BeFalse();
    }

    [Fact]
    public void Advance_rejects_negative_elapsed_time()
    {
        var animation = new ConstellationAnimation(AssistantState.Information);

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
        var animation = new ConstellationAnimation(AssistantState.Information);

        var action = () => animation.Advance(
            AssistantState.Information,
            isSpeaking: true,
            speechOutputLevel,
            TimeSpan.FromMilliseconds(1));

        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(nameof(speechOutputLevel));
    }
}
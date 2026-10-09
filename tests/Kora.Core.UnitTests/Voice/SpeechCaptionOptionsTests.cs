using AwesomeAssertions;
using Kora.Core.Voice;

namespace Kora.Core.UnitTests.Voice;

public sealed class SpeechCaptionOptionsTests
{
    [Fact]
    public void Defaults_and_exact_changes_preserve_the_companion()
    {
        var defaults = SpeechCaptionOptions.Default;
        defaults.Placement.Should().Be(SpeechCaptionPlacement.BottomRight);
        defaults.DismissalDelaySeconds.Should().Be(5);
        var positioned = defaults.With(new SpeechCaptionValue.Placement(SpeechCaptionPlacement.TopLeft));
        positioned.DismissalDelaySeconds.Should().Be(5);
        positioned.With(new SpeechCaptionValue.Delay(30)).Placement.Should().Be(SpeechCaptionPlacement.TopLeft);
        new SpeechCaptionValue.Delay(30).Option.Should().Be(SpeechCaptionOption.DismissalDelay);
        new SpeechCaptionValue.Delay(30).Label.Should().Be("30");
        new SpeechCaptionValue.Placement(SpeechCaptionPlacement.TopLeft).Option.Should().Be(SpeechCaptionOption.Placement);
        new SpeechCaptionValue.Placement(SpeechCaptionPlacement.TopLeft).Label.Should().Be("TopLeft");
        defaults.Invoking(item => item.With(null!)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(31)]
    public void Out_of_range_delays_are_rejected(int seconds)
    {
        var create = () => new SpeechCaptionOptions(SpeechCaptionPlacement.BottomRight, seconds);
        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Unknown_placement_is_rejected()
    {
        var create = () => new SpeechCaptionOptions((SpeechCaptionPlacement)99, 0);
        create.Should().Throw<ArgumentOutOfRangeException>();
    }
}

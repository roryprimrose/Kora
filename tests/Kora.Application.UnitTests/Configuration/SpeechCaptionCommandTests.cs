using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Configuration;

public sealed class SpeechCaptionCommandTests
{
    [Fact]
    public void All_advertised_phrases_round_trip_with_current_name_and_without_model_interpretation()
    {
        foreach (var phrase in SpeechTextCommand.FixedPhrases)
        {
            SpeechTextCommand.Parse(phrase, "Nova")!.Operation.Should().NotBe(AppearanceCommandOperation.Clarify, phrase);
            SpeechTextCommand.Parse("Nova, " + phrase.ToUpperInvariant(), "Nova")!.Operation.Should().NotBe(AppearanceCommandOperation.Clarify, phrase);
        }
        SpeechTextCommand.Parse("set " + SpeechTextCommand.PlacementId + " to TopLeft", "Kora")!.CaptionValue
            .Should().Be(new SpeechCaptionValue.Placement(SpeechCaptionPlacement.TopLeft));
        SpeechTextCommand.Parse("set " + SpeechTextCommand.DelayId + " to 30", "Kora")!.CaptionValue
            .Should().Be(new SpeechCaptionValue.Delay(30));
        SpeechTextCommand.Parse("reset " + SpeechTextCommand.PinId, "Kora")!.PinValue.Should().BeFalse();
        SpeechTextCommand.Parse("get " + SpeechTextCommand.PinId, "Kora")!.IsPinControl.Should().BeTrue();
        SpeechTextCommand.Parse("set " + SpeechTextCommand.PinId + " to true", "Kora")!.PinValue.Should().BeTrue();
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("31")]
    [InlineData("05")]
    [InlineData("+5")]
    [InlineData("5.0")]
    [InlineData("5 seconds")]
    public void Noncanonical_or_out_of_range_delay_is_reserved_and_rejected(string value) =>
        SpeechTextCommand.Parse("set " + SpeechTextCommand.DelayId + " to " + value, "Kora")!.Operation
            .Should().Be(AppearanceCommandOperation.Clarify);
}

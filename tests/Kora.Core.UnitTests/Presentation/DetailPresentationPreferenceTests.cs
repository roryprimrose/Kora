using AwesomeAssertions;
using Kora.Core.Presentation;

namespace Kora.Core.UnitTests.Presentation;

public sealed class DetailPresentationPreferenceTests
{
    [Theory]
    [InlineData(DetailPresentationMode.Offer, "Offer")]
    [InlineData(DetailPresentationMode.LinkOnly, "LinkOnly")]
    public void ExactCanonicalValuesRoundTrip(DetailPresentationMode mode, string text)
    {
        DetailPresentationPreference.Format(mode).Should().Be(text);
        DetailPresentationPreference.Parse(text).Should().Be(mode);
    }

    [Fact]
    public void MissingLegacyValueIsUnsavedWithUnchangedOfferDefault()
    {
        DetailPresentationPreference.Parse(null).Should().BeNull();
        DetailPresentationPreference.Default.Should().Be(DetailPresentationMode.Offer);
    }

    [Theory]
    [InlineData("")]
    [InlineData("offer")]
    [InlineData("Link only")]
    [InlineData("LinkOnly\n")]
    [InlineData("Automatic")]
    [InlineData("OpenAutomatically")]
    [InlineData("0")]
    public void MalformedOrUnavailableModesAreNotDefaults(string value)
    {
        var parse = () => DetailPresentationPreference.Parse(value);
        parse.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void UndefinedModesCannotBeSerialized()
    {
        var format = () => DetailPresentationPreference.Format((DetailPresentationMode)2);
        format.Should().Throw<ArgumentOutOfRangeException>();
    }
}

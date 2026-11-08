using AwesomeAssertions;
using Kora.Core.Presentation;

namespace Kora.Core.UnitTests.Presentation;

public sealed class MarkdownDisplayPreferenceTests
{
    [Fact]
    public void MissingLegacyValueIsUnsavedRenderedDefault()
    {
        MarkdownDisplayPreference.Parse(null).Should().BeNull();
        MarkdownDisplayPreference.Default.Should().Be(MarkdownDisplayMode.Rendered);
    }

    [Theory]
    [InlineData(MarkdownDisplayMode.Rendered, "Rendered")]
    [InlineData(MarkdownDisplayMode.Source, "Source")]
    public void ClosedValuesRoundTripExactly(MarkdownDisplayMode mode, string value)
    {
        MarkdownDisplayPreference.Format(mode).Should().Be(value);
        MarkdownDisplayPreference.Parse(value).Should().Be(mode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("source")]
    [InlineData("Rendered\n")]
    [InlineData("HTML")]
    public void MalformedAndUnknownValuesNeverBecomeRendered(string value) =>
        new Action(() => MarkdownDisplayPreference.Parse(value)).Should().Throw<InvalidDataException>();

    [Fact]
    public void UnknownEnumCannotBeSaved() =>
        new Action(() => MarkdownDisplayPreference.Format((MarkdownDisplayMode)2)).Should().Throw<ArgumentOutOfRangeException>();
}

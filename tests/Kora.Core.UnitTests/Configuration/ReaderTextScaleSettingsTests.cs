using AwesomeAssertions;

using Kora.Core.Configuration;

namespace Kora.Core.UnitTests.Configuration;

public sealed class ReaderTextScaleSettingsTests
{
    [Theory]
    [InlineData(75)]
    [InlineData(100)]
    [InlineData(200)]
    public void ValidatePercentAdmitsExactDocumentedExtremesAndDefault(int percent)
    {
        ReaderTextScaleSettings.ValidatePercent(percent);
        var descriptor = AppearanceOptionRegistry.Get(AppearanceOption.ReaderTextScale);
        descriptor.Id.Should().Be("appearance.reader-text-scale");
        descriptor.Default.Should().Be(new AppearanceValue.Number(100));
        descriptor.Minimum.Should().Be(75);
        descriptor.Maximum.Should().Be(200);
        descriptor.Units.Should().Be("percent");
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(74)]
    [InlineData(201)]
    [InlineData(int.MaxValue)]
    public void ValidatePercentRejectsOutsideExactDocumentedRange(int percent)
    {
        var action = () => ReaderTextScaleSettings.ValidatePercent(percent);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}

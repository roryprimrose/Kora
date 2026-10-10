using AwesomeAssertions;
using Kora.Core.Dependencies;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class ModelProviderModePreferenceTests
{
    [Theory]
    [InlineData(ModelProviderMode.LocalOnly)]
    [InlineData(ModelProviderMode.LocalFirst)]
    [InlineData(ModelProviderMode.HostedPreferred)]
    public void CanonicalModesRoundTrip(ModelProviderMode mode) =>
        ModelProviderModePreference.Parse(ModelProviderModePreference.Serialize(mode)).Should().Be(mode);

    [Theory]
    [InlineData("")]
    [InlineData("Unknown")]
    [InlineData("1")]
    [InlineData("localonly")]
    [InlineData(" LocalOnly")]
    [InlineData("LocalOnly,LocalFirst")]
    public void NoncanonicalSavedModesAreRejected(string value) =>
        FluentActions.Invoking(() => ModelProviderModePreference.Parse(value)).Should().Throw<InvalidDataException>();

    [Theory]
    [InlineData(ModelProviderMode.Unknown)]
    [InlineData((ModelProviderMode)999)]
    public void InvalidModesCannotBeSaved(ModelProviderMode mode) =>
        FluentActions.Invoking(() => ModelProviderModePreference.Serialize(mode)).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void DefaultAndDiscoverableChoicesArePrivacyFirst()
    {
        ModelProviderModePreference.Default.Should().Be(ModelProviderMode.LocalOnly);
        ModelProviderModePreference.Choices.Should().Equal(ModelProviderMode.LocalOnly, ModelProviderMode.LocalFirst, ModelProviderMode.HostedPreferred);
    }
}

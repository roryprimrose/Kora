using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class NativeFixtureEvidenceModeTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("synchronous", false)]
    [InlineData("buffered", true)]
    public void IsBufferedResolvesOnlyExplicitProfilesAndPreservesTheDefault(string? mode, bool expected)
    {
        NativeFixtureEvidenceMode.IsBuffered(mode).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("BUFFERED")]
    [InlineData("buffered\n")]
    public void IsBufferedRejectsUnknownProfiles(string mode)
    {
        var resolve = () => NativeFixtureEvidenceMode.IsBuffered(mode);

        resolve.Should().Throw<InvalidDataException>();
    }
}

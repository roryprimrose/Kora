using AwesomeAssertions;
using Kora.Core.Voice;

namespace Kora.Core.UnitTests.Voice;

public sealed class SpeechInputSelectionTests
{
    [Fact]
    public void CatalogueContainsOnlyTheExistingWindowsAdapter()
    {
        SpeechInputProfileCatalogue.Profiles.Should().ContainSingle().Which.Id.Should().Be(SpeechInputSelection.Default.ProfileId);
        SpeechInputProfileCatalogue.UnavailableReason(SpeechInputSelection.Default).Should().BeNull();
    }

    [Theory]
    [InlineData(SpeechInputResource.Cpu, SpeechInputResidency.CaptureLifetime)]
    [InlineData(SpeechInputResource.Gpu, SpeechInputResidency.KeepLoaded)]
    [InlineData(SpeechInputResource.WindowsManaged, SpeechInputResidency.KeepLoaded)]
    public void KnownUnsupportedPreferencesAreUnavailableWithoutFallback(SpeechInputResource resource, SpeechInputResidency residency)
    {
        var selection = SpeechInputSelection.Default with { Resource = resource, Residency = residency };
        selection.Validate();
        SpeechInputProfileCatalogue.UnavailableReason(selection).Should().Contain("not qualified").And.Contain("No other engine");
    }

    [Theory]
    [InlineData("unknown", SpeechInputResource.WindowsManaged, SpeechInputResidency.CaptureLifetime)]
    [InlineData("windows-activated-v1", (SpeechInputResource)99, SpeechInputResidency.CaptureLifetime)]
    [InlineData("windows-activated-v1", SpeechInputResource.WindowsManaged, (SpeechInputResidency)99)]
    public void UnknownTupleCannotSelectAProvider(string profile, SpeechInputResource resource, SpeechInputResidency residency)
    {
        var selection = new SpeechInputSelection(profile, resource, residency);
        selection.Invoking(value => value.Validate()).Should().Throw<ArgumentOutOfRangeException>();
    }
}

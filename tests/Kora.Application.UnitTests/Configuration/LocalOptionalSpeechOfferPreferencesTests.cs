using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalOptionalSpeechOfferPreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"Kora.OfferTests.{Guid.NewGuid():N}");

    [Fact]
    public void Load_returns_unhandled_when_no_preference_exists()
    {
        CreatePreferences().Load().Should().Be(new OptionalSpeechOfferState(false, null));
    }

    [Fact]
    public void Save_persists_the_offer_response_across_instances()
    {
        CreatePreferences().Save(new OptionalSpeechOfferState(true, "kokoro"));

        CreatePreferences().Load().Should().Be(new OptionalSpeechOfferState(true, "kokoro"));
        File.Exists(Path.Combine(root, "Preferences", "optional-speech-offer.tmp"))
            .Should().BeFalse();
    }

    [Fact]
    public void Load_rejects_an_incomplete_offer_state()
    {
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "optional-speech-offer.json"), "{}");

        var action = CreatePreferences().Load;

        action.Should().Throw<InvalidDataException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private LocalOptionalSpeechOfferPreferences CreatePreferences() =>
        new(new TestPaths(root));

    private sealed record TestPaths(string LocalRoot) : IApplicationDataPaths
    {
        public string RoamingRoot => LocalRoot;
    }
}

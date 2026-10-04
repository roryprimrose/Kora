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

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"MissingProviderNotified":null}""")]
    [InlineData("""{"InitialOfferHandled":null,"MissingProviderNotified":null}""")]
    [InlineData("""{"InitialOfferHandled":false}""")]
    [InlineData("""{"InitialOfferHandled":false,"MissingProviderNotified":false}""")]
    public void Load_rejects_invalid_offer_state(string json)
    {
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "optional-speech-offer.json"), json);

        var action = CreatePreferences().Load;

        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Load_accepts_unhandled_offer_with_null_provider()
    {
        CreatePreferences().Save(new OptionalSpeechOfferState(false, null));

        CreatePreferences().Load().Should().Be(new OptionalSpeechOfferState(false, null));
    }

    [Fact]
    public void Save_rejects_null_state()
    {
        var action = () => CreatePreferences().Save(null!);

        action.Should().Throw<ArgumentNullException>();
        Directory.Exists(root).Should().BeFalse();
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

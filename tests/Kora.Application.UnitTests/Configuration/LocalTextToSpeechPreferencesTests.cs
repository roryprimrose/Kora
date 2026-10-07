using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Configuration;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging.Abstractions;
using Neovolve.Logging.Xunit;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalTextToSpeechPreferencesTests(ITestOutputHelper output)
    : LoggingTestsBase<LocalTextToSpeechPreferences>(output), IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"Kora.Tests.{Guid.NewGuid():N}");

    [Fact]
    public void LoadVoiceId_returns_null_when_no_preference_exists()
    {
        var preferences = CreatePreferences();

        var result = preferences.LoadVoiceId();

        result.Should().BeNull();
    }

    [Fact]
    public void LoadProviderId_returns_null_when_no_preference_exists()
    {
        var preferences = CreatePreferences();

        var result = preferences.LoadProviderId();

        result.Should().BeNull();
    }

    [Fact]
    public void Coherent_selection_migrates_legacy_without_writing_then_shadows_it_atomically()
    {
        var preferences = CreatePreferences();
        preferences.LoadSelection().Should().BeNull();
        preferences.SaveVoiceId("voice");
        preferences.LoadSelection().Should().Be(new SpeechSelection(SpeechProviderIds.Windows, "voice"));
        preferences.SaveProviderId(SpeechProviderIds.Kokoro);
        preferences.LoadSelection().Should().Be(new SpeechSelection(SpeechProviderIds.Kokoro, "voice"));
        preferences.SaveSelection(SpeechSelection.Default);
        preferences.LoadSelection().Should().Be(SpeechSelection.Default);
        preferences.SaveSelection(new(SpeechProviderIds.Kokoro, new string('v', SpeechSelection.MaximumVoiceIdLength)));
        CreatePreferences().LoadSelection()!.VoiceId!.Length.Should().Be(SpeechSelection.MaximumVoiceIdLength);
        Directory.GetFiles(Path.Combine(root, "Preferences"), "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("2\nwindows-sapi\n")]
    [InlineData("1\nunknown\nvoice")]
    [InlineData("1\nwindows-sapi\n padded")]
    [InlineData("1\nwindows-sapi\nvoice\nunexpected")]
    public void Coherent_unknown_or_malformed_formats_are_explicit_errors(string contents)
    {
        Directory.CreateDirectory(Path.Combine(root, "Preferences"));
        File.WriteAllText(Path.Combine(root, "Preferences", "speech-selection.txt"), contents);
        var load = CreatePreferences().LoadSelection;
        load.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Unknown_legacy_provider_is_not_defaulted_and_invalid_saves_do_not_replace_valid_state()
    {
        var preferences = CreatePreferences();
        preferences.SaveProviderId("unknown");
        var legacy = preferences.LoadSelection;
        legacy.Should().Throw<InvalidDataException>();
        preferences.SaveSelection(SpeechSelection.Default);
        var invalid = () => preferences.SaveSelection(new("unknown", null));
        invalid.Should().Throw<ArgumentOutOfRangeException>();
        var missing = () => preferences.SaveSelection(null!);
        missing.Should().Throw<ArgumentNullException>();
        preferences.LoadSelection().Should().Be(SpeechSelection.Default);
    }

    [Fact]
    public void Structured_preference_diagnostics_do_not_record_voice_identifiers()
    {
        var preferences = new LocalTextToSpeechPreferences(
            new TestPaths(root, Path.Combine(root, "Roaming")), Logger);
        preferences.SaveProviderId(SpeechProviderIds.Windows);
        preferences.SaveVoiceId("private voice");
        preferences.LoadSelection().Should().Be(new SpeechSelection(SpeechProviderIds.Windows, "private voice"));
        preferences.SaveSelection(SpeechSelection.Default);
        preferences.LoadSelection().Should().Be(SpeechSelection.Default);
    }

    [Fact]
    public void SaveVoiceId_atomically_replaces_and_loads_the_preference()
    {
        var preferences = CreatePreferences();

        preferences.SaveVoiceId("first");
        preferences.SaveVoiceId("second");

        preferences.LoadVoiceId().Should().Be("second");
        File.Exists(Path.Combine(root, "Preferences", "speech-voice.tmp")).Should().BeFalse();
    }

    [Fact]
    public void SaveProviderId_atomically_replaces_and_loads_the_preference()
    {
        var preferences = CreatePreferences();

        preferences.SaveProviderId("windows");
        preferences.SaveProviderId("kokoro");

        preferences.LoadProviderId().Should().Be("kokoro");
        File.Exists(Path.Combine(root, "Preferences", "speech-provider.tmp")).Should().BeFalse();
    }

    [Fact]
    public void LoadVoiceId_rejects_an_empty_preference()
    {
        var preferences = CreatePreferences();
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "speech-voice.txt"), " ");

        var action = preferences.LoadVoiceId;

        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void LoadProviderId_rejects_an_empty_preference()
    {
        var preferences = CreatePreferences();
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "speech-provider.txt"), " ");

        var action = preferences.LoadProviderId;

        action.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void SaveVoiceId_rejects_a_blank_identifier(string voiceId)
    {
        var preferences = CreatePreferences();

        var action = () => preferences.SaveVoiceId(voiceId);

        action.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void SaveProviderId_rejects_a_blank_identifier(string providerId)
    {
        var preferences = CreatePreferences();

        var action = () => preferences.SaveProviderId(providerId);

        action.Should().Throw<ArgumentException>();
    }

    public new void Dispose()
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        finally { base.Dispose(); }
    }

    private LocalTextToSpeechPreferences CreatePreferences() =>
        new(
            new TestPaths(root, Path.Combine(root, "Roaming")),
            NullLogger<LocalTextToSpeechPreferences>.Instance);

    private sealed record TestPaths(string LocalRoot, string RoamingRoot) : IApplicationDataPaths;
}
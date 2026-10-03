using System.Globalization;

using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Voice;

namespace Kora.Core.UnitTests;

public sealed class ValueObjectTests
{
    [Fact]
    public void CommandDefinition_has_value_semantics_and_deconstructs()
    {
        var first = new CommandDefinition(
            BuiltInAction.ShowHelp,
            "help",
            "Show help.",
            ["what can you do"]);
        var second = new CommandDefinition(
            BuiltInAction.ShowHelp,
            "help",
            "Show help.",
            first.Aliases);

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
        first.ToString().Should().Contain(nameof(BuiltInAction.ShowHelp));
        var (action, phrase, description, aliases) = first;
        action.Should().Be(BuiltInAction.ShowHelp);
        phrase.Should().Be("help");
        description.Should().Be("Show help.");
        aliases.Should().Equal("what can you do");
    }

    [Fact]
    public void DependencyStatus_has_value_semantics_and_deconstructs()
    {
        var first = new DependencyStatus("voice", "Voice", DependencyReadiness.Ready, "ready");
        var second = new DependencyStatus("voice", "Voice", DependencyReadiness.Ready, "ready");

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
        first.ToString().Should().Contain("voice");
        var (id, name, readiness, detail) = first;
        id.Should().Be("voice");
        name.Should().Be("Voice");
        readiness.Should().Be(DependencyReadiness.Ready);
        detail.Should().Be("ready");
    }

    [Theory]
    [InlineData(DependencyReadiness.Ready, "Ready")]
    [InlineData(DependencyReadiness.Missing, "Missing")]
    [InlineData(DependencyReadiness.NeedsConfiguration, "Needs configuration")]
    [InlineData(DependencyReadiness.Incompatible, "Incompatible")]
    [InlineData(DependencyReadiness.Blocked, "Blocked")]
    [InlineData(DependencyReadiness.Failed, "Failed")]
    public void DependencyStatus_formats_readiness_for_display(
        DependencyReadiness readiness,
        string expected)
    {
        var status = new DependencyStatus("voice", "Voice", readiness, "detail");

        status.DisplayReadiness.Should().Be(expected);
    }

    [Fact]
    public void DependencyStatus_rejects_an_unsupported_readiness_value()
    {
        var status = new DependencyStatus("voice", "Voice", (DependencyReadiness)99, "detail");

        var action = () => status.DisplayReadiness;

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Unsupported dependency readiness value*");
    }

    [Fact]
    public void ApplicationLogFile_has_value_semantics_and_deconstructs()
    {
        var lastWriteTime = new DateTimeOffset(2026, 10, 3, 6, 0, 0, TimeSpan.Zero);
        var first = new ApplicationLogFile(
            "kora-20261003.log",
            new DateOnly(2026, 10, 3),
            42,
            lastWriteTime);
        var second = new ApplicationLogFile(
            "kora-20261003.log",
            new DateOnly(2026, 10, 3),
            42,
            lastWriteTime);

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
        first.ToString().Should().Contain("kora-20261003.log");
        var (fileName, date, length, modified) = first;
        fileName.Should().Be("kora-20261003.log");
        date.Should().Be(new DateOnly(2026, 10, 3));
        length.Should().Be(42);
        modified.Should().Be(lastWriteTime);
    }

    [Fact]
    public void Voice_value_objects_expose_supplied_values()
    {
        var microphone = new MicrophoneDevice("7", "Headset");
        var outputDevice = new AudioOutputDevice("endpoint", "Speakers", IsMuted: true);
        var voice = new SpeechVoice("voice-id", "Kora voice", "en-AU", SpeechVoiceGender.Female);
        var transcript = new VoiceTranscriptEventArgs("help", 0.8f);

        microphone.Id.Should().Be("7");
        microphone.Name.Should().Be("Headset");
        microphone.IsSystemDefault.Should().BeFalse();
        outputDevice.Id.Should().Be("endpoint");
        outputDevice.Name.Should().Be("Speakers");
        outputDevice.IsMuted.Should().BeTrue();
        outputDevice.IsSystemDefault.Should().BeFalse();
        SystemAudioDevices.Microphone.Name.Should().Be("System");
        SystemAudioDevices.Microphone.IsSystemDefault.Should().BeTrue();
        SystemAudioDevices.Output.Name.Should().Be("System");
        SystemAudioDevices.Output.IsSystemDefault.Should().BeTrue();
        voice.Id.Should().Be("voice-id");
        voice.Name.Should().Be("Kora voice");
        voice.Culture.Should().Be("en-AU");
        voice.Gender.Should().Be(SpeechVoiceGender.Female);
        voice.ProviderId.Should().Be(SpeechProviderIds.Windows);
        transcript.Transcript.Should().Be("help");
        transcript.Confidence.Should().Be(0.8f);
    }

    [Fact]
    public void Speech_provider_has_value_semantics_and_deconstructs()
    {
        var provider = new SpeechProvider(
            SpeechProviderIds.Kokoro,
            "Kokoro",
            "Local neural speech.",
            IsInstalled: true,
            IsBuiltIn: false,
            DownloadSizeBytes: 42,
            DefaultVoiceId: "af_heart");

        var copy = provider with { };
        var (id, name, description, installed, builtIn, size, defaultVoice) = provider;

        copy.Should().Be(provider);
        id.Should().Be(SpeechProviderIds.Kokoro);
        name.Should().Be("Kokoro");
        description.Should().Be("Local neural speech.");
        installed.Should().BeTrue();
        builtIn.Should().BeFalse();
        size.Should().Be(42);
        defaultVoice.Should().Be("af_heart");
    }

    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(50, 100, 50)]
    [InlineData(200, 100, 100)]
    [InlineData(1, 0, 0)]
    public void Speech_provider_progress_calculates_a_bounded_percentage(
        long received,
        long total,
        int expected)
    {
        var progress = new SpeechProviderInstallProgress(
            SpeechProviderInstallStage.Downloading,
            received,
            total);

        progress.Percentage.Should().Be(expected);
    }

    [Fact]
    public void AudioOutputDeviceUnavailableException_preserves_details()
    {
        var inner = new InvalidOperationException("device failure");

        var basic = new AudioOutputDeviceUnavailableException("unavailable");
        var wrapped = new AudioOutputDeviceUnavailableException("failed", inner);
        var muted = new AudioOutputDeviceUnavailableException(
            AudioOutputFailureReason.Muted,
            "muted");
        var playback = new AudioOutputDeviceUnavailableException(
            AudioOutputFailureReason.PlaybackFailed,
            "playback",
            inner);

        basic.Message.Should().Be("unavailable");
        basic.Reason.Should().Be(AudioOutputFailureReason.Unavailable);
        wrapped.Message.Should().Be("failed");
        wrapped.InnerException.Should().BeSameAs(inner);
        wrapped.Reason.Should().Be(AudioOutputFailureReason.Unavailable);
        muted.Reason.Should().Be(AudioOutputFailureReason.Muted);
        playback.Reason.Should().Be(AudioOutputFailureReason.PlaybackFailed);
        playback.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void SpeechVoiceSelector_prefers_an_exact_locale_female_voice()
    {
        SpeechVoice[] voices =
        [
            new("family", "Family", "en-GB", SpeechVoiceGender.Female),
            new("exact", "Exact", "en-AU", SpeechVoiceGender.Female),
            new("male", "Male", "en-AU", SpeechVoiceGender.Male),
        ];

        var selected = SpeechVoiceSelector.SelectDefault(
            voices,
            CultureInfo.GetCultureInfo("en-AU"));

        selected?.Id.Should().Be("exact");
    }

    [Theory]
    [InlineData("es", "neutral")]
    [InlineData("es-MX", "regional")]
    public void SpeechVoiceSelector_falls_back_to_a_same_language_female_voice(
        string voiceCulture,
        string voiceId)
    {
        SpeechVoice[] voices =
        [
            new(voiceId, "Spanish", voiceCulture, SpeechVoiceGender.Female),
        ];

        var selected = SpeechVoiceSelector.SelectDefault(
            voices,
            CultureInfo.GetCultureInfo("es-ES"));

        selected?.Id.Should().Be(voiceId);
    }

    [Fact]
    public void SpeechVoiceSelector_prefers_a_same_language_female_voice_over_an_exact_locale_male_voice()
    {
        SpeechVoice[] voices =
        [
            new("female", "Female", "en-GB", SpeechVoiceGender.Female),
            new("male", "Male", "en-AU", SpeechVoiceGender.Male),
        ];

        var selected = SpeechVoiceSelector.SelectDefault(
            voices,
            CultureInfo.GetCultureInfo("en-AU"));

        selected?.Id.Should().Be("female");
    }

    [Fact]
    public void SpeechVoiceSelector_uses_a_compatible_male_voice_when_no_female_voice_is_available()
    {
        SpeechVoice[] voices =
        [
            new("male", "Male", "en-AU", SpeechVoiceGender.Male),
            new("female", "Female", "fr-FR", SpeechVoiceGender.Female),
        ];

        var selected = SpeechVoiceSelector.SelectDefault(
            voices,
            CultureInfo.GetCultureInfo("en-AU"));

        selected?.Id.Should().Be("male");
    }

    [Theory]
    [InlineData(SpeechVoiceGender.Neutral)]
    [InlineData(SpeechVoiceGender.Unknown)]
    public void SpeechVoiceSelector_uses_another_compatible_voice_when_preferred_genders_are_unavailable(
        SpeechVoiceGender gender)
    {
        SpeechVoice[] voices =
        [
            new("fallback", "Fallback", "en-GB", gender),
        ];

        var selected = SpeechVoiceSelector.SelectDefault(
            voices,
            CultureInfo.GetCultureInfo("en-AU"));

        selected?.Id.Should().Be("fallback");
    }

    [Fact]
    public void SpeechVoiceSelector_does_not_choose_an_unrelated_language()
    {
        SpeechVoice[] voices =
        [
            new("female", "Female", "fr-FR", SpeechVoiceGender.Female),
            new("male", "Male", "de-DE", SpeechVoiceGender.Male),
        ];

        var selected = SpeechVoiceSelector.SelectDefault(
            voices,
            CultureInfo.GetCultureInfo("en-AU"));

        selected.Should().BeNull();
    }

    [Fact]
    public void SpeechVoiceSelector_rejects_null_arguments()
    {
        var culture = CultureInfo.GetCultureInfo("en-AU");

        var nullVoices = () => SpeechVoiceSelector.SelectDefault(null!, culture);
        var nullCulture = () => SpeechVoiceSelector.SelectDefault([], null!);

        nullVoices.Should().Throw<ArgumentNullException>();
        nullCulture.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(ResponseOutputMode.Hybrid, null, null, ResponseOutputMode.Hybrid)]
    [InlineData(ResponseOutputMode.Hybrid, ResponseOutputMode.VisualOnly, null, ResponseOutputMode.VisualOnly)]
    [InlineData(ResponseOutputMode.Hybrid, ResponseOutputMode.VisualOnly, ResponseOutputMode.VoiceOnly, ResponseOutputMode.VoiceOnly)]
    public void ResponseOutputModeResolver_applies_task_queue_default_precedence(
        ResponseOutputMode defaultMode,
        ResponseOutputMode? queueMode,
        ResponseOutputMode? taskMode,
        ResponseOutputMode expected)
    {
        var result = ResponseOutputModeResolver.Resolve(defaultMode, queueMode, taskMode);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(100, null, null)]
    [InlineData(0, 100, null)]
    [InlineData(0, null, 100)]
    public void ResponseOutputModeResolver_rejects_invalid_modes(
        int defaultMode,
        int? queueMode,
        int? taskMode)
    {
        var action = () => ResponseOutputModeResolver.Resolve(
            (ResponseOutputMode)defaultMode,
            (ResponseOutputMode?)queueMode,
            (ResponseOutputMode?)taskMode);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CallAwareSettings_defaults_to_visual_text_and_voice_activation()
    {
        var settings = CallAwareSettings.Default;
        var equivalent = new CallAwareSettings(
            ShowVisualTextDuringCalls: true,
            AllowVoiceActivationDuringCalls: true);

        settings.Should().Be(equivalent);
        settings.GetHashCode().Should().Be(equivalent.GetHashCode());
        settings.ToString().Should().Contain(nameof(CallAwareSettings.ShowVisualTextDuringCalls));
        var (showVisualText, allowVoiceActivation) = settings;
        showVisualText.Should().BeTrue();
        allowVoiceActivation.Should().BeTrue();
    }

    [Theory]
    [InlineData(CallState.Unavailable)]
    [InlineData(CallState.Clear)]
    [InlineData(CallState.Active)]
    [InlineData(CallState.Suspected)]
    [InlineData(CallState.Unknown)]
    public void CallStateChangedEventArgs_preserves_valid_states(CallState state)
    {
        var eventArgs = new CallStateChangedEventArgs(state);

        eventArgs.State.Should().Be(state);
    }

    [Fact]
    public void CallStateChangedEventArgs_rejects_an_invalid_state()
    {
        var action = () => new CallStateChangedEventArgs((CallState)100);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}
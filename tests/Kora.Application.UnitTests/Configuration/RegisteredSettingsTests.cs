using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Configuration.RegisteredSettings;
using Kora.Core.Configuration;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Configuration;

public sealed class RegisteredSettingsTests
{
    [Fact]
    public void ClosedRegistryInspectionAndPolicyDeclareOnlyTheExistingAdmittedSubset()
    {
        RegisteredSettingRegistry.Settings.Select(item => item.Setting).Should().Equal(Enum.GetValues<RegisteredSetting>());
        RegisteredSettingRegistry.Settings.Select(item => item.Id).Should().OnlyHaveUniqueItems().And.HaveCount(15);
        foreach (var descriptor in RegisteredSettingRegistry.Settings)
        {
            RegisteredSettingRegistry.Get(descriptor.Setting).Should().BeSameAs(descriptor);
            descriptor.Scope.Should().Be("shared device-local");
            RegisteredSettingSuggestions.ModelPolicy.Should().Contain(descriptor.Id).And.Contain(descriptor.Description);
            descriptor.SuggestedValues.Should().NotBeNullOrWhiteSpace();
            RegisteredSettingSuggestions.ModelPolicy.Should().Contain(descriptor.SuggestedValues);
            descriptor.RequiresNativeReview.Should().Be(descriptor.Id.StartsWith("appearance.", StringComparison.Ordinal));
            if (descriptor.RequiresNativeReview)
            {
                var appearance = AppearanceOptionRegistry.Options.Single(item => string.Equals(item.Id, descriptor.Id, StringComparison.Ordinal));
                descriptor.Description.Should().Be(appearance.Description);
                descriptor.Units.Should().Be(appearance.Units);
                descriptor.Minimum.Should().Be(appearance.Minimum);
                descriptor.Maximum.Should().Be(appearance.Maximum);
                if (appearance.Type == AppearanceValueType.Toggle)
                { descriptor.SuggestedValues.Should().Be("true/false"); }
            }
        }
        RegisteredSettingRegistry.Get(RegisteredSetting.ReaderTextScale).Minimum.Should().Be(ReaderTextScaleSettings.MinimumPercent);
        RegisteredSettingRegistry.Get(RegisteredSetting.ReaderTextScale).Maximum.Should().Be(ReaderTextScaleSettings.MaximumPercent);
        RegisteredSettingSuggestions.ModelPolicy.Should().Contain("never write authority").And.Contain("separately confirm or cancel")
            .And.Contain("companion cap").And.Contain("desired/effective").And.Contain("zero blocks future synthesis")
            .And.Contain("native/authentication exceptions");
    }

    [Theory]
    [InlineData("set appearance.theme to system", RegisteredSetting.Theme)]
    [InlineData("set appearance.theme to light", RegisteredSetting.Theme)]
    [InlineData("set appearance.theme to dark", RegisteredSetting.Theme)]
    [InlineData("set appearance.reader-text-scale to 75", RegisteredSetting.ReaderTextScale)]
    [InlineData("set appearance.reader-text-scale to 200", RegisteredSetting.ReaderTextScale)]
    [InlineData("set appearance.presence-display to false", RegisteredSetting.PresenceDisplay)]
    [InlineData("set appearance.presence-display to true", RegisteredSetting.PresenceDisplay)]
    [InlineData("set appearance.presence-timeout to 1", RegisteredSetting.PresenceTimeout)]
    [InlineData("set appearance.presence-timeout to 60", RegisteredSetting.PresenceTimeout)]
    [InlineData("set appearance.presence-size to 240", RegisteredSetting.PresenceSize)]
    [InlineData("set appearance.presence-size to 600", RegisteredSetting.PresenceSize)]
    [InlineData("set appearance.dot-size to 50", RegisteredSetting.DotSize)]
    [InlineData("set appearance.dot-size to 200", RegisteredSetting.DotSize)]
    [InlineData("set appearance.dot-density to 25", RegisteredSetting.DotDensity)]
    [InlineData("set appearance.dot-density to 200", RegisteredSetting.DotDensity)]
    [InlineData("set appearance.movement-speed to 25", RegisteredSetting.MovementSpeed)]
    [InlineData("set appearance.movement-speed to 200", RegisteredSetting.MovementSpeed)]
    [InlineData("set appearance.speech-scaling to false", RegisteredSetting.SpeechScaling)]
    [InlineData("set appearance.speech-scaling to true", RegisteredSetting.SpeechScaling)]
    [InlineData("set appearance.speech-scale-amount to 0", RegisteredSetting.SpeechScaleAmount)]
    [InlineData("set appearance.speech-scale-amount to 200", RegisteredSetting.SpeechScaleAmount)]
    [InlineData("set speech.summary-words to 1", RegisteredSetting.SummaryWords)]
    [InlineData(" SET speech.summary-words to 80 ", RegisteredSetting.SummaryWords)]
    [InlineData("set speech.summary-sentences to 1", RegisteredSetting.SummarySentences)]
    [InlineData("set speech.summary-sentences to 3", RegisteredSetting.SummarySentences)]
    [InlineData("set speech.playback-volume to 0", RegisteredSetting.PlaybackVolume)]
    [InlineData("set speech.playback-volume to 100", RegisteredSetting.PlaybackVolume)]
    [InlineData("set responses.default-mode to Hybrid", RegisteredSetting.ResponseMode)]
    [InlineData("set responses.default-mode to VoiceOnly", RegisteredSetting.ResponseMode)]
    [InlineData("set responses.default-mode to VisualOnly", RegisteredSetting.ResponseMode)]
    [InlineData("set speech.windows-rate to -10", RegisteredSetting.WindowsSpeechRate)]
    [InlineData("set speech.windows-rate to -1", RegisteredSetting.WindowsSpeechRate)]
    [InlineData("set speech.windows-rate to 0", RegisteredSetting.WindowsSpeechRate)]
    [InlineData("set speech.windows-rate to 10", RegisteredSetting.WindowsSpeechRate)]
    public void SharedParserReturnsTypedInertProposalAndExactDescriptor(string text, RegisteredSetting setting)
    {
        var proposal = RegisteredSettingSuggestions.Parse(text);
        proposal.Setting.Should().Be(setting);
        proposal.Descriptor.Should().BeSameAs(RegisteredSettingRegistry.Get(setting));
        switch (proposal)
        {
            case RegisteredSettingProposal.Appearance appearance:
                AppearanceOptionRegistry.Validate(appearance.Option, appearance.Value);
                break;
            case RegisteredSettingProposal.Summary summary:
                SpokenSummaryLimits.Default.With(summary.Option, summary.Value).Validate();
                break;
            case RegisteredSettingProposal.Volume volume:
                volume.Value.Percent.Should().BeInRange(0, 100);
                break;
            case RegisteredSettingProposal.ResponseMode mode:
                Enum.IsDefined(mode.Value).Should().BeTrue();
                mode.Descriptor.SuggestedValues.Should().Be("Hybrid/VoiceOnly/VisualOnly");
                mode.Descriptor.Id.Should().Be(ResponseModeCommand.OptionId);
                break;
            case RegisteredSettingProposal.WindowsRate rate:
                rate.Value.Value.Should().BeInRange(WindowsSpeechRate.Minimum, WindowsSpeechRate.Maximum);
                rate.Descriptor.Units.Should().Be("Windows native units");
                break;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("set appearance.response-timeout to 10")]
    [InlineData("set appearance.native-presence-renderer to true")]
    [InlineData("set appearance.theme to ultraviolet")]
    [InlineData("set appearance.reader-text-scale to 999")]
    [InlineData("reset appearance.theme")]
    [InlineData("set speech.provider to windows-sapi")]
    [InlineData("get speech.summary-words")]
    [InlineData("set speech.summary-words to 0")]
    [InlineData("set speech.summary-words to 81")]
    [InlineData("set speech.summary-sentences to 4")]
    [InlineData("set speech.summary-words to 040")]
    [InlineData("set speech.summary-words to +40")]
    [InlineData("Kora, set speech.summary-words to 40")]
    [InlineData("set spoken summary word limit to 40")]
    [InlineData("set speech.summary-words to 40\nset speech.summary-sentences to 2")]
    [InlineData("set speech.playback-volume to 01")]
    [InlineData("set speech.playback-volume to 101")]
    [InlineData("set speech.playback-volume to -1")]
    [InlineData(" SET speech.playback-volume to 30 ")]
    [InlineData("set speech.playback-volume to 30 and confirm")]
    [InlineData("I changed your settings.")]
    [InlineData("set responses.default-mode to 1")]
    [InlineData("set responses.default-mode to 999")]
    [InlineData("set responses.default-mode to voiceonly")]
    [InlineData("set responses.default-mode to VoiceOnly ")]
    [InlineData("SET responses.default-mode to VoiceOnly")]
    [InlineData("set responses.default-mode to Hybrid, VoiceOnly")]
    [InlineData("Kora, set responses.default-mode to VoiceOnly")]
    [InlineData("set responses.default-mode to VoiceOnly\nconfirm")]
    [InlineData("set responses.session-mode to VoiceOnly")]
    [InlineData("set responses.default-mode to VoiceOnly\nset speech.playback-volume to 30")]
    [InlineData("reset responses.default-mode")]
    [InlineData("set speech.windows-rate to -11")]
    [InlineData("set speech.windows-rate to 11")]
    [InlineData("set speech.windows-rate to +1")]
    [InlineData("set speech.windows-rate to -0")]
    [InlineData("set speech.windows-rate to -01")]
    [InlineData("set speech.windows-rate to 01")]
    [InlineData("set speech.windows-rate to 1.0")]
    [InlineData("set speech.windows-rate to 1 percent")]
    [InlineData(" set speech.windows-rate to -1")]
    [InlineData("SET speech.windows-rate to -1")]
    [InlineData("set speech.windows-rate to -1 ")]
    [InlineData("set speech.windows-rate to -1\nconfirm rate change")]
    [InlineData("set speech.windows-rate to -1\nset speech.playback-volume to 30")]
    [InlineData("reset speech.windows-rate")]
    [InlineData("speak slower")]
    public void SharedParserRejectsUnregisteredMixedOrNoncanonicalSuggestionData(string text)
    {
        FluentActions.Invoking(() => RegisteredSettingSuggestions.Parse(text)).Should().Throw<Exception>();
    }

    [Fact]
    public void SharedParserRejectsMissingOrOversizeSuggestionBeforeInterpretation()
    {
        FluentActions.Invoking(() => RegisteredSettingSuggestions.Parse(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => RegisteredSettingSuggestions.Parse(new string('x', 257))).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => RegisteredSettingRegistry.Get((RegisteredSetting)999)).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("set appearance.presence-display to 0")]
    [InlineData("set appearance.presence-display to on")]
    [InlineData("set appearance.presence-display to False")]
    [InlineData("set appearance.speech-scaling to 1")]
    [InlineData("set appearance.speech-scaling to system")]
    [InlineData("set appearance.presence-timeout to 0")]
    [InlineData("set appearance.presence-timeout to 61")]
    [InlineData("set appearance.presence-size to 239")]
    [InlineData("set appearance.presence-size to 601")]
    [InlineData("set appearance.dot-size to 49")]
    [InlineData("set appearance.dot-size to 201")]
    [InlineData("set appearance.dot-density to 24")]
    [InlineData("set appearance.dot-density to 201")]
    [InlineData("set appearance.movement-speed to 24")]
    [InlineData("set appearance.movement-speed to 201")]
    [InlineData("set appearance.speech-scale-amount to -1")]
    [InlineData("set appearance.speech-scale-amount to 201")]
    [InlineData("set appearance.movement-speed to 050")]
    [InlineData("set appearance.movement-speed to +50")]
    [InlineData("set appearance.movement-speed to 50.0")]
    [InlineData("set appearance.movement-speed to false")]
    [InlineData("set appearance.movement-speed to 50 percent")]
    [InlineData("set appearance.movement-speed to 50 ")]
    [InlineData(" set appearance.movement-speed to 50")]
    [InlineData("SET appearance.movement-speed to 50")]
    [InlineData("set appearance.MOVEMENT-SPEED to 50")]
    [InlineData("Kora, set appearance.movement-speed to 50")]
    [InlineData("set appearance movement speed to 50")]
    [InlineData("set appearance.unknown to 50")]
    [InlineData("set appearance.movement-speed to 50\nset appearance.dot-size to 50")]
    [InlineData("set appearance.movement-speed to 50 and confirm")]
    [InlineData("I suggest: set appearance.movement-speed to 50")]
    public void PresenceParserRejectsInvalidOrNoncanonicalDataWithTypedFailure(string text)
    {
        FluentActions.Invoking(() => RegisteredSettingSuggestions.Parse(text)).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("review response mode suggestion", RegisteredSettingSuggestionOperation.Review)]
    [InlineData("Nova, confirm response mode change", RegisteredSettingSuggestionOperation.Confirm)]
    [InlineData("Nova cancel response mode review", RegisteredSettingSuggestionOperation.Cancel)]
    [InlineData("Nova\t REVIEW RESPONSE MODE SUGGESTION", RegisteredSettingSuggestionOperation.Review)]
    [InlineData("Novax review response mode suggestion", null)]
    [InlineData("review response mode suggestion now", null)]
    [InlineData("yes", null)]
    [InlineData("Nova", null)]
    public void ResponseModeSuggestionCommandsAreExactAndCurrentNameAware(string input, RegisteredSettingSuggestionOperation? expected)
    {
        RegisteredSettingSuggestionCommand.Parse(input, "Nova").Should().Be(expected);
        RegisteredSettingSuggestions.ModelPolicy.Should().Contain("including VoiceOnly").And.Contain("future eligible output only");
        RegisteredSettingRegistry.Get(RegisteredSetting.ResponseMode).SuggestedValues.Should()
            .Be(string.Join("/", Enum.GetValues<ResponseOutputMode>()));
    }

    [Theory]
    [InlineData("review rate suggestion", RegisteredSettingSuggestionOperation.Review)]
    [InlineData("Nova, confirm rate change", RegisteredSettingSuggestionOperation.Confirm)]
    [InlineData("Nova cancel rate review", RegisteredSettingSuggestionOperation.Cancel)]
    [InlineData("Nova\t REVIEW RATE SUGGESTION", RegisteredSettingSuggestionOperation.Review)]
    [InlineData("Novax review rate suggestion", null)]
    [InlineData("review rate suggestion now", null)]
    [InlineData("yes", null)]
    [InlineData("Nova", null)]
    public void WindowsRateReviewCommandsAreExactAndCurrentNameAware(string input, RegisteredSettingSuggestionOperation? expected) =>
        WindowsSpeechRateSuggestionCommand.Parse(input, "Nova").Should().Be(expected);
}

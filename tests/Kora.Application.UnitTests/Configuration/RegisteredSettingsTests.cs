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
        RegisteredSettingRegistry.Settings.Select(item => item.Id).Should().OnlyHaveUniqueItems().And.HaveCount(6);
        foreach (var descriptor in RegisteredSettingRegistry.Settings)
        {
            RegisteredSettingRegistry.Get(descriptor.Setting).Should().BeSameAs(descriptor);
            descriptor.Scope.Should().Be("shared device-local");
            RegisteredSettingSuggestions.ModelPolicy.Should().Contain(descriptor.Id).And.Contain(descriptor.Description);
            descriptor.SuggestedValues.Should().NotBeNullOrWhiteSpace();
            RegisteredSettingSuggestions.ModelPolicy.Should().Contain(descriptor.SuggestedValues);
            descriptor.RequiresNativeReview.Should().Be(descriptor.Setting is RegisteredSetting.Theme or RegisteredSetting.ReaderTextScale);
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
    [InlineData("set speech.summary-words to 1", RegisteredSetting.SummaryWords)]
    [InlineData(" SET speech.summary-words to 80 ", RegisteredSetting.SummaryWords)]
    [InlineData("set speech.summary-sentences to 1", RegisteredSetting.SummarySentences)]
    [InlineData("set speech.summary-sentences to 3", RegisteredSetting.SummarySentences)]
    [InlineData("set speech.playback-volume to 0", RegisteredSetting.PlaybackVolume)]
    [InlineData("set speech.playback-volume to 100", RegisteredSetting.PlaybackVolume)]
    [InlineData("set responses.default-mode to Hybrid", RegisteredSetting.ResponseMode)]
    [InlineData("set responses.default-mode to VoiceOnly", RegisteredSetting.ResponseMode)]
    [InlineData("set responses.default-mode to VisualOnly", RegisteredSetting.ResponseMode)]
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
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("set appearance.presence-timeout to 10")]
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
}

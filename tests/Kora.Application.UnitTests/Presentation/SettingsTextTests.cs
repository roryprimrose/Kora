using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Presentation;
using Kora.Core.Communication;
using Kora.Core.Authorization;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Presentation;

public sealed class SettingsTextTests
{
    [Fact]
    public void Unknown_choices_fail_explicitly_instead_of_acquiring_a_display_default()
    {
        Action[] actions =
        [
            () => SettingsText.ResponseMode((ResponseOutputMode)int.MaxValue),
            () => SettingsText.ProviderMode((ModelProviderMode)int.MaxValue),
            () => SettingsText.CallFeedback((InCallFeedbackMode)int.MaxValue),
            () => SettingsText.Service((ProviderService)int.MaxValue),
            () => SettingsText.CaptionPlacement((SpeechCaptionPlacement)int.MaxValue),
        ];
        foreach (var action in actions) { action.Should().Throw<ArgumentOutOfRangeException>(); }
    }

    [Fact]
    public void Missing_effective_choices_remain_unavailable_even_when_availability_is_reported()
    {
        string[] summaries =
        [
            SettingsText.Describe(new ResponseModeCommandResult("observed", null, 1, 1,
                null, null, null, "unavailable", true)),
            SettingsText.Describe(new ProviderModeCommandResult("observed", null, 1, 1,
                null, null, "unavailable", true)),
            SettingsText.Describe(new InCallFeedbackCommandResult("observed", null, 1, 1,
                null, null, "unavailable", true)),
            SettingsText.Describe(new SpeechTextState("observed", 1, null, null, "unavailable", true, null)),
        ];
        summaries.Should().AllSatisfy(summary => summary.Should().StartWith("Setting unavailable."));
        SettingsText.Describe(new ProviderProfileConfigurationState(1, null, null, null,
            "Unavailable", null, LocalProviderInstallation.Unknown, true))
            .Should().Contain("Not selected").And.Contain("No model is ready");
    }

    [Fact]
    public void Available_modes_keep_their_selection_source_and_application_status()
    {
        SettingsText.Describe(new ResponseModeCommandResult("observed", null, 1, 1,
            ResponseOutputMode.Hybrid, ResponseOutputMode.Hybrid, ResponseOutputMode.Hybrid, "saved", true))
            .Should().Be("Text and speech. Saved on this device.");
        SettingsText.Describe(new ProviderModeCommandResult("observed", null, 1, 1,
            null, ModelProviderMode.LocalOnly, "default", true))
            .Should().Be("On-device only for new sessions. Using the default.");
        SettingsText.Describe(new InCallFeedbackCommandResult("observed", null, 1, 1,
            null, InCallFeedbackMode.UI, "observed", true) { Applied = false, MandatoryVisual = false })
            .Should().Be("Text only. Current setting on this device. Not currently applied.");
    }

    [Fact]
    public void Caption_option_labels_and_provenance_do_not_hide_invalid_choices()
    {
        var session = new WorkSessionAuthorization(default, default, false);
        var delay = new SpeechTextChoice(null, 1, Guid.NewGuid(), session,
            Kora.Core.Hosting.RequestOrigin.LocalUi, static () => false, new SpeechCaptionValue.Delay(5));
        delay.Label.Should().Be(new SpeechCaptionValue.Delay(5).Label);
        var invalid = new SpeechTextChoice(null, 1, Guid.NewGuid(), session,
            Kora.Core.Hosting.RequestOrigin.LocalUi, static () => false);
        var readLabel = () => invalid.Label;
        readLabel.Should().Throw<InvalidOperationException>();
        var state = new SpeechTextState("observed", 1, null, SpeechTextMode.Off, "saved", true, null)
        {
            SavedCaptionOptions = SpeechCaptionOptions.Default,
        };
        state.CaptionOptionsSource.Should().Be("saved");
        (state with { Available = false }).CaptionOptionsSource.Should().Be("unavailable");
        (state with { SavedCaptionOptions = null }).CaptionOptionsSource.Should().Be("default");
    }

    [Theory]
    [InlineData(ResponseOutputMode.VisualOnly, "Text only")]
    [InlineData(ResponseOutputMode.VoiceOnly, "Speech only")]
    [InlineData(ResponseOutputMode.Hybrid, "Text and speech")]
    public void Response_modes_have_plain_language_labels(ResponseOutputMode mode, string expected) =>
        SettingsText.ResponseMode(mode).Should().Be(expected);

    [Theory]
    [InlineData(ModelProviderMode.Unknown, "Unavailable")]
    [InlineData(ModelProviderMode.LocalOnly, "On-device only")]
    [InlineData(ModelProviderMode.LocalFirst, "Prefer on-device")]
    [InlineData(ModelProviderMode.HostedPreferred, "Prefer online")]
    public void Provider_modes_describe_where_processing_happens(ModelProviderMode mode, string expected) =>
        SettingsText.ProviderMode(mode).Should().Be(expected);

    [Theory]
    [InlineData(InCallFeedbackMode.UI, "Text only")]
    [InlineData(InCallFeedbackMode.Voice, "Speech only")]
    [InlineData(InCallFeedbackMode.Both, "Text and speech")]
    [InlineData(InCallFeedbackMode.Inherit, "Use normal response settings")]
    public void Call_modes_explain_inheritance_without_internal_enum_names(InCallFeedbackMode mode, string expected) =>
        SettingsText.CallFeedback(mode).Should().Be(expected);

    [Theory]
    [InlineData(ProviderService.Unset, "Not selected")]
    [InlineData(ProviderService.Local, "On-device (Ollama)")]
    [InlineData(ProviderService.Copilot, "GitHub Copilot")]
    [InlineData(ProviderService.Claude, "Claude")]
    [InlineData(ProviderService.Codex, "Codex")]
    public void Services_have_recognizable_names(ProviderService service, string expected) =>
        SettingsText.Service(service).Should().Be(expected);

    [Theory]
    [InlineData(SpeechCaptionPlacement.BottomRight, "Bottom right")]
    [InlineData(SpeechCaptionPlacement.BottomLeft, "Bottom left")]
    [InlineData(SpeechCaptionPlacement.TopRight, "Top right")]
    [InlineData(SpeechCaptionPlacement.TopLeft, "Top left")]
    public void Caption_positions_have_readable_labels(SpeechCaptionPlacement placement, string expected) =>
        SettingsText.CaptionPlacement(placement).Should().Be(expected);

    [Fact]
    public void Log_storage_summaries_distinguish_defaults_and_saved_values_without_database_details()
    {
        var diagnostic = new DiagnosticRetentionState(new(30), new(30), "default", 4, null);
        var audit = new AuditRetentionState(new(120), new(120), "saved", 7, null);

        SettingsText.Describe(diagnostic).Should().Be("30 days for new diagnostic records. Using the default.");
        SettingsText.Describe(audit).Should().Be("120 days for new security records. Saved on this device.");
        DiagnosticRetentionState.Serialize(diagnostic, 2, "observed").Should().Contain("\"revision\":4");
        AuditRetentionState.Serialize(audit, 2, "observed").Should().Contain("\"revision\":7");
    }

    [Fact]
    public void Unavailable_settings_preserve_recovery_without_presenting_desired_values_as_active()
    {
        const string recovery = "The write is unconfirmed. Inspect saved state before repair.";
        string[] descriptions =
        [
            SettingsText.Describe(new DiagnosticRetentionState(new(7), null, "unavailable", 1, recovery)),
            SettingsText.Describe(new AuditRetentionState(new(90), null, "unavailable", 1, recovery)),
            SettingsText.Describe(new PlaybackVolumeState(new(50), null, "unavailable", 1, recovery)),
            SettingsText.Describe(new WindowsSpeechRateState(new(3), null, null, default, 0, "unavailable", 1, recovery)),
            SettingsText.Describe(new SessionQueueConfigurationState(null, null, 1, recovery)),
            SettingsText.Describe(new ResponseModeCommandResult("observed", recovery, 1, 1,
                ResponseOutputMode.Hybrid, ResponseOutputMode.Hybrid, null, "unavailable", false)),
            SettingsText.Describe(new ProviderModeCommandResult("observed", recovery, 1, 1,
                ModelProviderMode.LocalOnly, ModelProviderMode.LocalOnly, "unavailable", false)),
            SettingsText.Describe(new InCallFeedbackCommandResult("observed", recovery, 1, 1,
                InCallFeedbackMode.UI, InCallFeedbackMode.UI, "unavailable", false)),
            SettingsText.Describe(new SpeechTextState("observed", 1, SpeechTextMode.CurrentUtterance,
                null, "unavailable", false, recovery)),
            SettingsText.Describe(new ProviderProfileConfigurationState(1, null, null, null,
                "Unavailable", recovery, LocalProviderInstallation.Unknown, false)),
        ];

        descriptions.Should().AllSatisfy(text => text.Should().StartWith("Setting unavailable.")
            .And.Contain(recovery).And.NotContain("Using the default").And.NotContain("Saved on this device"));
    }

    [Fact]
    public void Zero_volume_explicitly_explains_on_screen_responses()
    {
        SettingsText.Describe(new PlaybackVolumeState(new(0), new(0), "saved", 1, null))
            .Should().Contain("0%").And.Contain("Speech is off").And.Contain("on screen");
        SettingsText.Describe(new WindowsSpeechRateState(new(3), new(3), SpeechProviderIds.Windows,
            default, 0, "saved", 1, null)).Should().Contain("Windows voice speed: 3");
    }

    [Fact]
    public void Queue_summary_keeps_all_four_limits()
    {
        var limits = new SessionQueueLimits(4, 2, 45, 10);
        SettingsText.Describe(new SessionQueueConfigurationState(new(4, 2, 45, 10), limits, 1, null))
            .Should().Contain("4 waiting").And.Contain("2 running").And.Contain("45 minutes").And.Contain("10 minutes");
    }

    [Fact]
    public void Missing_or_muted_speakers_do_not_appear_ready_even_when_the_choice_is_saved()
    {
        var missing = new OutputDeviceCommandResult("observed", "Choose an available speaker.", 1, 1,
            true, "missing", null, null, "saved", false, false);
        SettingsText.Describe(missing).Should().Contain("Speaker unavailable")
            .And.Contain("Saved on this device").And.Contain("Choose an available speaker").And.NotContain("ready");
        SettingsText.Describe(missing with { Muted = true }).Should().Contain("muted").And.Contain("on screen").And.NotContain("ready");
        SettingsText.Describe(missing with { Effective = "speaker", Available = true, Recovery = null })
            .Should().StartWith("Speaker ready.");
    }

    [Fact]
    public void Call_summary_keeps_mandatory_visual_protection_separate_from_the_selected_mode()
    {
        var state = new InCallFeedbackCommandResult("observed", null, 1, 1,
            InCallFeedbackMode.Voice, InCallFeedbackMode.Voice, "saved", true)
        {
            Applied = true,
            MandatoryVisual = true,
        };
        SettingsText.Describe(state).Should().Contain("Speech only").And.Contain("current call")
            .And.Contain("full response must remain on screen");
    }

    [Fact]
    public void Profile_summaries_preserve_choices_needed_for_review_without_serialized_state()
    {
        var tuple = new ProviderProfileTuple(ProviderService.Local, new("account"), new(1),
            new("model"), new("thinking"), new("size"));
        var preferences = new ProviderProfilePreferences(ProviderService.Local, [tuple]);
        var state = new ProviderProfileConfigurationState(1, preferences, preferences, tuple,
            "Ready", null, LocalProviderInstallation.Installed, true);

        SettingsText.Describe(state).Should().Contain("On-device (Ollama)").And.Contain("Account: account")
            .And.Contain("model: model").And.Contain("thinking level: thinking").And.Contain("conversation size: size")
            .And.NotContain("Revision").And.NotContain("tuple").And.NotContain("{");
        SettingsText.Describe(state with { Effective = null }).Should().Contain("No model is ready");
    }

    [Fact]
    public void Captions_distinguish_off_on_and_unavailable()
    {
        var state = new SpeechTextState("observed", 1, null, SpeechTextMode.Off, "default", true, null);
        SettingsText.Describe(state).Should().Be("Captions are off. Using the default.");
        SettingsText.Describe(state with { Effective = SpeechTextMode.CurrentUtterance })
            .Should().Contain("Show the words Kora speaks");
        SettingsText.Describe(state with { Available = false }).Should().Contain("unavailable").And.NotContain("Captions are off");
    }
}

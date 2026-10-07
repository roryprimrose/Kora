using AwesomeAssertions;
using Kora.Core.Configuration;
using Kora.Core.Voice;

namespace Kora.Core.UnitTests.Configuration;

public sealed class SpeechConfigurationTests
{
    [Fact]
    public void Delivered_schema_has_explicit_defaults_effects_and_reset_boundaries()
    {
        SpeechSelection.Default.Should().Be(new SpeechSelection(SpeechProviderIds.Windows, null));
        SpeechSelection.Default.Validate();
        SpeechOptionRegistry.Options.Should().HaveCount(4);
        SpeechOptionRegistry.SchemaVersion.Should().Be(2);
        new SpeechVoice("voice", "Voice", "en-US", SpeechVoiceGender.Unknown).ConfigurationId.Should().Be("windows-sapi / voice");
        foreach (var descriptor in SpeechOptionRegistry.Options)
        {
            SpeechOptionRegistry.Get(descriptor.Option).Should().BeSameAs(descriptor);
            descriptor.Id.Should().StartWith("speech.");
            descriptor.SpokenName.Should().StartWith("speech ");
            descriptor.Default.Should().NotBeEmpty();
            descriptor.ResetEffect.Should().Contain(descriptor.IsSummaryLimit ? "cap" : "default voice");
            descriptor.Type.Should().Be(descriptor.IsSummaryLimit ? "positive-integer" : "installed-choice");
            descriptor.Scope.Should().Be("device-local");
            descriptor.Effect.Should().Be("voice-output");
            descriptor.Availability.Should().Be(descriptor.IsSummaryLimit ? "local-host" : "ready-installed-assets");
            descriptor.ApplicationTiming.Should().Contain("atomic-save");
            descriptor.Confirmation.Should().Contain("original-channel");
            descriptor.AuditAction.Should().StartWith("configuration.");
        }
        var invalid = () => SpeechOptionRegistry.Get((SpeechOption)99);
        invalid.Should().Throw<InvalidOperationException>();
        new SpeechSelection(SpeechProviderIds.Kokoro, new string('v', SpeechSelection.MaximumVoiceIdLength)).Validate();
        var invalidDescriptor = () => new SpeechOptionDescriptor((SpeechOption)99, "", "", "", "").AuditAction;
        invalidDescriptor.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("windows", "voice")]
    [InlineData("", null)]
    [InlineData("kokoro", "")]
    [InlineData("kokoro", " ")]
    [InlineData("kokoro", " padded")]
    [InlineData("kokoro", "padded ")]
    [InlineData("kokoro", "new\nline")]
    public void Invalid_or_unknown_selection_is_not_reinterpreted(string provider, string? voice)
    {
        var validate = () => new SpeechSelection(provider, voice).Validate();
        validate.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Over_limit_voice_is_rejected()
    {
        var validate = () => new SpeechSelection(SpeechProviderIds.Windows,
            new string('v', SpeechSelection.MaximumVoiceIdLength + 1)).Validate();
        validate.Should().Throw<ArgumentOutOfRangeException>();
    }
}

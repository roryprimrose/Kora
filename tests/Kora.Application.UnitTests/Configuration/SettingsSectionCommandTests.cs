using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Presentation;

namespace Kora.Application.UnitTests.Configuration;

public sealed class SettingsSectionCommandTests
{
    [Theory]
    [InlineData("show setting speech.windows-rate", "speech.windows-rate")]
    [InlineData("view setting voice speed", "voice speed")]
    [InlineData("Nova Prime, open setting Reading text size", "Reading text size")]
    [InlineData("show appearance.presence.show", "appearance.presence.show")]
    [InlineData("Nova Prime, show settings for Reduced motion", "Reduced motion")]
    [InlineData("show me the settings for appearance.presence.show", "appearance.presence.show")]
    public void SearchCommandsPreserveTheQueryAsInertNavigation(string phrase, string query)
    {
        var command = SettingsSectionCommand.Parse(phrase, "Nova Prime")!;
        command.Section.Should().BeNull();
        command.Filter.Should().Be(query);
        command.Error.Should().BeNull();
    }

    [Theory]
    [InlineData("show setting")]
    [InlineData("view setting ")]
    [InlineData("open setting voice\nspeed")]
    [InlineData("\tshow setting voice speed")]
    [InlineData("show test.option\n")]
    public void MalformedSearchNeverDefaultsToASectionOrChangesASetting(string phrase)
    {
        var command = SettingsSectionCommand.Parse(phrase, "Kora")!;
        command.Section.Should().BeNull();
        command.Filter.Should().BeNull();
        command.Error.Should().NotBeNull();
        SettingsSectionCommand.Parse("show setting " + new string('a', 257), "Kora")!.Filter.Should().BeNull();
    }

    [Theory]
    [InlineData("open appearance settings", SettingsSection.Appearance)]
    [InlineData("open speech settings", SettingsSection.SpeechAudio)]
    [InlineData("open audio settings", SettingsSection.SpeechAudio)]
    [InlineData("open microphone settings", SettingsSection.SpeechAudio)]
    [InlineData("open response settings", SettingsSection.Responses)]
    [InlineData("open session settings", SettingsSection.Sessions)]
    [InlineData("open call settings", SettingsSection.Calls)]
    public void ParseSelectsOnlyTheDeclaredWholeUtterance(string phrase, SettingsSection expected)
    {
        SettingsSectionCommand.FixedPhrases.Should().Contain(phrase);
        foreach (var input in new[] { phrase, phrase.ToUpperInvariant(), "  " + phrase + "  ", "Nova Prime, " + phrase })
        {
            SettingsSectionCommand.Parse(input, "Nova Prime").Should().Be(new SettingsSectionCommand(expected));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("open")]
    [InlineData("open settings")]
    [InlineData("open setup")]
    [InlineData("open sessions")]
    [InlineData("open callous")]
    [InlineData("open audio0")]
    [InlineData("opening speech settings")]
    [InlineData("please open audio settings")]
    [InlineData("Kora, open audio settings")]
    [InlineData("Nova Primeval open audio settings")]
    [InlineData("show weather")]
    [InlineData("show settings")]
    public void ParseLeavesUnrelatedAndGenericRoutesUntouched(string input) =>
        SettingsSectionCommand.Parse(input, "Nova Prime").Should().BeNull();

    [Theory]
    [InlineData("open audio")]
    [InlineData("open microphone settings now")]
    [InlineData("open speech settings.")]
    [InlineData("open speech & audio settings")]
    [InlineData("open responses settings")]
    [InlineData("open unknown settings")]
    [InlineData("open settings call")]
    [InlineData("open\taudio settings")]
    [InlineData("open audio\nsettings")]
    [InlineData("open audio settings\0")]
    [InlineData("open audio settings\r\n")]
    public void ParseReservesMalformedNavigationWithoutSelectingADefault(string input)
    {
        var result = SettingsSectionCommand.Parse(input, "Kora");
        result.Should().Be(new SettingsSectionCommand(null, SettingsSectionCommand.Syntax));
    }

    [Fact]
    public void ParseEnforcesTheOriginalInputSizeAndControlsBeforeNavigation()
    {
        SettingsSectionCommand.FixedPhrases.Should().HaveCount(7).And.OnlyHaveUniqueItems();
        SettingsSectionCommand.Parse(new string(' ', 237) + "open audio settings", "Kora")!.Section
            .Should().Be(SettingsSection.SpeechAudio);
        SettingsSectionCommand.Parse(new string(' ', 238) + "open audio settings", "Kora")!.Section.Should().BeNull();
        SettingsSectionCommand.Parse("\topen audio settings", "Kora")!.Section.Should().BeNull();
        SettingsSectionCommand.Parse("show " + new string('a', 256) + ".option", "Kora")!.Filter.Should().BeNull();
    }
}

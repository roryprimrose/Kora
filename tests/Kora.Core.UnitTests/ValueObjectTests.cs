using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Dependencies;
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

    [Fact]
    public void Voice_value_objects_expose_supplied_values()
    {
        var microphone = new MicrophoneDevice("7", "Headset");
        var transcript = new VoiceTranscriptEventArgs("help", 0.8f);

        microphone.Id.Should().Be("7");
        microphone.Name.Should().Be("Headset");
        transcript.Transcript.Should().Be("help");
        transcript.Confidence.Should().Be(0.8f);
    }
}
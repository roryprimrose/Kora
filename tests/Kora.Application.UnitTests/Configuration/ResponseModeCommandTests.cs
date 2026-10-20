using System.Text;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Commands;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Configuration;

public sealed class ResponseModeCommandTests
{
    [Theory]
    [InlineData("list response settings", AppearanceCommandOperation.List, null)]
    [InlineData("get responses.default-mode", AppearanceCommandOperation.Get, null)]
    [InlineData("status responses.default-mode", AppearanceCommandOperation.Get, null)]
    [InlineData("reset responses.default-mode", AppearanceCommandOperation.Reset, null)]
    [InlineData("set responses.default-mode to Hybrid", AppearanceCommandOperation.Set, ResponseOutputMode.Hybrid)]
    [InlineData("set responses.default-mode to voiceonly", AppearanceCommandOperation.Set, ResponseOutputMode.VoiceOnly)]
    [InlineData("set responses.default-mode to VisualOnly", AppearanceCommandOperation.Set, ResponseOutputMode.VisualOnly)]
    [InlineData("use visual responses", AppearanceCommandOperation.Set, ResponseOutputMode.VisualOnly)]
    [InlineData("use spoken responses", AppearanceCommandOperation.Set, ResponseOutputMode.VoiceOnly)]
    [InlineData("use spoken and visual responses", AppearanceCommandOperation.Set, ResponseOutputMode.Hybrid)]
    [InlineData("check response mode", AppearanceCommandOperation.Get, null)]
    public void Exact_typed_and_current_prefix_commands_share_enum_grammar(string text, AppearanceCommandOperation operation, ResponseOutputMode? mode)
    {
        foreach (var prefix in new[] { "", "Nova, ", "Nova " })
        {
            ResponseModeCommand.Parse(prefix + text, "Nova").Should().Be(new ResponseModeCommand(operation, mode));
        }
        ResponseModeCommand.FixedPhrases.Should().Contain(text is "set responses.default-mode to voiceonly"
            ? "set responses.default-mode to VoiceOnly" : text);
    }

    [Theory]
    [InlineData("set responses.default-mode to 0")]
    [InlineData("set responses.default-mode to 100")]
    [InlineData("set responses.default-mode to Hybrid,VoiceOnly")]
    [InlineData("set responses.default-mode to visual only")]
    [InlineData("set responses.default-mode to")]
    [InlineData("set responses.queue-mode to VoiceOnly")]
    [InlineData("get responses.default-mode extra")]
    [InlineData("reset responses.default-mode all")]
    [InlineData("list responses")]
    [InlineData("set responses.default-mode to Hybrid\n")]
    [InlineData("use visual responses please")]
    [InlineData("use visual response")]
    [InlineData("use spoken responses and approve once")]
    [InlineData("use spoken and visual")]
    [InlineData("use spoken and visual responses.")]
    [InlineData("use spoken responses\n")]
    [InlineData("use\tvisual responses")]
    [InlineData("use  spoken responses")]
    [InlineData("check response mode please")]
    [InlineData("check  response mode")]
    [InlineData("check response\tmode")]
    [InlineData("check\nresponse mode")]
    public void Malformed_configuration_is_clarified_locally_not_inferred(string text) =>
        ResponseModeCommand.Parse(text, "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);

    [Theory]
    [InlineData("use")]
    [InlineData("use spoken")]
    [InlineData("use visual responses")]
    [InlineData("check")]
    [InlineData("check response")]
    [InlineData("check response mode")]
    public void Exact_alias_wins_over_name_prefix_and_prefixed_aliases_still_work(string name)
    {
        foreach (var phrase in new[] { "use visual responses", "use spoken responses", "use spoken and visual responses", "check response mode" })
        {
            var expected = ResponseModeCommand.Parse(phrase, "Nova");
            ResponseModeCommand.Parse(phrase, name).Should().Be(expected);
            ResponseModeCommand.Parse("  " + phrase.ToUpperInvariant() + "  ", name).Should().Be(expected);
            ResponseModeCommand.Parse(name + ", " + phrase, name).Should().Be(expected);
            ResponseModeCommand.Parse(name + " " + phrase, name).Should().Be(expected);
            ResponseModeCommand.Parse(name + ", " + phrase + " please", name)!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
        }
        ResponseModeCommand.Parse("use spoken responses please", "use")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
    }

    [Theory]
    [InlineData("stop speaking")]
    [InlineData("be quiet")]
    [InlineData("speak louder")]
    [InlineData("speak quieter")]
    [InlineData("speak faster")]
    [InlineData("speak slower")]
    [InlineData("mute")]
    [InlineData("use local models")]
    [InlineData("useful spoken responses")]
    [InlineData("use")]
    [InlineData("usex")]
    [InlineData("Nova Scotia use spoken responses")]
    [InlineData("Kora use spoken responses")]
    [InlineData("use these visual responses")]
    [InlineData("check")]
    [InlineData("checkx")]
    [InlineData("check outputs")]
    [InlineData("check response")]
    [InlineData("check responsex mode")]
    [InlineData("check response body")]
    [InlineData("show response mode")]
    public void Other_commands_and_observed_relative_audio_contracts_are_not_response_mode(string text)
    {
        ResponseModeCommand.Parse(text, "Nova").Should().BeNull();
        ResponseModeCommand.Parse("Nova, " + text, "Nova").Should().BeNull();
    }

    [Theory]
    [InlineData("Nova")]
    [InlineData("Response Mode")]
    [InlineData("Spoken Responses")]
    [InlineData("Visual Responses")]
    [InlineData("Use")]
    public void Response_aliases_do_not_collide_with_built_ins_or_audio_fixed_phrases(string name)
    {
        var router = new BuiltInCommandRouter(new());
        foreach (var phrase in new[] { "use visual responses", "use spoken responses", "use spoken and visual responses", "check response mode" })
        {
            router.Match(name + ", " + phrase, name).IsMatch.Should().BeFalse();
            PlaybackVolumeCommand.Parse(phrase, name).Should().BeNull();
            WindowsSpeechRateCommand.Parse(phrase, name).Should().BeNull();
        }
        router.Match(name + ", stop speaking", name).Command!.Action.Should().Be(BuiltInAction.StopSpeaking);
        router.Match(name + ", be quiet", name).Command!.Action.Should().Be(BuiltInAction.StopSpeaking);
        router.Match("Response Mode, show response mode", "Response Mode").Command!.Action.Should().Be(BuiltInAction.ShowApplication);
        ResponseModeCommand.Parse("Response Mode, show response mode", "Response Mode").Should().BeNull();
    }

    [Theory]
    [InlineData("use spoken responses")]
    [InlineData("check response mode")]
    public void Reserved_alias_bounds_include_the_original_prefix_and_controls(string phrase)
    {
        var exact = "Nova, " + phrase + new string(' ', SessionCommand.MaximumInputBytes - Encoding.UTF8.GetByteCount("Nova, " + phrase));
        ResponseModeCommand.Parse(exact, "Nova")!.Operation.Should().NotBe(AppearanceCommandOperation.Clarify);
        ResponseModeCommand.Parse(exact + " ", "Nova")!.Error.Should().Contain("UTF-8");
        ResponseModeCommand.Parse("Nova,\t" + phrase, "Nova")!.Error.Should().Contain("controls");
        ResponseModeCommand.Parse("Nova,\n" + phrase, "Nova")!.Error.Should().Contain("controls");
        ResponseModeCommand.Parse("Nova, " + phrase + "\0", "Nova")!.Error.Should().Contain("controls");
        ResponseModeCommand.Parse("Nova, " + phrase + new string('é', 512), "Nova")!.Error.Should().Contain("UTF-8");
    }

    [Theory]
    [InlineData("Nova Scotia list response settings")]
    [InlineData("old get responses.default-mode")]
    [InlineData("ordinary request")]
    [InlineData("Nova")]
    public void Unrelated_or_old_prefix_input_is_not_configuration(string text) =>
        ResponseModeCommand.Parse(text, "Nova").Should().BeNull();

    [Fact]
    public void Exact_utf8_input_and_complete_result_limits_are_enforced()
    {
        var prefix = "set responses.default-mode to ";
        var exact = prefix + new string('a', SessionCommand.MaximumInputBytes - Encoding.UTF8.GetByteCount(prefix));
        ResponseModeCommand.Parse(exact, "Kora")!.Error.Should().Be(ResponseModeCommand.Syntax);
        ResponseModeCommand.Parse(exact + "a", "Kora")!.Error.Should().Contain("UTF-8");
        ResponseModeCommand.Parse(prefix + new string('é', 510), "Kora")!.Error.Should().Contain("UTF-8");
        var result = new ResponseModeCommandResult("observed", null, 1, 2, null, ResponseOutputMode.Hybrid,
            ResponseOutputMode.VisualOnly, "default", true);
        var json = ResponseModeCommandResult.Serialize(result);
        json.Should().Contain("\"default\":\"Hybrid\"").And.Contain("\"scope\":\"device-local\"")
            .And.Contain("\"choices\":[\"Hybrid\",\"VoiceOnly\",\"VisualOnly\"]");
        var bytes = Encoding.UTF8.GetByteCount(json);
        var bounded = result with { Recovery = new string('a', SessionCommand.MaximumResultBytes - bytes + 2) };
        Encoding.UTF8.GetByteCount(ResponseModeCommandResult.Serialize(bounded)).Should().Be(SessionCommand.MaximumResultBytes);
        var over = () => ResponseModeCommandResult.Serialize(bounded with { Recovery = bounded.Recovery + "a" });
        over.Should().Throw<InvalidDataException>();
    }
}

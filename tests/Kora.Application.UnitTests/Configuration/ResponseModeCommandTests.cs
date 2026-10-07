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
    public void Malformed_configuration_is_clarified_locally_not_inferred(string text) =>
        ResponseModeCommand.Parse(text, "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);

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

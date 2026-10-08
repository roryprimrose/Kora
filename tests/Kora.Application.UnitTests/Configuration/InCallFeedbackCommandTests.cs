using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Communication;

namespace Kora.Application.UnitTests.Configuration;

public sealed class InCallFeedbackCommandTests
{
    [Fact]
    public void Every_exact_phrase_current_name_and_enum_is_discoverable_without_aliases()
    {
        foreach (var phrase in InCallFeedbackCommand.FixedPhrases)
        {
            InCallFeedbackCommand.Parse(phrase, "Nova")!.Operation.Should().NotBe(AppearanceCommandOperation.Clarify);
            InCallFeedbackCommand.Parse("Nova, " + phrase, "Nova")!.Operation.Should().NotBe(AppearanceCommandOperation.Clarify);
            InCallFeedbackCommand.Parse("Nova " + phrase.ToUpperInvariant(), "Nova")!.Operation.Should().NotBe(AppearanceCommandOperation.Clarify);
        }
        foreach (var mode in Enum.GetValues<InCallFeedbackMode>())
        {
            InCallFeedbackCommand.Parse("set calls.feedback-mode to " + mode, "Nova")!.Value.Should().Be(mode);
        }
        InCallFeedbackCommand.Parse("Kora, get calls.feedback-mode", "Nova").Should().BeNull();
        InCallFeedbackCommand.Parse("NovaX get calls.feedback-mode", "Nova").Should().BeNull();
        InCallFeedbackCommand.Parse("use voice in calls", "Nova").Should().BeNull();
    }

    [Theory]
    [InlineData("list call feedback")]
    [InlineData("get calls.feedback-unknown")]
    [InlineData("set calls.feedback-mode to 1")]
    [InlineData("set calls.feedback-mode to VisualOnly")]
    [InlineData("set calls.feedback-mode to Both and enable microphone")]
    [InlineData("reset calls.feedback-mode now")]
    [InlineData("get calls.feedback-mode\t")]
    public void Invalid_reserved_input_is_explicit_clarification(string input) =>
        InCallFeedbackCommand.Parse(input, "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);

    [Fact]
    public void Exact_input_and_complete_result_bounds_are_enforced()
    {
        const string phrase = "get calls.feedback-mode";
        InCallFeedbackCommand.Parse(phrase.PadRight(1024), "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Get);
        InCallFeedbackCommand.Parse(phrase.PadRight(1025), "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
        InCallFeedbackCommand.Parse(phrase + new string('é', 600), "Kora")!.Error.Should().Contain("1024");
        var result = new InCallFeedbackCommandResult("observed", null, 2, 3, null, InCallFeedbackMode.UI, "default", true);
        var json = InCallFeedbackCommandResult.Serialize(result);
        Encoding.UTF8.GetByteCount(json).Should().BeLessThanOrEqualTo(65536);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("id").GetString().Should().Be("calls.feedback-mode");
        root.GetProperty("default").GetString().Should().Be("UI");
        root.GetProperty("choices").GetArrayLength().Should().Be(4);
        root.GetProperty("scope").GetString().Should().Be("device-local");
        root.GetProperty("schema").GetInt32().Should().Be(1);
        root.GetProperty("saved").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("precedence").GetString().Should().Contain("session");
        FluentActions.Invoking(() => InCallFeedbackCommandResult.Serialize(result with { Recovery = new string('x', 65536) }))
            .Should().Throw<InvalidDataException>();
    }
}

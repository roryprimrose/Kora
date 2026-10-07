using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Commands;

namespace Kora.Application.UnitTests.Configuration;

public sealed class InputDeviceCommandTests
{
    [Theory]
    [InlineData("list input settings", AppearanceCommandOperation.List, null)]
    [InlineData("Kora, GET speech.input-device", AppearanceCommandOperation.Get, null)]
    [InlineData("Kora reset speech.input-device", AppearanceCommandOperation.Reset, null)]
    [InlineData("set speech.input-device to Exact ID", AppearanceCommandOperation.Set, "Exact ID")]
    [InlineData("Kora, set speech.input-device to system-default", AppearanceCommandOperation.Set, "system-default")]
    public void Exact_shapes_preserve_ID_case_and_do_not_select_friendly_names(
        string input, AppearanceCommandOperation operation, string? value)
    {
        var result = InputDeviceCommand.Parse(input, "Kora");
        result!.Operation.Should().Be(operation);
        result.Value.Should().Be(value);
        result.Error.Should().BeNull();
    }

    [Theory]
    [InlineData("list input")]
    [InlineData("list input settings extra")]
    [InlineData("get speech.input-device extra")]
    [InlineData("reset speech.input-device to system-default")]
    [InlineData("set speech.input-device")]
    [InlineData("set speech.input-device to")]
    [InlineData("get speech.input")]
    [InlineData("get speech.input-device\n")]
    public void Incomplete_extra_or_control_shapes_are_clarification_not_model_work(string input)
    {
        var result = InputDeviceCommand.Parse(input, "Kora");
        result!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
        result.Error.Should().Contain(InputDeviceCommand.Syntax);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("Kora")]
    [InlineData("Koralist input settings")]
    [InlineData("Kora! get speech.input-device")]
    [InlineData("get speech.provider")]
    [InlineData("set input device to Headset")]
    public void Other_commands_are_not_aliases(string input) => InputDeviceCommand.Parse(input, "Kora").Should().BeNull();

    [Fact]
    public void Original_input_has_the_existing_1024_UTF8_byte_limit_even_with_prefix()
    {
        const string prefix = "Kora, set speech.input-device to ";
        var accepted = prefix + new string('x', SessionCommand.MaximumInputBytes - Encoding.UTF8.GetByteCount(prefix));
        InputDeviceCommand.Parse(accepted, "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Set);
        InputDeviceCommand.Parse(accepted + "x", "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
        InputDeviceCommand.Parse(prefix + new string('é', 500), "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
    }

    [Fact]
    public void Results_are_complete_versioned_shapes_or_explicitly_rejected_never_truncated()
    {
        var result = new InputDeviceCommandResult("inspected", null, 1, 2, true, "system-default", "mic", "default", "closed")
        {
            Choices = [new("mic", "Headset", false, true)],
        };
        using var json = JsonDocument.Parse(InputDeviceCommandResult.Serialize(result));
        json.RootElement.EnumerateObject().Select(item => item.Name).Should().BeEquivalentTo(
            "outcome", "recovery", "revision", "callRevision", "metadataCurrent", "desired", "effective",
            "source", "readiness", "schema", "id", "type", "default", "scope", "effect",
            "applicationTiming", "resetEffect", "confirmation", "syntax", "choices");
        var choice = json.RootElement.GetProperty("choices")[0];
        choice.EnumerateObject().Select(item => item.Name).Should().BeEquivalentTo("id", "name", "isSystemDefault", "isAvailable");
        choice.GetProperty("id").GetString().Should().Be("mic");
        json.RootElement.GetProperty("id").GetString().Should().Be(InputDeviceCommand.OptionId);
        json.RootElement.GetProperty("schema").GetInt32().Should().Be(1);
        var empty = result with { Recovery = "Refresh", MetadataCurrent = false, Desired = null, Effective = null, Choices = [] };
        using var emptyJson = JsonDocument.Parse(InputDeviceCommandResult.Serialize(empty));
        emptyJson.RootElement.GetProperty("effective").ValueKind.Should().Be(JsonValueKind.Null);
        emptyJson.RootElement.GetProperty("choices").GetArrayLength().Should().Be(0);
        var oversized = result with { Choices = ImmutableArray.Create(new InputDeviceCommandChoice("mic", new string('x', SessionCommand.MaximumResultBytes), false, true)) };
        var serialize = () => InputDeviceCommandResult.Serialize(oversized);
        serialize.Should().Throw<InvalidDataException>().WithMessage("*64 KiB*");
        var blankName = result with { Choices = [new("mic", string.Empty, false, true)] };
        var overhead = Encoding.UTF8.GetByteCount(InputDeviceCommandResult.Serialize(blankName));
        var exactLimit = result with { Choices = [new("mic", new string('x', SessionCommand.MaximumResultBytes - overhead), false, true)] };
        Encoding.UTF8.GetByteCount(InputDeviceCommandResult.Serialize(exactLimit)).Should().Be(SessionCommand.MaximumResultBytes);
    }
}

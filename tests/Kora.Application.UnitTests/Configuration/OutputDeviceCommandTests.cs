using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Commands;

namespace Kora.Application.UnitTests.Configuration;

public sealed class OutputDeviceCommandTests
{
    [Theory]
    [InlineData("list output settings", AppearanceCommandOperation.List, null)]
    [InlineData("Kora, GET speech.output-device", AppearanceCommandOperation.Get, null)]
    [InlineData("Kora status speech.output-device", AppearanceCommandOperation.Get, null)]
    [InlineData("reset speech.output-device", AppearanceCommandOperation.Reset, null)]
    [InlineData("set speech.output-device to Exact ID", AppearanceCommandOperation.Set, "Exact ID")]
    public void Exact_shapes_preserve_complete_case_sensitive_endpoint_value(string input, AppearanceCommandOperation operation, string? value)
    {
        var command = OutputDeviceCommand.Parse(input, "Kora")!;
        command.Operation.Should().Be(operation);
        command.Value.Should().Be(value);
    }

    [Theory]
    [InlineData("list output")]
    [InlineData("list output settings extra")]
    [InlineData("get speech.output-device extra")]
    [InlineData("set speech.output-device to")]
    [InlineData("reset speech.output-device to system-default")]
    [InlineData("status speech.output-device\n")]
    public void Malformed_namespace_requests_never_reach_inference(string input) =>
        OutputDeviceCommand.Parse(input, "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);

    [Theory]
    [InlineData("hello")]
    [InlineData("Kora")]
    [InlineData("Kora! list output settings")]
    [InlineData("Koralist output settings")]
    [InlineData("get speech.provider")]
    public void No_fuzzy_alias_or_other_option_is_admitted(string input) => OutputDeviceCommand.Parse(input, "Kora").Should().BeNull();

    [Fact]
    public void Whole_original_input_and_complete_result_use_existing_exact_UTF8_bounds()
    {
        const string prefix = "Kora, set speech.output-device to ";
        var exact = prefix + new string('x', SessionCommand.MaximumInputBytes - Encoding.UTF8.GetByteCount(prefix));
        OutputDeviceCommand.Parse(exact, "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Set);
        OutputDeviceCommand.Parse(exact + "x", "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
        OutputDeviceCommand.Parse(prefix + new string('é', 500), "Kora")!.Error.Should().Contain("1024");
        var result = new OutputDeviceCommandResult("observed", null, 1, 2, true, "system-default", "one", "one", "default", true, false)
        { Choices = [new("one", "", false, true, false)] };
        using var json = JsonDocument.Parse(OutputDeviceCommandResult.Serialize(result));
        json.RootElement.GetProperty("schema").GetInt32().Should().Be(1);
        json.RootElement.GetProperty("id").GetString().Should().Be(OutputDeviceCommand.OptionId);
        json.RootElement.GetProperty("choices")[0].EnumerateObject().Select(item => item.Name).Should()
            .BeEquivalentTo("id", "name", "system", "available", "muted");
        var overhead = Encoding.UTF8.GetByteCount(OutputDeviceCommandResult.Serialize(result));
        var bounded = result with { Choices = [new("one", new string('x', SessionCommand.MaximumResultBytes - overhead), false, true, false)] };
        Encoding.UTF8.GetByteCount(OutputDeviceCommandResult.Serialize(bounded)).Should().Be(SessionCommand.MaximumResultBytes);
        var excessive = () => OutputDeviceCommandResult.Serialize(bounded with { Recovery = "too much" });
        excessive.Should().Throw<InvalidDataException>().WithMessage("*No partial*");
    }
}

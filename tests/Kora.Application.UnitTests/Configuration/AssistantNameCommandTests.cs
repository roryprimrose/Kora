using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;

namespace Kora.Application.UnitTests.Configuration;

public sealed class AssistantNameCommandTests
{
    [Theory]
    [InlineData("list assistant settings", AppearanceCommandOperation.List, null)]
    [InlineData("Nova, GET assistant.name", AppearanceCommandOperation.Get, null)]
    [InlineData("Nova\treset assistant name", AppearanceCommandOperation.Reset, null)]
    [InlineData("set assistant.name to  ノヴァ  Prime ", AppearanceCommandOperation.Set, " ノヴァ  Prime")]
    [InlineData("set assistant name to Kora", AppearanceCommandOperation.Set, "Kora")]
    public void Exact_operations_preserve_values_for_authoritative_validation(
        string text, AppearanceCommandOperation operation, string? value)
    {
        var command = AssistantNameCommand.Parse(text, "Nova")!;
        command.Operation.Should().Be(operation);
        command.Value.Should().Be(value);
        command.Error.Should().BeNull();
    }

    [Theory]
    [InlineData("get assistant.unknown")]
    [InlineData("reset assistant wake name")]
    [InlineData("set assistant name")]
    [InlineData("get assistant name to Nova")]
    [InlineData("list assistant name")]
    public void Unknown_or_ambiguous_targets_clarify_without_inference(string text)
    {
        AssistantNameCommand.Parse(text, "Nova")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
    }

    [Theory]
    [InlineData("Kora, get assistant name")]
    [InlineData("Nova")]
    [InlineData("NovaX get assistant name")]
    [InlineData("please set assistant name to Nova")]
    [InlineData("set appearance.theme to dark")]
    [InlineData("set speech.voice to default")]
    public void Unrelated_and_retired_prefixes_are_not_aliases(string text)
    {
        AssistantNameCommand.Parse(text, "Nova").Should().BeNull();
    }

    [Fact]
    public void Command_bound_is_exact_and_discovery_is_read_only()
    {
        var prefix = "set assistant.name to ";
        var at = prefix + new string('a', AssistantNameCommand.MaximumLength - prefix.Length);
        AssistantNameCommand.Parse(at, "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Set);
        AssistantNameCommand.Parse(at + "a", "Kora")!.Error.Should().Contain("too long");
        AssistantNameCommand.DiscoveryPhrases.Should().Contain("get assistant name");
        AssistantNameCommand.Syntax.Should().Contain(AssistantNameOption.Id);
    }
}

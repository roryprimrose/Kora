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
    [InlineData("what is your name", AppearanceCommandOperation.Get, null)]
    [InlineData("  Nova, WhAt Is YoUr NaMe? \t", AppearanceCommandOperation.Get, null)]
    [InlineData("Nova reset your name ", AppearanceCommandOperation.Reset, null)]
    [InlineData("call yourself  ノヴァ\tPrime  ", AppearanceCommandOperation.Set, " ノヴァ\tPrime  ")]
    [InlineData("Nova, CHANGE YOUR NAME TO O'NeIl-7 ", AppearanceCommandOperation.Set, "O'NeIl-7 ")]
    [InlineData("call yourself Nova!", AppearanceCommandOperation.Set, "Nova!")]
    [InlineData("call yourself No\u0001va", AppearanceCommandOperation.Set, "No\u0001va")]
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
    [InlineData("what is your name please")]
    [InlineData("what is your name!")]
    [InlineData("reset your name to Nova")]
    [InlineData("reset your name?")]
    [InlineData("call yourself")]
    [InlineData("call yourself \t ")]
    [InlineData("call yourself\tNova")]
    [InlineData("change your name")]
    [InlineData("change your name Nova")]
    [InlineData("change your name to")]
    [InlineData("change your name to  \t")]
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
    [InlineData("Kora, call yourself Atlas")]
    [InlineData("NovaX call yourself Atlas")]
    [InlineData("please call yourself Atlas")]
    [InlineData("say what is your name")]
    [InlineData("what is your namespace")]
    [InlineData("call yourselfNova")]
    [InlineData("change your names to Nova")]
    [InlineData("reset your names")]
    [InlineData("")]
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
        AssistantNameCommand.DiscoveryPhrases.Should().Contain("what is your name").And.Contain("reset your name");
        AssistantNameCommand.Syntax.Should().Contain("call yourself <name>").And.Contain("change your name to <name>");
    }

    [Theory]
    [InlineData("call yourself ")]
    [InlineData("change your name to ")]
    public void Natural_command_bound_preserves_raw_proposal_without_domain_validation(string prefix)
    {
        var value = new string('é', AssistantNameCommand.MaximumLength - prefix.Length);
        AssistantNameCommand.Parse(prefix + value, "Nova")!.Value.Should().Be(value);
        var over = AssistantNameCommand.Parse(prefix + value + "é", "Nova")!;
        over.Operation.Should().Be(AppearanceCommandOperation.Clarify);
        over.Error.Should().Contain("too long");
    }

    [Theory]
    [InlineData("Nova Prime, call yourself Atlas")]
    [InlineData("NOVA PRIME what is your name")]
    public void Current_multiword_prefix_uses_existing_normalization(string text)
    {
        AssistantNameCommand.Parse(text, " Nova\tPrime ")!.Error.Should().BeNull();
    }
}

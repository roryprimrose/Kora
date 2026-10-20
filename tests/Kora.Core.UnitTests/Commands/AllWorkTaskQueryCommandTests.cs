using AwesomeAssertions;

using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class AllWorkTaskQueryCommandTests
{
    private const string Id = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";

    [Theory]
    [InlineData("Nova")]
    [InlineData("Prepare")]
    [InlineData("All Work")]
    public void ExactTypedAndCurrentNameActivatedGrammarCannotBeStolenByNames(string name)
    {
        var command = AllWorkTaskQueryCommand.Parse("prepare all work task " + Id, name)!;
        command.IsValid.Should().BeTrue();
        command.IsHelp.Should().BeFalse();
        command.IsActivated.Should().BeFalse();
        command.TaskId.Should().Be(Guid.Parse(Id));
        command.ToString().Should().Be(nameof(AllWorkTaskQueryCommand)).And.NotContain(Id);
        var activated = AllWorkTaskQueryCommand.Parse(name + ", PREPARE ALL WORK TASK " + Id, name)!;
        activated.IsValid.Should().BeTrue();
        activated.IsActivated.Should().BeTrue();
        AllWorkTaskQueryCommand.Parse("ALL WORK QUERY HELP", name)!.IsHelp.Should().BeTrue();
        AllWorkTaskQueryCommand.DiscoveryPhrases.Should().Contain("all work query help");
    }

    [Theory]
    [InlineData("")]
    [InlineData("current")]
    [InlineData("latest")]
    [InlineData("source")]
    [InlineData("Task name")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("AAAAAAAA-BBBB-4CCC-8DDD-EEEEEEEEEEEE")]
    [InlineData("aaaaaaaaBBBB4ccc8dddeeeeeeeeeeee")]
    [InlineData("{aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee}")]
    [InlineData(" aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee")]
    [InlineData("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee ")]
    [InlineData("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee\n")]
    [InlineData("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee\0")]
    [InlineData("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeee\u200b")]
    [InlineData("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeｅe")]
    [InlineData("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee refresh")]
    public void InvalidIdsAreReservedAndNeverRepaired(string id)
    {
        var command = AllWorkTaskQueryCommand.Parse("Nova, prepare all work task " + id, "Nova")!;
        command.IsValid.Should().BeFalse();
        command.TaskId.Should().BeNull();
    }

    [Theory]
    [InlineData("prepare all work")]
    [InlineData("prepare all work task")]
    [InlineData("prepare all work task\t")]
    [InlineData("prepare all-work task current")]
    [InlineData("prepare\tall work task current")]
    [InlineData("prepare all work query current")]
    [InlineData("all work query")]
    [InlineData("all work query help!")]
    [InlineData("all work query help ")]
    [InlineData("all work query help\n")]
    [InlineData("all work query help selected")]
    public void ReservedMalformedGrammarCannotBecomeReasoning(string text) =>
        AllWorkTaskQueryCommand.Parse(text, "Nova")!.IsValid.Should().BeFalse();

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Nova")]
    [InlineData("open all work")]
    [InlineData("prepare file search")]
    [InlineData("please prepare all work task current")]
    [InlineData("Kora, all work query help")]
    public void UnownedInputIsNotClaimed(string text) =>
        AllWorkTaskQueryCommand.Parse(text, "Nova").Should().BeNull();

    [Fact]
    public void LengthAndControlBoundsApplyBeforeHelpOrIdAdmission()
    {
        AllWorkTaskQueryCommand.Parse("prepare all work task " + new string('a', 160), "Nova")!.IsValid.Should().BeFalse();
        AllWorkTaskQueryCommand.Parse("\tall work query help", "Nova")!.IsValid.Should().BeFalse();
        AllWorkTaskQueryCommand.Guidance.Should().Contain("same validated retained task")
            .And.Contain("ALL other filter drafts remain AND-composed");
    }

    [Fact]
    public void BuiltInNameAliasesCannotShadowReservedHelp()
    {
        var action = () => new BuiltInCommandCatalog().GetCommands("All Work Query");
        action.Should().NotThrow();
        AllWorkTaskQueryCommand.Parse("all work query help", "All Work Query")!.IsHelp.Should().BeTrue();
    }
}

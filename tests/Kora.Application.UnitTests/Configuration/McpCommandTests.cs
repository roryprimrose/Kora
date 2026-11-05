using AwesomeAssertions;

using Kora.Application.Mcp;
using Kora.Core.Mcp;

namespace Kora.Application.UnitTests.Configuration;

public sealed class McpCommandTests
{
    [Theory]
    [InlineData("Kora, use selected mcp tool find reference")]
    [InlineData("Kora use selected mcp tool find reference")]
    [InlineData("use selected mcp tool find reference")]
    public void Preserves_explicit_user_task_without_selecting_an_account_or_endpoint(string input)
    {
        var command = McpCommand.Parse(input, "Kora")!;
        command.Body.Should().Be("find reference");
        command.Error.Should().BeNull();
        command.Stop.Should().BeFalse();
        McpCommand.FixedPhrases.Should().Equal("use selected mcp tool", "invoke selected mcp tool", "stop mcp",
            "manage selected mcp capabilities", "read selected mcp resource", "read selected mcp resource template",
            "preview selected mcp prompt", "use selected mcp resource", "use selected mcp resource template", "use selected mcp prompt");
    }

    [Theory]
    [InlineData("use selected mcp tool")]
    [InlineData("use selected mcp toolX")]
    [InlineData("use selected mcp tool  ")]
    [InlineData("use selected mcp tool \0")]
    public void Invalid_user_task_is_explicit_and_never_becomes_default_work(string input)
    {
        var command = McpCommand.Parse(input, "Kora")!;
        command.Error.Should().NotBeNullOrEmpty();
        command.Body.Should().BeNull();
    }

    [Fact]
    public void Explicit_original_input_does_not_invent_task_arguments_or_model_work()
    {
        var command = McpCommand.Parse("Kora, invoke selected mcp tool", "Kora")!;
        command.Should().Be(new McpCommand(null, false, Explicit: true));
        McpCommand.Parse("invoke selected mcp tool extra", "Kora").Should().BeNull();
    }

    [Fact]
    public void Unknown_text_and_null_never_gain_an_execution_route()
    {
        McpCommand.Parse("use other integration", "Kora").Should().BeNull();
        McpCommand.Parse("Kora", "Kora").Should().BeNull();
        McpCommand.Parse("Korax stop mcp", "Kora").Should().BeNull();
        McpCommand.Parse("Kora stop mcp", "Kora")!.Stop.Should().BeTrue();
        FluentActions.Invoking(() => McpCommand.Parse(null!, "Kora")).Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("read selected mcp resource", McpCapabilityKind.Resource)]
    [InlineData("read selected mcp resource template", McpCapabilityKind.ResourceTemplate)]
    [InlineData("preview selected mcp prompt", McpCapabilityKind.Prompt)]
    public void ContentReadCommandsDoNotInventModelTasks(string phrase, McpCapabilityKind kind)
    {
        var command = McpCommand.Parse("Kora, " + phrase, "Kora")!;
        command.ContentKind.Should().Be(kind);
        command.Explicit.Should().BeTrue();
        command.Body.Should().BeNull();
    }

    [Theory]
    [InlineData("use selected mcp resource", McpCapabilityKind.Resource)]
    [InlineData("use selected mcp resource template", McpCapabilityKind.ResourceTemplate)]
    [InlineData("use selected mcp prompt", McpCapabilityKind.Prompt)]
    public void ContentModelCommandsPreserveTheExactOriginalTask(string phrase, McpCapabilityKind kind)
    {
        var command = McpCommand.Parse(phrase + " Explain this reference.", "Kora")!;
        command.ContentKind.Should().Be(kind);
        command.Body.Should().Be("Explain this reference.");
        command.Explicit.Should().BeFalse();
        McpCommand.Parse(phrase, "Kora")!.Error.Should().NotBeNull();
    }

    [Fact]
    public void CapabilityManagementIsExplicitAndDoesNotInventADiscoveryTarget()
    {
        var command = McpCommand.Parse("manage selected mcp capabilities", "Kora")!;
        command.Manage.Should().BeTrue();
        command.Body.Should().BeNull();
        McpCommand.Parse("manage selected mcp capabilities extra", "Kora").Should().BeNull();
    }
}
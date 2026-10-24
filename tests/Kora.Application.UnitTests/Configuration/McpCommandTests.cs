using AwesomeAssertions;
using Kora.Application.Mcp;

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
        McpCommand.FixedPhrases.Should().Equal("use selected mcp tool", "stop mcp");
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
    public void Unknown_text_and_null_never_gain_an_execution_route()
    {
        McpCommand.Parse("use other integration", "Kora").Should().BeNull();
        McpCommand.Parse("Kora", "Kora").Should().BeNull();
        McpCommand.Parse("Korax stop mcp", "Kora").Should().BeNull();
        McpCommand.Parse("Kora stop mcp", "Kora")!.Stop.Should().BeTrue();
        FluentActions.Invoking(() => McpCommand.Parse(null!, "Kora")).Should().Throw<ArgumentNullException>();
    }
}

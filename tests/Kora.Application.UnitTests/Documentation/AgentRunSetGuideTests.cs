using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Application.Documentation;

namespace Kora.Application.UnitTests.Documentation;

public sealed class AgentRunSetGuideTests
{
    [Fact]
    public void EmbeddedGuideDocumentsOnlyAvailableCompleteOperationAndEveryCommandParses()
    {
        var page = new EmbeddedUserDocumentationProvider().GetPages()
            .Single(item => string.Equals(item.Id, "independent-agent-runs", StringComparison.Ordinal));
        page.Title.Should().Be("Independent local agent runs");
        page.Markdown.Should().Contain("unavailable by default")
            .And.Contain("different existing Active conversations")
            .And.Contain("separately confirm")
            .And.Contain("until it actually")
            .And.Contain("does not autosave")
            .And.Contain("join all actual resources");
        var commands = page.Markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split("```text\n", StringSplitOptions.None)[1]
            .Split("```", StringSplitOptions.None)[0].Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var command in commands)
        { AgentRunCommand.Parse(command, "Kora")!.RunSet!.Action.Should().NotBe("Invalid"); }
    }
}

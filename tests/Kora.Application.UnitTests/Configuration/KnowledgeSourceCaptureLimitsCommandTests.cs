using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.KnowledgeSources;

namespace Kora.Application.UnitTests.Configuration;

public sealed class KnowledgeSourceCaptureLimitsCommandTests
{
    [Theory]
    [InlineData("source capture limits get \"Team\"", "get")]
    [InlineData("Nova source capture limits RESET \"Team\"", "reset")]
    [InlineData("source capture limits lower \"Team\" 2 100 200", "lower")]
    [InlineData("source capture limits confirm", "confirm")]
    [InlineData("source capture limits cancel", "cancel")]
    public void OriginalExactCommandsSelectOnlyNamedOperation(string input, string operation)
    {
        var command = KnowledgeSourceCaptureLimitsCommand.Parse(input, "Nova");
        command.Should().NotBeNull();
        command!.Invalid.Should().BeFalse();
        command.Operation.Should().Be(operation);
        if (string.Equals(operation, "lower", StringComparison.Ordinal))
        { command.Limits.Should().Be(new KnowledgeSourceCaptureLimits(2, 100, 200)); }
        else { command.Limits.Should().BeNull(); }
    }

    [Theory]
    [InlineData("source capture limits")]
    [InlineData("source capture limitsx")]
    [InlineData("source capture limits get Team")]
    [InlineData("source capture limits get \"Team")]
    [InlineData("source capture limits get \"\"")]
    [InlineData("source capture limits get \"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\"")]
    [InlineData("source capture limits get \"bad\nname\"")]
    [InlineData("source capture limits get \"Team\" extra")]
    [InlineData("source capture limits set \"Team\" 1 1 1")]
    [InlineData("source capture limits lower \"Team\"")]
    [InlineData("source capture limits lower \"Team\" 1 1")]
    [InlineData("source capture limits lower \"Team\" x 1 1")]
    [InlineData("source capture limits lower \"Team\" 1 x 1")]
    [InlineData("source capture limits lower \"Team\" 1 1 x")]
    [InlineData("source capture limits lower \"Team\" 33 1 1")]
    [InlineData("source capture limits lower \"Team\" 1 262145 1")]
    [InlineData("source capture limits lower \"Team\" 1 1 1048577")]
    [InlineData("source capture limits lower \"Team\" 0 1 1")]
    [InlineData("source capture limits confirm \"Team\"")]
    public void InvalidInputCannotBecomeWriteOrUnrelatedFallback(string input) =>
        KnowledgeSourceCaptureLimitsCommand.Parse(input, "Nova")!.Invalid.Should().BeTrue();

    [Fact]
    public void UnrelatedInputDoesNotClaimCommand() =>
        KnowledgeSourceCaptureLimitsCommand.Parse("answer using source \"Team\": alpha", "Nova").Should().BeNull();
}

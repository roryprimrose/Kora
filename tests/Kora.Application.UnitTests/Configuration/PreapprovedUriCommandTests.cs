using AwesomeAssertions;
using Kora.Application.Configuration;

namespace Kora.Application.UnitTests.Configuration;

public sealed class PreapprovedUriCommandTests
{
    [Theory]
    [InlineData("list preapproved addresses", PreapprovedUriCommandOperation.List, null)]
    [InlineData("clear preapproved addresses", PreapprovedUriCommandOperation.Clear, null)]
    [InlineData("add preapproved address https://*.example.com/*", PreapprovedUriCommandOperation.Add, "https://*.example.com/*")]
    [InlineData("remove preapproved address https://example.com/", PreapprovedUriCommandOperation.Remove, "https://example.com/")]
    public void TypedAndVoiceCommandsUseTheSameGrammar(
        string text,
        PreapprovedUriCommandOperation operation,
        string? pattern)
    {
        foreach (var prefix in new[] { "", "Nova, ", "Nova " })
        {
            PreapprovedUriCommand.Parse(prefix + text, "Nova")
                .Should().Be(new PreapprovedUriCommand(operation, pattern));
        }
    }

    [Theory]
    [InlineData("add preapproved address")]
    [InlineData("remove preapproved address")]
    [InlineData("clear preapproved addresses now")]
    [InlineData("list preapproved addresses now")]
    public void IncompleteOrExtendedCommandsAreClarified(string text) =>
        PreapprovedUriCommand.Parse(text, "Kora")!.Operation
            .Should().Be(PreapprovedUriCommandOperation.Clarify);

    [Fact]
    public void UnrelatedInputIsNotASetting() =>
        PreapprovedUriCommand.Parse("open https://example.com", "Kora").Should().BeNull();
}

using AwesomeAssertions;

using Kora.Application.Definitions;

namespace Kora.Application.UnitTests.Agents;

public sealed class AgentProfileProposalTests
{
    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"description\":\"Role\",\"body\":\"Body\",\"root\":\"elsewhere\"}")]
    [InlineData("{\"description\":\"Role\",\"body\":\"Body\",\"tools\":[]}")]
    [InlineData("{\"description\":\"Role\",\"body\":\"Body\",\"enabled\":true}")]
    [InlineData("{\"description\":\"Role\",\"body\":\"Body\",\"worker\":\"shell\"}")]
    [InlineData("{\"description\":\"Role\",\"body\":\"Body\",\"dependencies\":[]}")]
    [InlineData("{\"description\":\"Role\",\"description\":\"Other\"}")]
    [InlineData("{\"body\":\"Body\",\"body\":\"Other\"}")]
    [InlineData("{\"description\":null,\"body\":\"Body\"}")]
    [InlineData("{\"description\":\"Role\",\"body\":false}")]
    [InlineData("{\"description\":\"Role\",\"body\":{\"body\":\"nested\"}}")]
    [InlineData("```json\n{\"description\":\"Role\",\"body\":\"Body\"}\n```")]
    public void MalformedOrAuthorityBearingModelOutputIsRejectedWhole(string answer) =>
        ((Action)(() => CustomizationAuthoringService.ParseAgentProposal("example", "", answer)))
            .Should().Throw<InvalidDataException>();

    [Fact]
    public void OversizeModelOutputIsRejectedWithoutStrippingOrTruncating() =>
        ((Action)(() => CustomizationAuthoringService.ParseAgentProposal("example", "", new string('x', 65537))))
            .Should().Throw<InvalidDataException>();
}

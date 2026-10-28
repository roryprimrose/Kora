using AwesomeAssertions;

using Kora.Core.Agents;

namespace Kora.Core.UnitTests.Agents;

public sealed class ModelAgentUseProposalTests
{
    [Fact]
    public void OneExactOpaqueReferenceIsDataOnly()
    {
        const string reference = "abc12345123412341234123412341234";
        ModelAgentUseProposal.Parse("{\"agentRef\":\"" + reference + "\"}").Reference.Should().Be(reference);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"AgentRef\":\"abc12345123412341234123412341234\"}")]
    [InlineData("{\"agentRef\":null}")]
    [InlineData("{\"agentRef\":1}")]
    [InlineData("{\"agentRef\":\"00000000000000000000000000000000\"}")]
    [InlineData("{\"agentRef\":\"ABC12345123412341234123412341234\"}")]
    [InlineData("{\"agentRef\":\"a path or a model-selected host ID\"}")]
    [InlineData("{\"agentRef\":\"abc12345123412341234123412341234\",\"approved\":true}")]
    [InlineData("{\"agentRef\":\"abc12345123412341234123412341234\",\"agentRef\":\"abc12345123412341234123412341234\"}")]
    [InlineData("{\"agentRef\":\"abc12345123412341234123412341234\"} trailing")]
    public void MalformedDuplicateForeignShapeAndForgedAuthorityAreRejectedWhole(string answer)
    {
        var parse = () => ModelAgentUseProposal.Parse(answer);
        parse.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void CompleteByteBoundIsNotTruncated()
    {
        var parse = () => ModelAgentUseProposal.Parse(new string(' ', 129));
        parse.Should().Throw<InvalidDataException>();
    }
}

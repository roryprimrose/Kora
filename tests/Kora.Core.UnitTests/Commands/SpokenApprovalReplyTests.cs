using AwesomeAssertions;
using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class SpokenApprovalReplyTests
{
    [Theory]
    [InlineData("Kora, approve once", SpokenApprovalReply.Operation.Once)]
    [InlineData("Nova approve for session.", SpokenApprovalReply.Operation.Session)]
    [InlineData("Kora deny!", SpokenApprovalReply.Operation.Deny)]
    [InlineData("approve once", SpokenApprovalReply.Operation.Once)]
    [InlineData("  DENY  ", SpokenApprovalReply.Operation.Deny)]
    public void ExactRepliesUseCurrentAndFixedActivationNames(string input, SpokenApprovalReply.Operation decision) =>
        SpokenApprovalReply.Parse(input, "Nova")!.Decision.Should().Be(decision);

    [Theory]
    [InlineData("Kora, approve always")]
    [InlineData("approve once extra")]
    [InlineData("approve\nonce")]
    [InlineData("approve\tfor session")]
    [InlineData("approve for this session")]
    public void UnsupportedApprovalPhrasesAreRecognizedButNotAccepted(string input) =>
        SpokenApprovalReply.Parse(input, "Kora")!.Decision.Should().BeNull();

    [Fact]
    public void OversizedApprovalIsNotAccepted() =>
        SpokenApprovalReply.Parse("approve " + new string('a', 1024), "Kora")!.Decision.Should().BeNull();

    [Theory]
    [InlineData("Kora")]
    [InlineData("Koran approve once")]
    [InlineData("explain approval")]
    [InlineData("deny everything")]
    public void OrdinaryContentIsNotAnApprovalReply(string input) =>
        SpokenApprovalReply.Parse(input, "Kora").Should().BeNull();
}

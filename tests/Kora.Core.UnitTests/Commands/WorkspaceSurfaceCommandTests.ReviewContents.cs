using AwesomeAssertions;

using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed partial class WorkspaceSurfaceCommandTests
{
    [Theory]
    [InlineData("show contents")]
    [InlineData("View Contents")]
    [InlineData("show the contents")]
    [InlineData("Nova, view contents")]
    [InlineData("Kora, show contents")]
    public void ContentsPhrasesOpenTheCurrentReviewContents(string input) =>
        WorkspaceSurfaceCommand.Parse(input, "Nova")!.Target.Should().Be(WorkspaceSurfaceTarget.ReviewContents);

    [Theory]
    [InlineData("show contents please")]
    [InlineData("show contents\n")]
    public void ContentsPhrasesRequireTheExactPhrase(string input) =>
        WorkspaceSurfaceCommand.Parse(input, "Nova")!.Target.Should().Be(WorkspaceSurfaceTarget.Invalid);
}

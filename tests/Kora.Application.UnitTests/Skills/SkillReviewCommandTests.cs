using AwesomeAssertions;

using Kora.Application.Skills;
using Kora.Core.Artifacts;
using Kora.Core.Commands;

namespace Kora.Application.UnitTests.Skills;

public sealed class SkillReviewCommandTests
{
    [Theory]
    [InlineData("open skill packages", SkillReviewSurface.BundledPackages)]
    [InlineData("show skill package review", SkillReviewSurface.BundledPackages)]
    [InlineData("open shared skill sources", SkillReviewSurface.SharedSources)]
    [InlineData("show shared skill sources", SkillReviewSurface.SharedSources)]
    [InlineData(" NOVA PRIME, OPEN SKILL PACKAGES ", SkillReviewSurface.BundledPackages)]
    public void ExactWholeUtterancesSelectOnlyTheRequestedReview(string input, SkillReviewSurface surface) =>
        SkillReviewCommand.Parse(input, "Nova Prime")!.Surface.Should().Be(surface);

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Nova Prime")]
    [InlineData("Kora, open skill packages")]
    [InlineData("please open skill packages")]
    [InlineData("run skill packages")]
    [InlineData("use shared skill sources")]
    [InlineData("apply skill package review")]
    [InlineData("/skill lock")]
    [InlineData("open settings")]
    public void UnrelatedOrArtifactInvocationInputDoesNotSelectReview(string input) =>
        SkillReviewCommand.Parse(input, "Nova Prime").Should().BeNull();

    [Theory]
    [InlineData("open skill packages lock")]
    [InlineData("show skill package review please")]
    [InlineData("open shared skill sources C:\\private")]
    [InlineData("show shared skill sources first")]
    [InlineData("open skill")]
    [InlineData("open skill packages.")]
    [InlineData("open skill packages\t")]
    [InlineData("open skill packages\0")]
    public void ReservedMalformedInputClarifiesWithoutFallingThrough(string input) =>
        SkillReviewCommand.Parse(input, "Kora")!.Surface.Should().BeNull();

    [Fact]
    public void OversizedReservedInputIsNotTruncatedIntoAnAction()
    {
        var boundary = "open skill packages".PadLeft(SessionCommand.MaximumInputBytes);
        SkillReviewCommand.Parse(boundary, "Kora")!.Surface.Should().Be(SkillReviewSurface.BundledPackages);
        SkillReviewCommand.Parse(" " + boundary, "Kora")!.Surface.Should().BeNull();
        SkillReviewCommand.Parse("open skill packages" + new string('é', SessionCommand.MaximumInputBytes), "Kora")!
            .Surface.Should().BeNull();
        SkillReviewCommand.FixedPhrases.Should().HaveCount(4);
        var catalogue = new ArtifactCatalogue([new("bundled.test", ArtifactKind.Skill, "Test", "Inert test",
            "test", ["test"], "bundled", "1", new string('a', 64), "Inert instructions.")]);
        foreach (var phrase in SkillReviewCommand.FixedPhrases)
        {
            new ArtifactCommandRouter(catalogue).Match(phrase, "Kora").IsArtifactCommand.Should().BeFalse();
        }
    }
}

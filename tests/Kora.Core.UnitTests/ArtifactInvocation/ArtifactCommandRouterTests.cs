using AwesomeAssertions;

using Kora.Core.Artifacts;

namespace Kora.Core.UnitTests.Artifacts;

public sealed class ArtifactCommandRouterTests
{
    private readonly ArtifactCommandRouter router = new(new ArtifactCatalogue(
    [
        new ArtifactDefinition(
            "kora.example",
            ArtifactKind.Skill,
            "Example skill",
            "Runs the example workflow.",
            "example",
            ["example", "example workflow"],
            "bundled",
            "1.0.0",
            new string('0', 64),
            "Follow the example workflow."),
        new ArtifactDefinition(
            "kora.explain",
            ArtifactKind.Prompt,
            "Explain",
            "Explains the supplied subject.",
            "explain",
            ["explain"],
            "bundled",
            "1.0.0",
            new string('1', 64),
            "Explain the subject clearly."),
    ]));

    [Theory]
    [InlineData("/example", "kora.example", "")]
    [InlineData("/skill example", "kora.example", "")]
    [InlineData("/explain dependency injection", "kora.explain", "dependency injection")]
    [InlineData("/prompt explain why this failed", "kora.explain", "why this failed")]
    [InlineData("Kora, run example", "kora.example", "")]
    [InlineData("Nova, use the example workflow skill", "kora.example", "")]
    [InlineData("Kora, use explain to describe the failure.", "kora.explain", "describe the failure")]
    public void Match_routes_typed_and_spoken_artifact_commands(
        string input,
        string expectedId,
        string expectedRequest)
    {
        var assistantName = input.StartsWith("Nova", StringComparison.Ordinal) ? "Nova" : "Kora";

        var match = router.Match(input, assistantName);

        match.IsArtifactCommand.Should().BeTrue();
        match.Error.Should().BeNull();
        match.Invocation!.Artifact.Id.Should().Be(expectedId);
        match.Invocation.Request.Should().Be(expectedRequest);
    }

    [Theory]
    [InlineData("/missing")]
    [InlineData("/skill")]
    [InlineData("/")]
    public void Match_identifies_invalid_slash_commands_without_falling_through_to_inference(string input)
    {
        var match = router.Match(input, "Kora");

        match.IsArtifactCommand.Should().BeTrue();
        match.Invocation.Should().BeNull();
        match.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("explain this")]
    [InlineData("please run example")]
    [InlineData("Kora, discuss the example workflow")]
    public void Match_rejects_non_command_text(string input)
    {
        router.Match(input, "Kora").Should().BeSameAs(ArtifactCommandMatch.NotMatched);
    }
}

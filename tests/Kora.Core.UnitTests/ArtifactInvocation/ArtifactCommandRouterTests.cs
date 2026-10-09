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
    [InlineData("/instruction missing", null, null)]
    [InlineData("/instructions missing", null, null)]
    [InlineData("/explain dependency injection", "kora.explain", "dependency injection")]
    [InlineData("/prompt explain why this failed", "kora.explain", "why this failed")]
    [InlineData("Kora, run example", "kora.example", "")]
    [InlineData("Kora run example", "kora.example", "")]
    [InlineData("Kora: run example", "kora.example", "")]
    [InlineData("Kora- run example", "kora.example", "")]
    [InlineData("Kora— run example", "kora.example", "")]
    [InlineData("Nova, use the example workflow skill", "kora.example", "")]
    [InlineData("Kora, use explain to describe the failure.", "kora.explain", "describe the failure")]
    public void Match_routes_typed_and_spoken_artifact_commands(
        string input,
        string? expectedId,
        string? expectedRequest)
    {
        var assistantName = input.StartsWith("Nova", StringComparison.Ordinal) ? "Nova" : "Kora";

        var match = router.Match(input, assistantName);

        match.IsArtifactCommand.Should().BeTrue();
        if (expectedId is null)
        {
            match.Invocation.Should().BeNull();
            match.Error.Should().NotBeNull();
        }
        else
        {
            match.Error.Should().BeNull();
            match.Invocation!.Artifact.Id.Should().Be(expectedId);
            match.Invocation.Request.Should().Be(expectedRequest);
        }
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
    [InlineData("Kora")]
    [InlineData("KoraX, run example")]
    [InlineData("Kora, use explain to    .")]
    public void Match_rejects_non_command_text(string input)
    {
        router.Match(input, "Kora").Should().BeSameAs(ArtifactCommandMatch.NotMatched);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Match_rejects_missing_input(string? input)
    {
        var action = () => router.Match(input!, "Kora");

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Artifact_definition_rejects_invalid_contract_values()
    {
        InvalidDefinition(kind: (ArtifactKind)99).Should().Throw<ArgumentOutOfRangeException>();
        InvalidDefinition(spokenNames: []).Should().Throw<ArgumentException>();
        InvalidDefinition(spokenNames: Enumerable.Repeat("name", 9).ToArray()).Should().Throw<ArgumentException>();
        InvalidDefinition(spokenNames: ["same", "SAME"]).Should().Throw<ArgumentException>();
        InvalidDefinition(id: "").Should().Throw<ArgumentException>();
        InvalidDefinition(id: new string('a', 129)).Should().Throw<ArgumentException>();
        InvalidDefinition(id: "invalid_id").Should().Throw<ArgumentException>();
        InvalidDefinition(command: "-invalid").Should().Throw<ArgumentException>();
        InvalidDefinition(command: "invalid-").Should().Throw<ArgumentException>();
        InvalidDefinition(name: new string('a', 129)).Should().Throw<ArgumentException>();
        InvalidDefinition(description: new string('a', ArtifactDefinition.MaximumDescriptionLength + 1))
            .Should().Throw<ArgumentException>();
        InvalidDefinition(content: "invalid\0content").Should().Throw<ArgumentException>();
        InvalidDefinition(digest: new string('a', 63)).Should().Throw<ArgumentException>();
        InvalidDefinition(digest: new string('g', 64)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Artifact_catalogue_rejects_invalid_bounds_and_conflicts()
    {
        ((Action)(() => _ = new ArtifactCatalogue([]))).Should().Throw<ArgumentException>();
        ((Action)(() => _ = new ArtifactCatalogue(
            Enumerable.Range(0, 129).Select(index => Definition($"item-{index}", $"item-{index}")).ToArray())))
            .Should().Throw<ArgumentException>();
        ((Action)(() => _ = new ArtifactCatalogue([null!]))).Should().Throw<ArgumentException>();
        ((Action)(() => _ = new ArtifactCatalogue(
            [Definition("same", "first"), Definition("same", "second")])))
            .Should().Throw<ArgumentException>();
        ((Action)(() => _ = new ArtifactCatalogue(
            [Definition("first", "same"), Definition("second", "SAME")])))
            .Should().Throw<ArgumentException>();
        ((Action)(() => _ = new ArtifactCatalogue(
            [Definition("first", "first", ["shared"]), Definition("second", "second", ["SHARED"])])))
            .Should().Throw<ArgumentException>();
    }

    private static Action InvalidDefinition(
        string id = "kora.valid",
        ArtifactKind kind = ArtifactKind.Skill,
        string name = "Valid",
        string description = "Valid description.",
        string command = "valid",
        IReadOnlyList<string>? spokenNames = null,
        string? digest = null,
        string content = "Valid content.") =>
        () => _ = new ArtifactDefinition(
            id,
            kind,
            name,
            description,
            command,
            spokenNames ?? ["valid"],
            "bundled",
            "1.0.0",
            digest ?? new string('a', 64),
            content);

    private static ArtifactDefinition Definition(
        string id,
        string command,
        IReadOnlyList<string>? spokenNames = null) =>
        new(
            $"kora.{id}",
            ArtifactKind.Skill,
            id,
            "Description.",
            command,
            spokenNames ?? [command],
            "bundled",
            "1.0.0",
            new string('a', 64),
            "Content.");
}

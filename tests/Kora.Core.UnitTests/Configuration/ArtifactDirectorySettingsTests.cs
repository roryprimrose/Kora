using AwesomeAssertions;

using Kora.Core.Artifacts;
using Kora.Core.Configuration;

namespace Kora.Core.UnitTests.Configuration;

public sealed class ArtifactDirectorySettingsTests
{
    [Fact]
    public void Paths_are_exposed_for_each_supported_kind()
    {
        var settings = new ArtifactDirectorySettings(
            ["skill"],
            ["instruction"],
            ["prompt"],
            ["agent"],
            ["mcp"],
            [new string('a', 64)]);

        settings.GetDirectories(ArtifactKind.Skill).Should().Equal("skill");
        settings.GetDirectories(ArtifactKind.Instruction).Should().Equal("instruction");
        settings.GetDirectories(ArtifactKind.Prompt).Should().Equal("prompt");
        settings.GetDirectories(ArtifactKind.Agent).Should().Equal("agent");
        settings.GetPaths(ArtifactSourceKind.Skill).Should().Equal("skill");
        settings.GetPaths(ArtifactSourceKind.Instruction).Should().Equal("instruction");
        settings.GetPaths(ArtifactSourceKind.Prompt).Should().Equal("prompt");
        settings.GetPaths(ArtifactSourceKind.Agent).Should().Equal("agent");
        settings.GetPaths(ArtifactSourceKind.McpServerConfiguration).Should().Equal("mcp");
        ((Action)(() => settings.GetDirectories((ArtifactKind)99))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => settings.GetPaths((ArtifactSourceKind)99))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Invalid_path_collections_are_rejected()
    {
        ((Action)(() => _ = new ArtifactDirectorySettings(null!, [], [], [], [], [])))
            .Should().Throw<ArgumentNullException>();
        InvalidPaths(Enumerable.Repeat("path", ArtifactDirectorySettings.MaximumPathsPerKind + 1).ToArray())
            .Should().Throw<ArgumentException>();
        InvalidPaths([""]).Should().Throw<ArgumentException>();
        InvalidPaths([" "]).Should().Throw<ArgumentException>();
        InvalidPaths([new string('a', 1025)]).Should().Throw<ArgumentException>();
        InvalidPaths(["bad\0path"]).Should().Throw<ArgumentException>();
        InvalidPaths(["same", "SAME"]).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Invalid_mcp_identifiers_are_rejected()
    {
        ((Action)(() => _ = new ArtifactDirectorySettings([], [], [], [], [], null!)))
            .Should().Throw<ArgumentNullException>();
        InvalidIds(Enumerable.Repeat(new string('a', 64), ArtifactDirectorySettings.MaximumIncludedMcpServers + 1).ToArray())
            .Should().Throw<ArgumentException>();
        InvalidIds([new string('a', 63)]).Should().Throw<ArgumentException>();
        InvalidIds([new string('g', 64)]).Should().Throw<ArgumentException>();
        InvalidIds([new string('A', 64)]).Should().Throw<ArgumentException>();
        InvalidIds([new string('a', 64), new string('a', 64)]).Should().Throw<ArgumentException>();
    }

    private static Action InvalidPaths(IReadOnlyList<string> paths) =>
        () => _ = new ArtifactDirectorySettings(paths, [], [], [], [], []);

    private static Action InvalidIds(IReadOnlyList<string> ids) =>
        () => _ = new ArtifactDirectorySettings([], [], [], [], [], ids);
}

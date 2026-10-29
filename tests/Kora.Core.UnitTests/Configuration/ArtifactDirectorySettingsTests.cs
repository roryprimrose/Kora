using AwesomeAssertions;

using Kora.Core.Artifacts;
using Kora.Core.Configuration;

namespace Kora.Core.UnitTests.Configuration;

public sealed class ArtifactDirectorySettingsTests
{
    [Fact]
    public void Defaults_include_Kora_personal_skills_and_both_editor_profile_locations()
    {
        var defaults = ArtifactDirectorySettings.DefaultDirectories("kora", "roaming", "user");
        defaults.UseDefaultDirectories.Should().BeFalse();
        defaults.SkillDirectories.Should().Equal(Path.Combine("kora", "Skills"),
            Path.Combine("user", ".copilot", "skills"), Path.Combine("user", ".agents", "skills"));
        defaults.InstructionDirectories.Should().Equal(Path.Combine("kora", "Instructions"),
            Path.Combine("roaming", "Code", "User", "prompts"), Path.Combine("roaming", "Code", "User", "profiles"),
            Path.Combine("roaming", "Code - Insiders", "User", "prompts"), Path.Combine("roaming", "Code - Insiders", "User", "profiles"));
        defaults.PromptDirectories[0].Should().Be(Path.Combine("kora", "Prompts"));
        defaults.AgentDirectories[0].Should().Be(Path.Combine("kora", "Agents"));
        defaults.McpConfigurationFiles.Should().BeEmpty();
        defaults.IncludedMcpServerIds.Should().BeEmpty();
    }

    [Fact]
    public void Legacy_additions_merge_once_without_duplicates_or_losing_maximum_legacy_lists()
    {
        var defaults = ArtifactDirectorySettings.DefaultDirectories("kora", "roaming", "user");
        var additions = Enumerable.Range(0, ArtifactDirectorySettings.MaximumPathsPerKind)
            .Select(index => "custom-" + index).ToArray();
        var legacy = new ArtifactDirectorySettings(additions, additions, additions, additions, [], []);
        var merged = legacy.WithDefaults(defaults);
        merged.SkillDirectories.Should().Equal(defaults.SkillDirectories.Concat(additions));
        merged.InstructionDirectories.Should().Equal(defaults.InstructionDirectories.Concat(additions));
        merged.PromptDirectories.Should().Equal(defaults.PromptDirectories.Concat(additions));
        merged.AgentDirectories.Should().Equal(defaults.AgentDirectories.Concat(additions));
        merged.WithDefaults(defaults).Should().BeSameAs(merged);
        new ArtifactDirectorySettings([defaults.SkillDirectories[0].ToUpperInvariant()], [], [], [], [], [])
            .WithDefaults(defaults).SkillDirectories.Should().Equal(defaults.SkillDirectories);
        new ArtifactDirectorySettings([], [], [], [], [], [], useDefaultDirectories: false)
            .WithDefaults(defaults).SkillDirectories.Should().BeEmpty();
    }

    [Fact]
    public void Complete_directory_lists_have_a_separate_bounded_limit_and_do_not_expand_Mcp_limits()
    {
        var paths = Enumerable.Range(0, ArtifactDirectorySettings.MaximumConfiguredDirectoriesPerKind)
            .Select(index => "path-" + index).ToArray();
        new ArtifactDirectorySettings(paths, paths, paths, paths, [], [], false)
            .SkillDirectories.Should().HaveCount(32);
        ((Action)(() => _ = new ArtifactDirectorySettings([.. paths, "overflow"], [], [], [], [], [], false)))
            .Should().Throw<ArgumentException>();
        ((Action)(() => _ = new ArtifactDirectorySettings([], [], [], [], paths, [], false)))
            .Should().Throw<ArgumentException>();
    }

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

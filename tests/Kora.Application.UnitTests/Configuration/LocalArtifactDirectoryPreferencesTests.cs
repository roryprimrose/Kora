using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Configuration;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalArtifactDirectoryPreferencesTests
{
    [Fact]
    public void Settings_roundtrip_normalized_absolute_directories_for_every_artifact_kind()
    {
        var store = new Store();
        var preferences = new LocalArtifactDirectoryPreferences(store);
        var root = Path.Combine(Path.GetTempPath(), "Kora", Guid.NewGuid().ToString("N"));
        var settings = new ArtifactDirectorySettings(
            [Path.Combine(root, "skills") + Path.DirectorySeparatorChar],
            [Path.Combine(root, "instructions")],
            [Path.Combine(root, "prompts")],
            [Path.Combine(root, "agents")],
            [Path.Combine(root, "mcp.json")],
            [new string('a', 64)]);

        preferences.Save(settings);

        var loaded = preferences.Load();
        loaded.SkillDirectories.Should().Equal(Path.Combine(root, "skills"));
        loaded.InstructionDirectories.Should().Equal(Path.Combine(root, "instructions"));
        loaded.PromptDirectories.Should().Equal(Path.Combine(root, "prompts"));
        loaded.AgentDirectories.Should().Equal(Path.Combine(root, "agents"));
        loaded.McpConfigurationFiles.Should().Equal(Path.Combine(root, "mcp.json"));
        loaded.IncludedMcpServerIds.Should().Equal(new string('a', 64));
        store.Writes.Should().Be(1);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Version\":1,\"Skills\":[],\"Instructions\":[],\"Prompts\":[],\"Agents\":[],\"McpConfigurationFiles\":[],\"IncludedMcpServerIds\":[]}")]
    [InlineData("{\"Version\":2,\"Skills\":[],\"Instructions\":[],\"Prompts\":[],\"Agents\":[],\"McpConfigurationFiles\":[],\"IncludedMcpServerIds\":[],\"Unknown\":[]}")]
    [InlineData("{\"Version\":2,\"Skills\":null,\"Instructions\":[],\"Prompts\":[],\"Agents\":[],\"McpConfigurationFiles\":[],\"IncludedMcpServerIds\":[]}")]
    [InlineData("{\"Version\":2,\"Skills\":[\"relative\"],\"Instructions\":[],\"Prompts\":[],\"Agents\":[],\"McpConfigurationFiles\":[],\"IncludedMcpServerIds\":[]}")]
    public void Malformed_or_unknown_saved_values_fail_closed(string text)
    {
        var preferences = new LocalArtifactDirectoryPreferences(new Store { Text = text });

        ((Action)(() => preferences.Load())).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Relative_and_duplicate_directories_are_rejected_before_write()
    {
        var store = new Store();
        var preferences = new LocalArtifactDirectoryPreferences(store);
        var root = Path.Combine(Path.GetTempPath(), "Kora", Guid.NewGuid().ToString("N"));

        ((Action)(() => preferences.Save(new(["relative"], [], [], [], [], []))))
            .Should().Throw<ArgumentException>();
        ((Action)(() => preferences.Save(new([root, root.ToUpperInvariant()], [], [], [], [], []))))
            .Should().Throw<ArgumentException>();
        ((Action)(() => preferences.Save(new([], [], [], [], ["relative.json"], []))))
            .Should().Throw<ArgumentException>();
        store.Writes.Should().Be(0);
    }

    private sealed class Store : IPreferenceStore
    {
        internal string? Text { get; set; }
        internal int Writes { get; private set; }
        public string? ReadText(string fileName) => Text;
        public string[]? ReadLines(string fileName) => throw new NotSupportedException();
        public void WriteText(string fileName, string contents)
        {
            Text = contents;
            Writes++;
        }
        public void WriteLines(string fileName, IEnumerable<string> contents) => throw new NotSupportedException();
        public void Delete(string fileName) => throw new NotSupportedException();
    }
}

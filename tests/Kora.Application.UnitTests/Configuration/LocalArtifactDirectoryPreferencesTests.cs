using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalArtifactDirectoryPreferencesTests
{
    [Fact]
    public void Missing_preferences_return_empty_settings()
    {
        new LocalArtifactDirectoryPreferences(new Store()).Load()
            .Should().BeSameAs(ArtifactDirectorySettings.Empty);
    }

    [Fact]
    public void Public_constructor_uses_application_data_paths()
    {
        var root = Path.Combine(Path.GetTempPath(), "Kora", Guid.NewGuid().ToString("N"));

        new LocalArtifactDirectoryPreferences(new Paths(root)).Load()
            .Should().BeSameAs(ArtifactDirectorySettings.Empty);
    }

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

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"Version\":\"two\",\"Skills\":[],\"Instructions\":[],\"Prompts\":[],\"Agents\":[],\"McpConfigurationFiles\":[],\"IncludedMcpServerIds\":[]}")]
    [InlineData("{\"Version\":2,\"Skills\":[1],\"Instructions\":[],\"Prompts\":[],\"Agents\":[],\"McpConfigurationFiles\":[],\"IncludedMcpServerIds\":[]}")]
    [InlineData("{\"Version\":2,\"Skills\":[],\"Instructions\":{},\"Prompts\":[],\"Agents\":[],\"McpConfigurationFiles\":[],\"IncludedMcpServerIds\":[]}")]
    [InlineData("{\"Version\":2,\"Skills\":[],\"Instructions\":[],\"Prompts\":[],\"Agents\":[],\"McpConfigurationFiles\":[1],\"IncludedMcpServerIds\":[]}")]
    [InlineData("{\"Version\":2,\"Skills\":[],\"Instructions\":[],\"Prompts\":[],\"Agents\":[],\"McpConfigurationFiles\":{},\"IncludedMcpServerIds\":[]}")]
    [InlineData("{\"Version\":2,\"Skills\":[],\"Instructions\":[],\"Prompts\":[],\"Agents\":[],\"McpConfigurationFiles\":[],\"IncludedMcpServerIds\":[1]}")]
    [InlineData("{\"Version\":2,\"Skills\":[],\"Instructions\":[],\"Prompts\":[],\"Agents\":[],\"McpConfigurationFiles\":[],\"IncludedMcpServerIds\":{}}")]
    public void Invalid_json_shapes_fail_closed(string text)
    {
        var preferences = new LocalArtifactDirectoryPreferences(new Store { Text = text });

        ((Action)(() => preferences.Load())).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Bounded_array_limits_are_enforced_on_read()
    {
        var tooManyPaths = string.Join(',', Enumerable.Repeat("\"C:\\\\source\"", 17));
        var tooManyIds = string.Join(',', Enumerable.Repeat($"\"{new string('a', 64)}\"", 129));

        Invalid($"{{\"Version\":2,\"Skills\":[{tooManyPaths}],\"Instructions\":[],\"Prompts\":[],\"Agents\":[],\"McpConfigurationFiles\":[],\"IncludedMcpServerIds\":[]}}");
        Invalid($"{{\"Version\":2,\"Skills\":[],\"Instructions\":[],\"Prompts\":[],\"Agents\":[],\"McpConfigurationFiles\":[{tooManyPaths}],\"IncludedMcpServerIds\":[]}}");
        Invalid($"{{\"Version\":2,\"Skills\":[],\"Instructions\":[],\"Prompts\":[],\"Agents\":[],\"McpConfigurationFiles\":[],\"IncludedMcpServerIds\":[{tooManyIds}]}}");
    }

    [Fact]
    public void Null_and_invalid_normalization_inputs_are_rejected()
    {
        var preferences = new LocalArtifactDirectoryPreferences(new Store());
        ((Action)(() => preferences.Save(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => preferences.Save(new([" "], [], [], [], [], [])))).Should().Throw<ArgumentException>();
        ((Action)(() => preferences.Save(new([], [], [], [], [" "], [])))).Should().Throw<ArgumentException>();

        var root = Path.Combine(Path.GetTempPath(), "Kora", Guid.NewGuid().ToString("N"));
        ((Action)(() => preferences.Save(new([root, Path.Combine(root, ".")], [], [], [], [], []))))
            .Should().Throw<InvalidDataException>();
        ((Action)(() => preferences.Save(new([], [], [], [], [root, Path.Combine(root, ".")], []))))
            .Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Oversized_serialized_preferences_are_rejected()
    {
        var root = Path.Combine(Path.GetTempPath(), "Kora", new string('a', 900));
        var paths = Enumerable.Range(0, ArtifactDirectorySettings.MaximumPathsPerKind)
            .Select(index => root + index)
            .ToArray();
        var preferences = new LocalArtifactDirectoryPreferences(new Store());
        var settings = new ArtifactDirectorySettings(paths, paths, paths, paths, paths, []);

        ((Action)(() => preferences.Save(settings))).Should().Throw<InvalidDataException>();
    }

    private static void Invalid(string text) =>
        ((Action)(() => new LocalArtifactDirectoryPreferences(new Store { Text = text }).Load()))
            .Should().Throw<InvalidDataException>();

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

    private sealed record Paths(string LocalRoot) : IApplicationDataPaths
    {
        public string RoamingRoot => LocalRoot;
    }
}

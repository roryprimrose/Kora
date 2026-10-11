using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Configuration;

public sealed class McpServerConfigurationServiceTests : IDisposable
{
    private readonly string root = Path.Combine(
        Directory.GetCurrentDirectory(),
        "artifacts",
        "mcp-configuration-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_reads_vscode_and_desktop_shapes_without_retaining_secrets()
    {
        Directory.CreateDirectory(root);
        var vscode = Path.Combine(root, "vscode.json");
        var desktop = Path.Combine(root, "desktop.json");
        File.WriteAllText(vscode, """
            {
              "servers": {
                "local-files": {
                  "command": "server.exe",
                  "args": ["--token", "secret-value"],
                  "env": { "TOKEN": "secret-value" }
                }
              }
            }
            """);
        File.WriteAllText(desktop, """
            {
              "mcpServers": {
                "remote-search": {
                  "url": "https://example.test/mcp",
                  "headers": { "Authorization": "Bearer secret-value" }
                }
              }
            }
            """);
        var preferences = new Preferences(new([], [], [], [], [vscode, desktop], []));
        var service = new McpServerConfigurationService(
            new Paths(Path.Combine(root, "kora")),
            preferences,
            Path.Combine(root, "empty-profile"));

        var servers = service.Load();

        servers.Select(server => server.Name).Should().Equal("local-files", "remote-search");
        servers.Select(server => server.Transport).Should().Equal("stdio", "http");
        servers.Should().OnlyContain(server => !server.IsIncluded);
        string.Join('|', servers.Select(server => server.Display)).Should().NotContain("secret-value");
    }

    [Fact]
    public void Included_state_is_resolved_by_source_qualified_server_identity()
    {
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "mcp.json");
        File.WriteAllText(file, """{"servers":{"review":{"command":"review.exe"}}}""");
        var preferences = new Preferences(new([], [], [], [], [file], []));
        var service = new McpServerConfigurationService(
            new Paths(Path.Combine(root, "kora")),
            preferences,
            Path.Combine(root, "empty-profile"));
        var server = service.Load().Single();
        preferences.Settings = new([], [], [], [], [file], [server.Id]);

        var included = service.Load().Single();
        included.IsIncluded.Should().BeTrue();
        included.Display.Should().EndWith("| included");
    }

    [Fact]
    public void Missing_optional_files_are_ignored_and_source_files_are_distinct()
    {
        var paths = new Paths(Path.Combine(root, "kora"));
        var service = new McpServerConfigurationService(
            paths,
            new Preferences(ArtifactDirectorySettings.Empty),
            Path.Combine(root, "profile"));
        service.NotifyChanged();
        var changes = 0;
        service.Changed += (_, _) => changes++;

        service.Load().Should().BeEmpty();
        service.SourceFiles.Should().HaveCount(4);
        service.NotifyChanged();

        changes.Should().Be(1);
    }

    [Fact]
    public void Public_constructor_exposes_default_sources()
    {
        var service = new McpServerConfigurationService(
            new Paths(Path.Combine(root, "kora")),
            new Preferences(ArtifactDirectorySettings.Empty));

        service.SourceFiles.Should().HaveCount(4);
    }

    [Fact]
    public void Missing_required_file_fails_closed()
    {
        var missing = Path.Combine(root, "missing.json");
        var service = new McpServerConfigurationService(
            new Paths(Path.Combine(root, "kora")),
            new Preferences(new([], [], [], [], [missing], [])),
            Path.Combine(root, "profile"));

        ((Action)(() => service.Load())).Should().Throw<FileNotFoundException>();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"servers\":{},\"mcpServers\":{}}")]
    [InlineData("{\"servers\":{\"bad\":{}}}")]
    [InlineData("{\"servers\":{\"bad\":{\"command\":\"x\",\"url\":\"https://example.test\"}}}")]
    [InlineData("{\"servers\":{\"bad\":{\"url\":\"file:///tmp/server\"}}}")]
    public void Invalid_configuration_fails_closed(string content)
    {
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "mcp.json");
        File.WriteAllText(file, content);
        var service = new McpServerConfigurationService(
            new Paths(Path.Combine(root, "kora")),
            new Preferences(new([], [], [], [], [file], [])),
            Path.Combine(root, "empty-profile"));

        ((Action)(() => service.Load())).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"servers\":[]}")]
    [InlineData("{\"servers\":{\"bad\":[]}}")]
    [InlineData("{\"servers\":{\"bad\":{\"command\":1}}}")]
    [InlineData("{\"servers\":{\"bad\":{\"command\":\" \"}}}")]
    [InlineData("{\"servers\":{\"bad\":{\"url\":1}}}")]
    [InlineData("{\"servers\":{\"bad\":{\"url\":\"relative\"}}}")]
    [InlineData("{\"servers\":{\"bad\":{\"url\":\"ftp://example.test\"}}}")]
    [InlineData("{\"servers\":{\" \":{\"command\":\"x\"}}}")]
    public void Invalid_server_shapes_fail_closed(string content) =>
        Invalid(content);

    [Fact]
    public void Invalid_encoding_size_json_and_names_fail_closed()
    {
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "mcp.json");
        var service = Service(file);

        File.WriteAllBytes(file, []);
        ((Action)(() => service.Load())).Should().Throw<InvalidDataException>();
        File.WriteAllBytes(file, Enumerable.Repeat((byte)' ', 256 * 1024 + 1).ToArray());
        ((Action)(() => service.Load())).Should().Throw<InvalidDataException>();
        File.WriteAllBytes(file, [0xff]);
        ((Action)(() => service.Load())).Should().Throw<InvalidDataException>();
        File.WriteAllText(file, "{");
        ((Action)(() => service.Load())).Should().Throw<InvalidDataException>();
        File.WriteAllText(file, "{\"servers\":{\"" + new string('a', 129) + "\":{\"command\":\"x\"}}}");
        ((Action)(() => service.Load())).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Server_limit_comments_and_trailing_commas_are_handled()
    {
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "mcp.json");
        File.WriteAllText(file, """
            {
              // compatible comment
              "servers": {
                "one": { "command": "x", },
              },
            }
            """);
        Service(file).Load().Single().Display.Should().Be("one | stdio | mcp.json | excluded");

        var entries = string.Join(',', Enumerable.Range(0, 129)
            .Select(index => $"\"server-{index}\":{{\"command\":\"x\"}}"));
        File.WriteAllText(file, $"{{\"servers\":{{{entries}}}}}");

        ((Action)(() => Service(file).Load())).Should().Throw<InvalidDataException>();
    }

    private void Invalid(string content)
    {
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "mcp.json");
        File.WriteAllText(file, content);
        ((Action)(() => Service(file).Load())).Should().Throw<InvalidDataException>();
    }

    private McpServerConfigurationService Service(string file) => new(
        new Paths(Path.Combine(root, "kora")),
        new Preferences(new([], [], [], [], [file], [])),
        Path.Combine(root, "empty-profile"));

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed record Paths(string LocalRoot) : IApplicationDataPaths
    {
        public string RoamingRoot => LocalRoot;
    }

    private sealed class Preferences(ArtifactDirectorySettings settings) : IArtifactDirectoryPreferences
    {
        internal ArtifactDirectorySettings Settings { get; set; } = settings;
        public ArtifactDirectorySettings Load() => Settings;
        public void Save(ArtifactDirectorySettings value) => Settings = value;
    }
}

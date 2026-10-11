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

        service.Load().Single().IsIncluded.Should().BeTrue();
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

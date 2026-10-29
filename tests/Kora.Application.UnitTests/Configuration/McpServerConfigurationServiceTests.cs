using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Mcp;

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Https_target_scope_records_user_or_kora_configuration_ownership(bool kora)
    {
        var roaming = Path.Combine(root, "kora");
        var file = kora ? Path.Combine(roaming, "Mcp", "mcp.json") : Path.Combine(root, "user.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, """{"servers":{"search":{"type":"http","url":"https://example.com/mcp"}}}""");
        var preferences = new Preferences(new([], [], [], [], kora ? [] : [file], []));
        var service = new McpServerConfigurationService(new Paths(roaming), preferences, Path.Combine(root, "empty-profile"));
        var summary = service.Load().Single();
        preferences.Settings = new([], [], [], [], kora ? [] : [file], [summary.Id]);
        var account = new McpAccountReference(Guid.NewGuid(), new(1), Guid.NewGuid(), new(new("https://example.com/mcp")));
        var descriptor = await service.ResolveHttpsAsync(summary.Id, account, TestContext.Current.CancellationToken);
        descriptor.Scope.Should().Be(kora ? Kora.Core.Customizations.CustomizationScope.Kora : Kora.Core.Customizations.CustomizationScope.User);
    }

    [Fact]
    public async Task Https_resolution_pins_private_source_and_requires_exact_current_inclusion()
    {
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "runtime.json");
        File.WriteAllText(file, """{"servers":{"search":{"type":"http","url":"https://example.com/mcp"}}}""");
        var preferences = new Preferences(new([], [], [], [], [file], []));
        var service = new McpServerConfigurationService(new Paths(Path.Combine(root, "kora")),
            preferences, Path.Combine(root, "empty-profile"));
        var summary = service.Load().Single();
        var account = new McpAccountReference(Guid.NewGuid(), new(1), Guid.NewGuid(),
            new(new("https://example.com/mcp")));
        await FluentActions.Awaiting(() => service.ResolveHttpsAsync(summary.Id, account, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        preferences.Settings = new([], [], [], [], [file], [summary.Id]);

        var descriptor = await service.ResolveHttpsAsync(summary.Id, account, TestContext.Current.CancellationToken);
        descriptor.ServerId.Should().Be(summary.Id);
        descriptor.ServerName.Should().Be("search");
        descriptor.SourcePath.Should().Be(file);
        descriptor.Scope.Should().Be(Kora.Core.Customizations.CustomizationScope.User);
        descriptor.SourceRevision.Should().HaveLength(64);
        descriptor.Account.Should().BeSameAs(account);
        descriptor.Endpoint.Should().Be(account.Destination);
        descriptor.ToString().Should().Be("McpConnectionDescriptor");
        await service.ValidateCurrentAsync(descriptor, TestContext.Current.CancellationToken);
        service.Load().Single().Display.Should().NotContain("https://").And.NotContain(account.AccountId.ToString());

        File.AppendAllText(file, " ");
        await FluentActions.Awaiting(() => service.ValidateCurrentAsync(descriptor, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        preferences.Settings = new([], [], [], [], [file], []);
        await FluentActions.Awaiting(() => service.ValidateCurrentAsync(descriptor, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("""{"url":"http://example.com/mcp"}""")]
    [InlineData("""{"url":"https://example.com/mcp?token=secret"}""")]
    [InlineData("""{"url":"https://example.com/mcp","headers":{"Authorization":"secret"}}""")]
    [InlineData("""{"url":"https://example.com/mcp","env":{"TOKEN":"secret"}}""")]
    [InlineData("""{"command":"server.exe"}""")]
    [InlineData("""{"url":"https://example.com/mcp","type":"sse"}""")]
    [InlineData("""{"url":"https://example.com/mcp","type":0}""")]
    public async Task Execution_rejects_unsupported_transport_and_inline_credentials_without_changing_summary(string definition)
    {
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "runtime.json");
        File.WriteAllText(file, "{\"mcpServers\":{\"search\":" + definition + "}}");
        var preferences = new Preferences(new([], [], [], [], [file], []));
        var service = new McpServerConfigurationService(new Paths(Path.Combine(root, "kora")),
            preferences, Path.Combine(root, "empty-profile"));
        var summary = service.Load().Single();
        preferences.Settings = new([], [], [], [], [file], [summary.Id]);
        var account = new McpAccountReference(Guid.NewGuid(), new(1), Guid.NewGuid(),
            new(new("https://example.com/mcp")));
        await FluentActions.Awaiting(() => service.ResolveHttpsAsync(summary.Id, account, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidDataException>();
        service.Load().Single().Id.Should().Be(summary.Id);
    }

    [Fact]
    public async Task Execution_rejects_other_destination_unknown_server_and_cancellation()
    {
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "runtime.json");
        File.WriteAllText(file, """{"servers":{"search":{"url":"https://example.com/mcp"}}}""");
        var preferences = new Preferences(new([], [], [], [], [file], []));
        var service = new McpServerConfigurationService(new Paths(Path.Combine(root, "kora")),
            preferences, Path.Combine(root, "empty-profile"));
        var summary = service.Load().Single();
        preferences.Settings = new([], [], [], [], [file], [summary.Id]);
        var other = new McpAccountReference(Guid.NewGuid(), new(1), Guid.NewGuid(),
            new(new("https://other.example.com/mcp")));
        await FluentActions.Awaiting(() => service.ResolveHttpsAsync(summary.Id, other, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => service.ResolveHttpsAsync(new string('0', 64), other, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => service.ResolveHttpsAsync(summary.Id, other, new CancellationToken(true)))
            .Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Awaiting(() => service.ResolveHttpsAsync(summary.Id, null!, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("""{"servers":{"search":{"url":"https://example.com/mcp","url":"https://other.example.com/"}}}""")]
    [InlineData("""{"servers":{"search":{"command":"x","args":[{"token":"one","token":"two"}]}}}""")]
    public void Ambiguous_duplicate_fields_fail_closed_for_metadata_and_execution(string content) => Invalid(content);

    [Theory]
    [InlineData(0)]
    [InlineData(262145)]
    public void Shared_parser_rejects_the_complete_byte_bound_before_adding_metadata(int bytes)
    {
        var summaries = new List<McpServerConfigurationSummary>();
        FluentActions.Invoking(() => McpServerConfigurationService.LoadBytes(new byte[bytes], "synthetic.json", [], summaries))
            .Should().Throw<InvalidDataException>().WithMessage("*byte limit*");
        summaries.Should().BeEmpty();
    }

    [Theory]
    [InlineData("rename")]
    [InlineData("empty")]
    [InlineData("oversize")]
    [InlineData("remove-before")]
    [InlineData("exclude-after")]
    [InlineData("remove-after")]
    [InlineData("use-after")]
    [InlineData("use-cycle-after")]
    public async Task Source_and_inclusion_changes_across_resolution_awaits_are_not_reinterpreted_as_current(string fault)
    {
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "runtime.json");
        File.WriteAllText(file, """{"servers":{"search":{"url":"https://example.com/mcp"}}}""");
        var preferences = new Preferences(new([], [], [], [], [file], []));
        var use = new ArtifactUsePreferences(new LocalSharedSkillPreferencesTests.Store());
        var service = new McpServerConfigurationService(new Paths(Path.Combine(root, "kora")), preferences, Path.Combine(root, "empty-profile"), use);
        var id = service.Load().Single().Id;
        preferences.Settings = new([], [], [], [], [file], [id]);
        var calls = 0;
        preferences.BeforeLoad = () =>
        {
            calls++;
            if (calls == 2)
            {
                if (fault is "rename") { File.WriteAllText(file, """{"servers":{"other":{"url":"https://example.com/mcp"}}}"""); }
                if (fault is "empty") { File.WriteAllBytes(file, []); }
                if (fault is "oversize") { File.WriteAllText(file, new string(' ', 262145)); }
                if (fault is "remove-before") { preferences.Settings = new([], [], [], [], [], [id]); }
            }
            if (calls == 3 && fault is "exclude-after") { preferences.Settings = new([], [], [], [], [file], []); }
            if (calls == 3 && fault is "use-after") { use.SetDisabled(Kora.Core.Artifacts.ArtifactUseIdentity.Mcp(id), true); }
            if (calls == 3 && fault is "use-cycle-after")
            {
                use.SetDisabled(Kora.Core.Artifacts.ArtifactUseIdentity.Mcp(id), true);
                use.SetDisabled(Kora.Core.Artifacts.ArtifactUseIdentity.Mcp(id), false);
            }
            if (calls == 4 && fault is "remove-after") { preferences.Settings = new([], [], [], [], [], [id]); }
        };
        var account = new McpAccountReference(Guid.NewGuid(), new(1), Guid.NewGuid(), new(new("https://example.com/mcp")));
        if (fault is "empty" or "oversize")
        {
            await FluentActions.Awaiting(() => service.ResolveHttpsAsync(id, account, TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidDataException>().WithMessage("*byte limit*");
        }
        else
        {
            await FluentActions.Awaiting(() => service.ResolveHttpsAsync(id, account, TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
        }
    }

    [Fact]
    public async Task A_resolved_mcp_descriptor_cannot_be_revived_by_disable_reenable()
    {
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "runtime.json");
        File.WriteAllText(file, """{"servers":{"search":{"url":"https://example.com/mcp"}}}""");
        var preferences = new Preferences(new([], [], [], [], [file], []));
        var use = new ArtifactUsePreferences(new LocalSharedSkillPreferencesTests.Store());
        var service = new McpServerConfigurationService(new Paths(Path.Combine(root, "kora")), preferences, Path.Combine(root, "empty-profile"), use);
        var id = service.Load().Single().Id;
        preferences.Settings = new([], [], [], [], [file], [id]);
        var account = new McpAccountReference(Guid.NewGuid(), new(1), Guid.NewGuid(), new(new("https://example.com/mcp")));
        var descriptor = await service.ResolveHttpsAsync(id, account, TestContext.Current.CancellationToken);
        await service.ValidateCurrentAsync(descriptor, TestContext.Current.CancellationToken);
        use.SetDisabled(Kora.Core.Artifacts.ArtifactUseIdentity.Mcp(id), true);
        use.SetDisabled(Kora.Core.Artifacts.ArtifactUseIdentity.Mcp(id), false);
        service.Load().Single().IsIncluded.Should().BeTrue();
        await FluentActions.Awaiting(() => service.ValidateCurrentAsync(descriptor, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
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
        internal Action? BeforeLoad { get; set; }
        public ArtifactDirectorySettings Load() { BeforeLoad?.Invoke(); return Settings; }
        public void Save(ArtifactDirectorySettings value) => Settings = value;
    }
}

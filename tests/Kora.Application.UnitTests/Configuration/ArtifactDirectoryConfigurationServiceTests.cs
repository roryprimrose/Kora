using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Configuration;

namespace Kora.Application.UnitTests.Configuration;

public sealed class ArtifactDirectoryConfigurationServiceTests
{
    [Fact]
    public void Add_and_remove_persist_exact_kind_and_write_terminal_audits()
    {
        var preferences = new Preferences();
        var audit = new Audit();
        var service = new ArtifactDirectoryConfigurationService(preferences, audit);
        var directory = Path.Combine(Path.GetTempPath(), "Kora", Guid.NewGuid().ToString("N"));

        var added = service.Add(ArtifactSourceKind.Agent, directory);
        var removed = service.Remove(ArtifactSourceKind.Agent, directory);

        added.AgentDirectories.Should().Equal(directory);
        added.SkillDirectories.Should().BeEmpty();
        removed.AgentDirectories.Should().BeEmpty();
        audit.Events.Select(item => item.Outcome).Should().Equal(
            SecurityAuditOutcome.Requested,
            SecurityAuditOutcome.Succeeded,
            SecurityAuditOutcome.Requested,
            SecurityAuditOutcome.Succeeded);
        audit.Events.Should().OnlyContain(item =>
            item.ActionId == "configuration.artifact-sources"
            && item.TargetId == "preferences.device-local");
    }

    [Fact]
    public void Failed_write_is_audited_and_does_not_publish_proposed_state()
    {
        var preferences = new Preferences { WriteFailure = new IOException("write failed") };
        var audit = new Audit();
        var service = new ArtifactDirectoryConfigurationService(preferences, audit);
        var directory = Path.Combine(Path.GetTempPath(), "Kora", Guid.NewGuid().ToString("N"));

        ((Action)(() => service.Add(ArtifactSourceKind.Prompt, directory)))
            .Should().Throw<IOException>();

        preferences.Settings.PromptDirectories.Should().BeEmpty();
        audit.Events.Select(item => item.Outcome).Should().Equal(
            SecurityAuditOutcome.Requested,
            SecurityAuditOutcome.Failed);
        audit.Events[^1].ReasonCode.Should().Be("io-error");
    }

    [Fact]
    public void Mcp_file_paths_and_server_inclusion_are_persisted_as_separate_settings()
    {
        var preferences = new Preferences();
        var service = new ArtifactDirectoryConfigurationService(preferences, new Audit());
        var file = Path.Combine(Path.GetTempPath(), "Kora", Guid.NewGuid().ToString("N"), "mcp.json");
        var serverId = new string('b', 64);

        service.Add(ArtifactSourceKind.McpServerConfiguration, file);
        var included = service.SetMcpServerIncluded(serverId, included: true);
        var excluded = service.SetMcpServerIncluded(serverId, included: false);

        included.McpConfigurationFiles.Should().Equal(file);
        included.IncludedMcpServerIds.Should().Equal(serverId);
        excluded.McpConfigurationFiles.Should().Equal(file);
        excluded.IncludedMcpServerIds.Should().BeEmpty();
    }

    private sealed class Preferences : IArtifactDirectoryPreferences
    {
        internal ArtifactDirectorySettings Settings { get; private set; } = ArtifactDirectorySettings.Empty;
        internal Exception? WriteFailure { get; init; }
        public ArtifactDirectorySettings Load() => Settings;
        public void Save(ArtifactDirectorySettings settings)
        {
            if (WriteFailure is not null)
            {
                throw WriteFailure;
            }
            Settings = settings;
        }
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        public void Write(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }
}

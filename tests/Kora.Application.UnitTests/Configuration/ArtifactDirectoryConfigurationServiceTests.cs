using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Mcp;

namespace Kora.Application.UnitTests.Configuration;

public sealed class ArtifactDirectoryConfigurationServiceTests
{
    [Fact]
    public void CapabilityWritesPreserveOtherSourcesAndServerSuppressionPreservesChildChoices()
    {
        var server = new string('a', 64);
        var other = new string('b', 64);
        var preferences = new Preferences();
        var audit = new Audit();
        var service = new ArtifactDirectoryConfigurationService(preferences, audit);
        service.SetMcpItemEnabled(server, McpCapabilityKind.Tool, "search", false);
        service.SetMcpCategoryEnabled(other, McpCapabilityCategory.Resources, false);
        service.SetMcpServerIncluded(server, true);
        service.SetMcpServerIncluded(server, false);
        service.Load().McpCapabilities.IsEnabled(server, McpCapabilityKind.Tool, "search").Should().BeFalse();
        service.SetMcpCategoryEnabled(server, McpCapabilityCategory.Tools, true)
            .McpCapabilities.IsEnabled(server, McpCapabilityKind.Tool, "search").Should().BeTrue();
        var current = service.Load().McpCapabilities;
        var proposed = current.SetItem(server, McpCapabilityKind.Prompt, "review", false)
            .SetCategory(other, McpCapabilityCategory.Resources, true);
        var saved = service.SetMcpCapabilities(server, true, proposed, current.Fingerprint(server));
        saved.McpCapabilities.IsEnabled(server, McpCapabilityKind.Prompt, "review").Should().BeFalse();
        saved.McpCapabilities.IsEnabled(other, McpCapabilityKind.Resource, "data://item").Should().BeFalse();
        saved.IncludedMcpServerIds.Should().Equal(server);
        FluentActions.Invoking(() => service.SetMcpCapabilities(server, false, current, current.Fingerprint(server)))
            .Should().Throw<InvalidOperationException>();
        audit.Events.Should().OnlyContain(item => item.Outcome == SecurityAuditOutcome.Requested || item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public void ExhaustedCapabilityRevisionCannotWriteOrPublishAConfigurationSuccess()
    {
        var server = new string('a', 64);
        var selection = new McpCapabilitySelection([new(server, McpCapabilityCategory.Tools, true, [], long.MaxValue)]);
        var original = new ArtifactDirectorySettings([], [], [], [], [], [], mcpCapabilities: selection);
        var preferences = new CapabilityReadbackPreferences(original);
        var audit = new Audit();
        var service = new ArtifactDirectoryConfigurationService(preferences, audit);
        var changes = 0;
        service.Changed += (_, _) => changes++;
        FluentActions.Invoking(() => service.SetMcpCapabilities(server, true, McpCapabilitySelection.Default, selection.Fingerprint(server)))
            .Should().Throw<InvalidOperationException>().WithMessage("*revision is exhausted*");
        preferences.Load().Should().BeSameAs(original);
        preferences.Writes.Should().Be(0);
        changes.Should().Be(0);
        audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("category")]
    [InlineData("item")]
    [InlineData("inventory")]
    public void CapabilityReadbackCannotSilentlyDiscardTheReviewedSelection(string operation)
    {
        var server = new string('a', 64);
        var original = new ArtifactDirectorySettings([], [], [], [], [], [server]);
        var preferences = new CapabilityReadbackPreferences(original) { DiscardCapabilities = true };
        var audit = new Audit();
        var service = new ArtifactDirectoryConfigurationService(preferences, audit);
        var changes = 0;
        service.Changed += (_, _) => changes++;
        Action write = operation switch
        {
            "category" => () => service.SetMcpCategoryEnabled(server, McpCapabilityCategory.Resources, false),
            "item" => () => service.SetMcpItemEnabled(server, McpCapabilityKind.Prompt, "review", false),
            _ => () => service.SetMcpCapabilities(server, true,
                original.McpCapabilities.SetCategory(server, McpCapabilityCategory.Tools, false),
                original.McpCapabilities.Fingerprint(server)),
        };
        write.Should().Throw<InvalidDataException>().WithMessage("*did not match readback*");
        preferences.Writes.Should().Be(1);
        preferences.Load().McpCapabilities.Categories.Should().BeEmpty();
        changes.Should().Be(0);
        audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Failed);
        audit.Events[^1].ReasonCode.Should().Be("invalid-data");
    }

    private sealed class CapabilityReadbackPreferences(ArtifactDirectorySettings initial) : IArtifactDirectoryPreferences
    {
        private ArtifactDirectorySettings settings = initial;
        internal bool DiscardCapabilities { get; init; }
        internal int Writes { get; private set; }
        public ArtifactDirectorySettings Load() => settings;
        public void Save(ArtifactDirectorySettings value)
        {
            Writes++;
            settings = DiscardCapabilities
                ? new(value.SkillDirectories, value.InstructionDirectories, value.PromptDirectories, value.AgentDirectories,
                    value.McpConfigurationFiles, value.IncludedMcpServerIds, value.UseDefaultDirectories)
                : value;
        }
    }

    [Fact]
    public void Load_publishes_current_settings()
    {
        var preferences = new Preferences();
        var service = new ArtifactDirectoryConfigurationService(preferences, new Audit());
        var changes = 0;
        service.Changed += (_, _) => changes++;

        service.Load().Should().BeSameAs(preferences.Settings);

        changes.Should().Be(1);

        new ArtifactDirectoryConfigurationService(preferences, new Audit()).Load()
            .Should().BeSameAs(preferences.Settings);
    }

    [Theory]
    [InlineData(ArtifactSourceKind.Skill)]
    [InlineData(ArtifactSourceKind.Instruction)]
    [InlineData(ArtifactSourceKind.Prompt)]
    public void Add_and_remove_support_every_directory_kind(ArtifactSourceKind kind)
    {
        var service = new ArtifactDirectoryConfigurationService(new Preferences(), new Audit());
        service.Changed += (_, _) => { };
        var directory = Path.Combine(Path.GetTempPath(), "Kora", Guid.NewGuid().ToString("N"));

        service.Add(kind, directory).GetPaths(kind).Should().Equal(directory);
        service.Remove(kind, directory).GetPaths(kind).Should().BeEmpty();
    }

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

    [Fact]
    public void External_parent_off_on_advances_persistent_revision_without_resetting_children_or_noop_writes()
    {
        var preferences = new Preferences();
        var service = new ArtifactDirectoryConfigurationService(preferences, new Audit());
        var serverId = new string('b', 64);
        service.SetMcpServerIncluded(serverId, true);
        service.SetMcpItemEnabled(serverId, McpCapabilityKind.Tool, "search", false);
        var before = preferences.Settings.McpCapabilities.Fingerprint(serverId);
        service.SetMcpServerIncluded(serverId, false);
        service.SetMcpServerIncluded(serverId, true);
        preferences.Settings.McpCapabilities.Fingerprint(serverId).Should().NotBe(before);
        preferences.Settings.McpCapabilities.IsEnabled(serverId, McpCapabilityKind.Tool, "search").Should().BeFalse();
        preferences.Settings.IncludedMcpServerIds.Should().Equal(serverId);
        var writes = preferences.Writes;
        service.SetMcpServerIncluded(serverId, true);
        preferences.Writes.Should().Be(writes);
    }

    [Fact]
    public void Builtin_item_changes_preserve_explicit_parent_and_revision_exhaustion_cannot_write()
    {
        var preferences = new Preferences();
        preferences.Save(new([], [], [], [], [], [KoraMcpCatalog.ServerId]));
        var service = new ArtifactDirectoryConfigurationService(preferences, new Audit());
        service.SetMcpItemEnabled(KoraMcpCatalog.ServerId, McpCapabilityKind.Tool, "settings.get", false)
            .IncludedMcpServerIds.Should().Equal(KoraMcpCatalog.ServerId);
        var exhausted = new McpCapabilitySelection(
            [new(KoraMcpCatalog.ServerId, McpCapabilityCategory.Tools, true, [], long.MaxValue)]);
        preferences.Save(new([], [], [], [], [], [KoraMcpCatalog.ServerId], mcpCapabilities: exhausted));
        var writes = preferences.Writes;
        var mutation = () => service.SetMcpCapabilities(KoraMcpCatalog.ServerId, false, exhausted,
            exhausted.Fingerprint(KoraMcpCatalog.ServerId));
        mutation.Should().Throw<InvalidOperationException>();
        preferences.Writes.Should().Be(writes);
        var stale = () => service.SetMcpCapabilities(KoraMcpCatalog.ServerId, false, exhausted, "stale");
        stale.Should().Throw<InvalidOperationException>();
        preferences.Writes.Should().Be(writes);
    }

    [Fact]
    public void Duplicate_and_missing_changes_are_rejected_without_writes()
    {
        var preferences = new Preferences();
        var service = new ArtifactDirectoryConfigurationService(preferences, new Audit());
        var directory = Path.Combine(Path.GetTempPath(), "Kora", Guid.NewGuid().ToString("N"));

        service.Add(ArtifactSourceKind.Skill, directory);

        ((Action)(() => service.Add(ArtifactSourceKind.Skill, directory.ToUpperInvariant())))
            .Should().Throw<InvalidOperationException>();
        ((Action)(() => service.Remove(ArtifactSourceKind.Prompt, directory)))
            .Should().Throw<InvalidOperationException>();
        preferences.Writes.Should().Be(1);
    }

    [Fact]
    public void Unchanged_mcp_inclusion_returns_without_writing()
    {
        var preferences = new Preferences();
        var service = new ArtifactDirectoryConfigurationService(preferences, new Audit());
        var serverId = new string('c', 64);

        service.SetMcpServerIncluded(serverId, false).Should().BeSameAs(preferences.Settings);
        service.SetMcpServerIncluded(serverId, true);
        var writes = preferences.Writes;
        service.SetMcpServerIncluded(serverId, true).IncludedMcpServerIds.Should().Equal(serverId);

        preferences.Writes.Should().Be(writes);
        ((Action)(() => service.SetMcpServerIncluded("", true))).Should().Throw<ArgumentException>();
    }

    [Theory]
    [MemberData(nameof(SaveFailures))]
    public void Supported_save_failures_are_audited(Exception failure, string reasonCode)
    {
        var preferences = new Preferences { WriteFailure = failure };
        var audit = new Audit();
        var service = new ArtifactDirectoryConfigurationService(preferences, audit);
        var directory = Path.Combine(Path.GetTempPath(), "Kora", Guid.NewGuid().ToString("N"));

        var exception = Record.Exception(() => service.Add(ArtifactSourceKind.Skill, directory));

        exception.Should().NotBeNull();
        exception!.GetType().Should().Be(failure.GetType());
        audit.Events[^1].Outcome.Should().Be(SecurityAuditOutcome.Failed);
        audit.Events[^1].ReasonCode.Should().Be(reasonCode);
    }

    [Fact]
    public void Mismatched_readback_fails_closed()
    {
        var preferences = new Preferences { ReturnEmptyAfterWrite = true };
        var audit = new Audit();
        var service = new ArtifactDirectoryConfigurationService(preferences, audit);
        var directory = Path.Combine(Path.GetTempPath(), "Kora", Guid.NewGuid().ToString("N"));

        ((Action)(() => service.Add(ArtifactSourceKind.Skill, directory)))
            .Should().Throw<InvalidDataException>();

        audit.Events[^1].ReasonCode.Should().Be("invalid-data");
    }

    [Fact]
    public void Readback_cannot_silently_restore_implicit_default_directories()
    {
        var preferences = new PolicyMismatchPreferences();
        var audit = new Audit();
        var service = new ArtifactDirectoryConfigurationService(preferences, audit);
        var directory = Path.GetFullPath("configured-directory");
        ((Action)(() => service.Add(ArtifactSourceKind.Skill, directory)))
            .Should().Throw<InvalidDataException>();
        audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Failed);
    }

    private sealed class PolicyMismatchPreferences : IArtifactDirectoryPreferences
    {
        private ArtifactDirectorySettings settings = new([], [], [], [], [], [], false);
        public ArtifactDirectorySettings Load() => settings;
        public void Save(ArtifactDirectorySettings value) => settings = new(value.SkillDirectories,
            value.InstructionDirectories, value.PromptDirectories, value.AgentDirectories,
            value.McpConfigurationFiles, value.IncludedMcpServerIds);
    }

    public static TheoryData<Exception, string> SaveFailures => new()
    {
        { new IOException("write failed"), "io-error" },
        { new UnauthorizedAccessException("denied"), "access-denied" },
        { new InvalidDataException("invalid"), "invalid-data" },
        { new ArgumentException("invalid"), "invalid-data" },
        { new NotSupportedException("invalid"), "invalid-data" },
    };

    private sealed class Preferences : IArtifactDirectoryPreferences
    {
        internal ArtifactDirectorySettings Settings { get; private set; } = ArtifactDirectorySettings.Empty;
        internal Exception? WriteFailure { get; init; }
        internal bool ReturnEmptyAfterWrite { get; init; }
        internal int Writes { get; private set; }
        public ArtifactDirectorySettings Load() =>
            ReturnEmptyAfterWrite && Writes > 0 ? ArtifactDirectorySettings.Empty : Settings;
        public void Save(ArtifactDirectorySettings settings)
        {
            if (WriteFailure is not null)
            {
                throw WriteFailure;
            }
            Settings = settings;
            Writes++;
        }
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        public void Write(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }
}
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Mcp;

namespace Kora.Application.Configuration;

public sealed class ArtifactDirectoryConfigurationService(
    IArtifactDirectoryPreferences preferences,
    ISecurityAuditLog auditLog)
{
    private readonly Lock gate = new();
    private ArtifactDirectorySettings settings = ArtifactDirectorySettings.Empty;

    public event EventHandler? Changed;

    public ArtifactDirectorySettings Load()
    {
        lock (gate)
        {
            settings = ReadCurrentCore();
            Changed?.Invoke(this, EventArgs.Empty);
            return settings;
        }
    }

    internal ArtifactDirectorySettings ReadCurrent()
    {
        lock (gate)
        {
            settings = ReadCurrentCore();
            return settings;
        }
    }

    public ArtifactDirectorySettings Add(ArtifactSourceKind kind, string path) =>
        Change(kind, path, add: true);

    public ArtifactDirectorySettings Remove(ArtifactSourceKind kind, string path) =>
        Change(kind, path, add: false);

    public ArtifactDirectorySettings SetMcpServerIncluded(string serverId, bool included)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);
        lock (gate)
        {
            settings = preferences.Load();
            if (!string.Equals(serverId, KoraMcpCatalog.ServerId, StringComparison.Ordinal)
                && settings.IncludedMcpServerIds.Contains(serverId, StringComparer.Ordinal) == included)
            {
                return settings;
            }
            return SetMcpCapabilities(serverId, included, settings.McpCapabilities,
                settings.McpCapabilities.Fingerprint(serverId));
        }
    }

    public ArtifactDirectorySettings SetMcpCategoryEnabled(string serverId, McpCapabilityCategory category, bool enabled)
    {
        lock (gate)
        {
            settings = preferences.Load();
            return Save(WithCapabilities(settings.McpCapabilities.SetCategory(serverId, category, enabled)));
        }
    }

    public ArtifactDirectorySettings SetMcpItemEnabled(string serverId, McpCapabilityKind kind, string identity, bool enabled)
    {
        lock (gate)
        {
            settings = preferences.Load();
            return Save(WithCapabilities(settings.McpCapabilities.SetItem(serverId, kind, identity, enabled)));
        }
    }

    private ArtifactDirectorySettings WithCapabilities(McpCapabilitySelection capabilities)
    {
        var included = settings.IncludedMcpServerIds.AsEnumerable();
        if (!settings.McpCapabilities.Categories.Any(category =>
                string.Equals(category.ServerId, KoraMcpCatalog.ServerId, StringComparison.Ordinal))
            && capabilities.Categories.Any(category =>
                string.Equals(category.ServerId, KoraMcpCatalog.ServerId, StringComparison.Ordinal))
            && !settings.IncludedMcpServerIds.Contains(KoraMcpCatalog.ServerId, StringComparer.Ordinal))
        { included = included.Append(KoraMcpCatalog.ServerId); }
        return new(settings.SkillDirectories, settings.InstructionDirectories, settings.PromptDirectories,
            settings.AgentDirectories, settings.McpConfigurationFiles, included.ToArray(),
            settings.UseDefaultDirectories, capabilities);
    }

    public ArtifactDirectorySettings SetMcpCapabilities(string serverId, bool included,
        McpCapabilitySelection proposed, string expectedFingerprint)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        lock (gate)
        {
            settings = preferences.Load();
            if (!string.Equals(settings.McpCapabilities.Fingerprint(serverId), expectedFingerprint, StringComparison.Ordinal))
            { throw new InvalidOperationException("MCP capability preferences changed. Review a fresh inventory."); }
            var replacements = Enum.GetValues<McpCapabilityCategory>().Select(category =>
            {
                var item = proposed.Categories.SingleOrDefault(value => string.Equals(value.ServerId, serverId, StringComparison.Ordinal)
                    && value.Category == category) ?? new McpCategorySelection(serverId, category, true, []);
                var revision = settings.McpCapabilities.Categories.SingleOrDefault(current =>
                    string.Equals(current.ServerId, serverId, StringComparison.Ordinal) && current.Category == item.Category)?.Revision ?? 0;
                if (revision == long.MaxValue) { throw new InvalidOperationException("The MCP category revision is exhausted."); }
                return new McpCategorySelection(serverId, item.Category, item.Enabled, item.Items, revision + 1);
            });
            var selection = new McpCapabilitySelection(settings.McpCapabilities.Categories
                .Where(item => !string.Equals(item.ServerId, serverId, StringComparison.Ordinal))
                .Concat(replacements));
            var includedIds = settings.IncludedMcpServerIds.Where(id => !string.Equals(id, serverId, StringComparison.Ordinal));
            if (included) { includedIds = includedIds.Append(serverId); }
            return Save(new(settings.SkillDirectories, settings.InstructionDirectories, settings.PromptDirectories,
                settings.AgentDirectories, settings.McpConfigurationFiles, includedIds.ToArray(),
                settings.UseDefaultDirectories, selection));
        }
    }

    private ArtifactDirectorySettings Change(ArtifactSourceKind kind, string path, bool add)
    {
        var normalized = kind == ArtifactSourceKind.McpServerConfiguration
            ? LocalArtifactDirectoryPreferences.NormalizeFile(path)
            : LocalArtifactDirectoryPreferences.NormalizeDirectory(path);
        lock (gate)
        {
            settings = preferences.Load();
            var current = settings.GetPaths(kind);
            var next = add
                ? current.Append(normalized).ToArray()
                : current.Where(item => !string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (add && current.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("That source path is already registered for this kind.");
            }
            if (!add && next.Length == current.Count)
            {
                throw new InvalidOperationException("That source path is no longer registered for this kind.");
            }
            return Save(Replace(settings, kind, next));
        }
    }

    private ArtifactDirectorySettings Save(ArtifactDirectorySettings proposed)
    {
        var audit = new SecurityAuditEvent(
                Guid.NewGuid(),
                SecurityAuditCategory.ConfigurationWrite,
                "configuration.artifact-sources",
                SecurityAuditOutcome.Requested,
                SecurityAuditInitiator.LocalUser,
                "preferences.device-local");
        auditLog.Write(audit);
        try
        {
            preferences.Save(proposed);
            var readBack = preferences.Load();
            if (!Equivalent(proposed, readBack))
            {
                throw new InvalidDataException("Saved artifact source preferences did not match readback.");
            }

            settings = readBack;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or ArgumentException
            or NotSupportedException)
        {
            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed,
                exception is IOException ? "io-error"
                : exception is UnauthorizedAccessException ? "access-denied"
                : "invalid-data"));
            throw;
        }
        auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
        Changed?.Invoke(this, EventArgs.Empty);
        return settings;
    }

    private ArtifactDirectorySettings ReadCurrentCore() => preferences.Load();

    private static ArtifactDirectorySettings Replace(
        ArtifactDirectorySettings current,
        ArtifactSourceKind kind,
        IReadOnlyList<string> paths) => new(
        kind == ArtifactSourceKind.Skill ? paths : current.SkillDirectories,
        kind == ArtifactSourceKind.Instruction ? paths : current.InstructionDirectories,
        kind == ArtifactSourceKind.Prompt ? paths : current.PromptDirectories,
        kind == ArtifactSourceKind.Agent ? paths : current.AgentDirectories,
        kind == ArtifactSourceKind.McpServerConfiguration ? paths : current.McpConfigurationFiles,
        current.IncludedMcpServerIds, current.UseDefaultDirectories, current.McpCapabilities);

    private static bool Equivalent(ArtifactDirectorySettings left, ArtifactDirectorySettings right) =>
        left.UseDefaultDirectories == right.UseDefaultDirectories
        && Enum.GetValues<ArtifactSourceKind>().All(kind =>
            left.GetPaths(kind).SequenceEqual(right.GetPaths(kind), StringComparer.OrdinalIgnoreCase))
        && left.IncludedMcpServerIds.SequenceEqual(right.IncludedMcpServerIds, StringComparer.Ordinal)
        && left.McpCapabilities.Categories.Select(category => category.ServerId)
            .Concat(right.McpCapabilities.Categories.Select(category => category.ServerId))
            .All(serverId => string.Equals(left.McpCapabilities.Fingerprint(serverId),
                right.McpCapabilities.Fingerprint(serverId), StringComparison.Ordinal));
}

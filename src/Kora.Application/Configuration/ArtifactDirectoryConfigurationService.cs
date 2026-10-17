using Kora.Core.Auditing;
using Kora.Core.Configuration;

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
            var next = included
                ? settings.IncludedMcpServerIds.Append(serverId).Distinct(StringComparer.Ordinal).ToArray()
                : settings.IncludedMcpServerIds.Where(id => !string.Equals(id, serverId, StringComparison.Ordinal)).ToArray();
            if (next.SequenceEqual(settings.IncludedMcpServerIds, StringComparer.Ordinal))
            {
                return settings;
            }
            return Save(new(
                settings.SkillDirectories,
                settings.InstructionDirectories,
                settings.PromptDirectories,
                settings.AgentDirectories,
                settings.McpConfigurationFiles,
                next));
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
        current.IncludedMcpServerIds);

    private static bool Equivalent(ArtifactDirectorySettings left, ArtifactDirectorySettings right) =>
        Enum.GetValues<ArtifactSourceKind>().All(kind =>
            left.GetPaths(kind).SequenceEqual(right.GetPaths(kind), StringComparer.OrdinalIgnoreCase))
        && left.IncludedMcpServerIds.SequenceEqual(right.IncludedMcpServerIds, StringComparer.Ordinal);
}

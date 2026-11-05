using Kora.Core.Artifacts;
using Kora.Core.Mcp;
using Kora.Core.Dependencies;
using Kora.Core.Configuration;
using System.Collections.Immutable;

namespace Kora.Application.Configuration;

/// <summary>Shared device-local availability. Allowing use never grants invocation authority.</summary>
public sealed class McpCapabilityPreferences(ArtifactUsePreferences preferences,
    IArtifactDirectoryPreferences directories, ArtifactDirectoryConfigurationService configuration)
{
    private volatile bool unavailable;
    public string Revision => RevisionFor(KoraMcpCatalog.ServerId);

    internal string RevisionFor(string serverId)
    {
        var settings = directories.Load();
        return FormattableString.Invariant(
            $"{preferences.Revision}:{settings.McpCapabilities.Fingerprint(serverId)}:{IsIncluded(settings, serverId)}:{unavailable}");
    }
    internal long ServerUseRevision => preferences.Revision;
    public event EventHandler? Changed
    {
        add { preferences.Changed += value; configuration.Changed += value; }
        remove { preferences.Changed -= value; configuration.Changed -= value; }
    }

    public bool IsServerEnabled(string serverId)
    {
        McpCapabilitySelection.ValidateServer(serverId);
        return !unavailable && IsIncluded(directories.Load(), serverId)
            && !preferences.IsDisabled(ArtifactUseIdentity.Mcp(serverId));
    }

    public bool IsServerSelected(string serverId)
    {
        McpCapabilitySelection.ValidateServer(serverId);
        RequireAvailable();
        return IsIncluded(directories.Load(), serverId);
    }

    internal static bool IsIncluded(ArtifactDirectorySettings settings, string serverId) =>
        settings.IncludedMcpServerIds.Contains(serverId, StringComparer.Ordinal)
        || string.Equals(serverId, KoraMcpCatalog.ServerId, StringComparison.Ordinal)
            && !settings.McpCapabilities.Categories.Any(category =>
                string.Equals(category.ServerId, serverId, StringComparison.Ordinal));

    public bool IsToolEnabled(string serverId, string toolId) =>
        IsServerEnabled(serverId) && directories.Load().McpCapabilities.IsEnabled(serverId, McpCapabilityKind.Tool, toolId);

    public bool IsToolSelected(string serverId, string toolId) =>
        !unavailable && directories.Load().McpCapabilities.IsEnabled(serverId, McpCapabilityKind.Tool, toolId);

    internal ModelContextEnvelope RestrictContext(ModelContextEnvelope context)
    {
        if (context.ToolMode == ModelToolMode.HostedClarification
            && !IsToolEnabled(KoraMcpCatalog.ServerId, ModelToolProtocol.AskUserToolName)
            || context.ToolMode == ModelToolMode.LocalWebPage
                && !IsToolEnabled(KoraMcpCatalog.ServerId, Kora.Core.Network.WebPageCapability.Id))
        { throw new InvalidOperationException("The selected Kora tool is disabled."); }
        if (context.McpTool is not null || context.WebPages is not null
            || context.ToolMode is ModelToolMode.None or ModelToolMode.HostedClarification)
        { return context; }
        var ids = context.Tools.Where(tool => IsToolEnabled(KoraMcpCatalog.ServerId, tool.Id))
            .Select(tool => tool.Id).ToImmutableArray();
        if (ids.Length == context.Tools.Count) { return context; }
        var prior = context.Constraints;
        var constraints = new ModelTurnConstraints(2, ids,
            prior?.MaximumCalls ?? ModelToolProtocol.MaximumCalls,
            prior?.MaximumRounds ?? ModelToolProtocol.MaximumRounds,
            prior?.InputUtf8Bytes ?? ModelToolProtocol.MaximumInputUtf8Bytes,
            prior?.OutputUtf8Bytes ?? ModelToolProtocol.MaximumOutputUtf8Bytes,
            prior?.DeadlineMilliseconds ?? (int)ModelToolProtocol.Deadline.TotalMilliseconds);
        return new(context.Request, context.SystemPolicy, context.UserRequest, context.Evidence,
            context.CreatedAt, context.ExpiresAt, context.ToolMode, constraints);
    }

    public void SetServerEnabled(string serverId, bool enabled)
    {
        McpCapabilitySelection.ValidateServer(serverId);
        RequireAvailable();
        try { configuration.SetMcpServerIncluded(serverId, enabled); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            unavailable = true;
            throw;
        }
    }

    public void SetToolEnabled(string serverId, string toolId, bool enabled)
    {
        McpToolSchema.ValidateName(toolId);
        RequireAvailable();
        try { configuration.SetMcpItemEnabled(serverId, McpCapabilityKind.Tool, toolId, enabled); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            unavailable = true;
            throw;
        }
    }

    public void SetCategoryEnabled(string serverId, McpCapabilityCategory category, bool enabled)
    {
        RequireAvailable();
        try { configuration.SetMcpCategoryEnabled(serverId, category, enabled); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            unavailable = true;
            throw;
        }
    }

    private void RequireAvailable()
    {
        if (unavailable) { throw new InvalidOperationException("MCP availability is unconfirmed. Restore verified preferences and restart."); }
    }

}

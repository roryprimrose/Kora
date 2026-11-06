using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kora.Core.Artifacts;

namespace Kora.Core.Mcp;

/// <summary>Device-local consideration preferences, never connection, disclosure or execution authority.</summary>
public sealed class McpCapabilitySelection
{
    private const int MaximumExternalCategories = 128 * 3;
    public const int MaximumCategories = MaximumExternalCategories + 3;
    public const int MaximumOverrides = 1024;
    public static McpCapabilitySelection Default { get; } = new([]);

    public McpCapabilitySelection(IEnumerable<McpCategorySelection> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);
        Categories = categories.Take(MaximumCategories + 1).ToImmutableArray();
        if (Categories.Length > MaximumCategories || Categories.Any(category => category is null)
            || Categories.Count(category => !string.Equals(category.ServerId, KoraMcpCatalog.ServerId, StringComparison.Ordinal)) > MaximumExternalCategories
            || Categories.Select(category => (category.ServerId, category.Category)).Distinct().Count() != Categories.Length
            || Categories.Sum(category => category.Items.Length) > MaximumOverrides)
        { throw new ArgumentException("MCP capability preferences require unique, bounded server categories.", nameof(categories)); }
    }

    public ImmutableArray<McpCategorySelection> Categories { get; }

    public bool MayConsider(string serverId, McpCapabilityCategory category)
    {
        ValidateServer(serverId);
        ValidateCategory(category);
        var saved = Categories.SingleOrDefault(item => string.Equals(item.ServerId, serverId, StringComparison.Ordinal)
            && item.Category == category);
        return saved is null || saved.Enabled || saved.Items.Any(item => item.Enabled);
    }

    public bool IsEnabled(string serverId, McpCapabilityKind kind, string identity)
    {
        ValidateServer(serverId);
        var category = Category(kind);
        var key = ItemKey(kind, identity);
        var saved = Categories.SingleOrDefault(item => string.Equals(item.ServerId, serverId, StringComparison.Ordinal) && item.Category == category);
        return saved?.Items.SingleOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal))?.Enabled ?? saved?.Enabled ?? true;
    }

    public McpCapabilitySelection SetCategory(string serverId, McpCapabilityCategory category, bool enabled)
    {
        ValidateServer(serverId);
        ValidateCategory(category);
        var revision = Categories.SingleOrDefault(item => string.Equals(item.ServerId, serverId, StringComparison.Ordinal)
            && item.Category == category)?.Revision ?? 0;
        if (revision == long.MaxValue) { throw new InvalidOperationException("The MCP category revision is exhausted."); }
        return new(Categories.Where(item => !string.Equals(item.ServerId, serverId, StringComparison.Ordinal) || item.Category != category)
            .Append(new(serverId, category, enabled, [], revision + 1)));
    }

    public McpCapabilitySelection SetItem(string serverId, McpCapabilityKind kind, string identity, bool enabled)
    {
        ValidateServer(serverId);
        var category = Category(kind);
        var key = ItemKey(kind, identity);
        var saved = Categories.SingleOrDefault(item => string.Equals(item.ServerId, serverId, StringComparison.Ordinal) && item.Category == category)
            ?? new McpCategorySelection(serverId, category, true, []);
        var items = saved.Items.Where(item => !string.Equals(item.Key, key, StringComparison.Ordinal));
        if (saved.Revision == long.MaxValue) { throw new InvalidOperationException("The MCP category revision is exhausted."); }
        if (enabled != saved.Enabled) { items = items.Append(new(key, enabled)); }
        return new(Categories.Where(item => !string.Equals(item.ServerId, serverId, StringComparison.Ordinal) || item.Category != category)
            .Append(new(serverId, category, saved.Enabled, items, saved.Revision + 1)));
    }

    public string Fingerprint(string serverId)
    {
        ValidateServer(serverId);
        return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            Categories.Where(item => string.Equals(item.ServerId, serverId, StringComparison.Ordinal)).OrderBy(item => item.Category)
                .Select(item => new { item.Category, item.Enabled, item.Revision,
                    Items = item.Items.OrderBy(value => value.Key, StringComparer.Ordinal) }))));
    }

    public static McpCapabilityCategory Category(McpCapabilityKind kind) => kind switch
    {
        McpCapabilityKind.Tool => McpCapabilityCategory.Tools,
        McpCapabilityKind.Resource or McpCapabilityKind.ResourceTemplate => McpCapabilityCategory.Resources,
        McpCapabilityKind.Prompt => McpCapabilityCategory.Prompts,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string ItemKey(McpCapabilityKind kind, string identity)
    {
        _ = Category(kind);
        if (string.IsNullOrWhiteSpace(identity) || identity.Length > 2048 || identity.Any(char.IsControl))
        { throw new ArgumentException("An MCP capability requires a bounded exact identity.", nameof(identity)); }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(kind + "\0" + identity)));
    }

    internal static void ValidateServer(string serverId)
    {
        ArgumentNullException.ThrowIfNull(serverId);
        if (string.Equals(serverId, KoraMcpCatalog.ServerId, StringComparison.Ordinal)) { return; }
        ValidateItemKey(serverId);
        _ = ArtifactUseIdentity.Mcp(serverId);
    }

    internal static void ValidateItemKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != 64 || key.Any(value => value is not (>= 'a' and <= 'f' or >= '0' and <= '9')))
        { throw new ArgumentException("Use an exact source-qualified MCP identity.", nameof(key)); }
    }

    internal static void ValidateCategory(McpCapabilityCategory category)
    {
        if (category is not (McpCapabilityCategory.Tools or McpCapabilityCategory.Resources or McpCapabilityCategory.Prompts))
        { throw new ArgumentOutOfRangeException(nameof(category)); }
    }
}

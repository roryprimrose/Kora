namespace Kora.Core.Mcp;

public sealed record McpItemSelection
{
    public McpItemSelection(string key, bool enabled)
    {
        McpCapabilitySelection.ValidateItemKey(key);
        Key = key;
        Enabled = enabled;
    }

    public string Key { get; }
    public bool Enabled { get; }
}

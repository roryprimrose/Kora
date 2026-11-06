namespace Kora;

internal static class CustomizationMetadataMatch
{
    internal static bool Matches(string? query, string label, string identity, string scope)
    {
        var filter = query?.Trim() ?? string.Empty;
        return label.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || identity.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || scope.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}
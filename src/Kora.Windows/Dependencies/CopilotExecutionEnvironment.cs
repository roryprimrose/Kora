namespace Kora.Windows.Dependencies;

internal static class CopilotExecutionEnvironment
{
    internal static IReadOnlyDictionary<string, string> Copy(IReadOnlyDictionary<string, string> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (environment.Any(item => item.Key is not ("SystemRoot" or "WINDIR" or "TEMP" or "TMP")
            || string.IsNullOrWhiteSpace(item.Value) || item.Value.Length > 4096))
        { throw new InvalidDataException("Copilot environment may contain only explicit bounded operating-system paths."); }
        return new Dictionary<string, string>(environment, StringComparer.OrdinalIgnoreCase);
    }
}

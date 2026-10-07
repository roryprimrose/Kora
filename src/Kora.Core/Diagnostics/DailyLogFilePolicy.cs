using System.Globalization;

namespace Kora.Core.Diagnostics;

public static class DailyLogFilePolicy
{
    public const string DirectoryName = "Logs";
    public const string Pattern = "kora-*.log";
    public const string RollingName = "kora-.log";

    public static bool TryParseName(string fileName, out DateOnly date)
    {
        if (fileName.Length != 17 || !fileName.StartsWith("kora-", StringComparison.Ordinal)
            || !fileName.EndsWith(".log", StringComparison.Ordinal))
        {
            date = default;
            return false;
        }
        return DateOnly.TryParseExact(fileName.AsSpan(5, 8), "yyyyMMdd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}

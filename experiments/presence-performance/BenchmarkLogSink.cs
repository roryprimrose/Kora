using System.Globalization;

using Avalonia.Logging;

namespace Presence.Performance;

internal sealed class BenchmarkLogSink : ILogSink
{
    public bool IsEnabled(LogEventLevel level, string area) =>
        level >= LogEventLevel.Warning
        || area.Contains("GL", StringComparison.OrdinalIgnoreCase)
        || area.Contains("Win32", StringComparison.OrdinalIgnoreCase)
        || area.Contains("Vulkan", StringComparison.OrdinalIgnoreCase);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
        Log(level, area, source, messageTemplate, []);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        var line = $"{level} [{area}] {messageTemplate} | "
            + string.Join("; ", propertyValues.Select(value => Convert.ToString(value, CultureInfo.InvariantCulture)));
        Program.GraphicsLog.Add(line);
        Console.WriteLine(line);
    }
}

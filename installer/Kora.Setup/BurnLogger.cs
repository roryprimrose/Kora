using Microsoft.Extensions.Logging;

namespace Kora.Setup;

internal sealed class BurnLogger<T>(Action<LogLevel, string> log) : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel))
        {
            log(logLevel, formatter(state, exception));
        }
    }
}

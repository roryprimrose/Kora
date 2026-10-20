using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Network;

internal sealed class EnabledLogger<T> : ILogger<T>
{
    internal List<string> Messages { get; } = [];
    public bool IsEnabled(LogLevel logLevel) => true;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
}

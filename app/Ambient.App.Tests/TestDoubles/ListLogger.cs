using Microsoft.Extensions.Logging;

namespace Ambient.App.Tests.TestDoubles;

/// <summary>Keeps every line logged, formatted, for a test to read.</summary>
internal sealed class ListLogger : ILogger
{
    public List<string> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Lines.Add($"{logLevel}: {formatter(state, exception)}");
}

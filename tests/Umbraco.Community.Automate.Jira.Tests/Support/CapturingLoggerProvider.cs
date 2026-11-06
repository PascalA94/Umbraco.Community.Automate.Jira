using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Umbraco.Community.Automate.Jira.Tests.Support;

/// <summary>
/// Collects every formatted line, scope-free, plus exception text, at <see cref="LogLevel.Trace"/> and above.
/// The secret-leak tests search these lines, so exceptions are captured in full (<c>ToString()</c>).
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyList<string> Lines => _lines.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _lines);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var line = $"{logLevel} {category}: {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            lines.Enqueue(line);
        }
    }
}

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PolvorApp.Api.Tests.Infrastructure;

public sealed record CapturedLog(
    string Category,
    LogLevel Level,
    string Message,
    IReadOnlyDictionary<string, object?> State,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Scopes,
    string? Exception,
    Exception? Thrown = null);

/// <summary>Collects every log entry, including structured state and active scopes, for assertions.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public IReadOnlyCollection<CapturedLog> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    private static Dictionary<string, object?> ToDictionary(object? state) =>
        state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? pairs.ToDictionary(p => p.Key, p => p.Value)
            : new Dictionary<string, object?> { ["Scope"] = state?.ToString() };

    private sealed class CapturingLogger(string category, CapturingLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => provider._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var scopes = new List<IReadOnlyDictionary<string, object?>>();
            provider._scopes.ForEachScope((scope, list) => list.Add(ToDictionary(scope)), scopes);
            provider._entries.Enqueue(new CapturedLog(
                category, logLevel, formatter(state, exception), ToDictionary(state), scopes, exception?.ToString(), exception));
        }
    }
}

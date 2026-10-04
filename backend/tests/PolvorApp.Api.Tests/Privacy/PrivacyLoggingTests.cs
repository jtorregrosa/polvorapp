using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PolvorApp.Api.Tests.Infrastructure;

namespace PolvorApp.Api.Tests.Privacy;

/// <summary>
/// NFR-12 and spec audit-privacy "Looking up a person": a GDPR request never writes the DNI/NIE, the
/// request reference or the person's name to the logs, whatever it finds (review, task 9.2).
/// </summary>
public sealed class PrivacyLoggingTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private const string Reference = "REQ-LOGS-2030";
    private readonly CapturingLoggerProvider _logs = new();
    private RegistryTestHost _registry = null!;

    public async ValueTask InitializeAsync() =>
        _registry = await RegistryTestHost.StartAsync(postgres, mailpit, services => services.AddSingleton<ILoggerProvider>(_logs));

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task Lookups_exports_and_erasures_log_no_identity_nor_reference()
    {
        var person = await _registry.RegisterAsync(_registry.Own.Id);
        var nationalId = person.GetProperty("nationalId").GetString()!;
        var lastName = person.GetProperty("lastName").GetString()!;
        var unknown = RegistryData.NextIdentity().NationalId;

        foreach (var (path, body) in new (string, object)[]
        {
            ("/api/privacy/people/lookup", new { nationalId, reference = Reference }),
            ("/api/privacy/people/lookup", new { nationalId = unknown }),
            ("/api/privacy/people/export", new { nationalId, reference = Reference }),
            ("/api/privacy/people/erasure", new { nationalId, reference = Reference }),
            ("/api/privacy/people/erasure", new { nationalId, reference = Reference }),
        })
        {
            using var response = await _registry.Admin.PostAsJsonAsync(path, body, TestContext.Current.CancellationToken);
            Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound, $"{path}: {response.StatusCode}");
        }

        Assert.NotEmpty(_logs.Messages);
        Assert.DoesNotContain(_logs.Messages, message =>
            message.Contains(nationalId, StringComparison.OrdinalIgnoreCase)
            || message.Contains(unknown, StringComparison.OrdinalIgnoreCase)
            || message.Contains(Reference, StringComparison.Ordinal)
            || message.Contains(lastName, StringComparison.Ordinal));
    }

    /// <summary>Keeps every formatted log message, with its exception, of every category.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue($"{formatter(state, exception)} {exception}");
        }
    }
}

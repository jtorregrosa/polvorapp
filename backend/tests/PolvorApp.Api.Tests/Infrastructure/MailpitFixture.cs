using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

[assembly: AssemblyFixture(typeof(PolvorApp.Api.Tests.Infrastructure.MailpitFixture))]

namespace PolvorApp.Api.Tests.Infrastructure;

/// <summary>
/// A Mailpit container (the local mail catcher) shared by the test assembly. Messages are found by
/// recipient and test classes run in parallel: an address whose mail a test reads or counts must
/// not receive mail from any other test class.
/// </summary>
public sealed class MailpitFixture : IAsyncLifetime
{
    private const int SmtpPort = 1025;
    private const int ApiPort = 8025;

    private readonly IContainer _container = new ContainerBuilder("axllent/mailpit:v1.31.3@sha256:ed9b00c609e77e99c79b93f1178255ebc271868920f2c69a8d166bd5634ed10d")
        .WithPortBinding(SmtpPort, true)
        .WithPortBinding(ApiPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(ApiPort).ForPath("/readyz")))
        .Build();

    private HttpClient? _api;

    public string SmtpHost => _container.Hostname;

    public int SmtpPortOnHost => _container.GetMappedPublicPort(SmtpPort);

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        _api = new HttpClient { BaseAddress = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(ApiPort)}") };
    }

    public async ValueTask DisposeAsync()
    {
        _api?.Dispose();
        await _container.DisposeAsync();
    }

    /// <summary>SMTP settings for an <see cref="ApiFactory"/> that delivers to this container.</summary>
    public IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Email:SmtpHost"] = SmtpHost,
        ["Email:SmtpPort"] = SmtpPortOnHost.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Email:Security"] = "None",
    };

    /// <summary>Waits until a message to <paramref name="address"/> arrives and returns it.</summary>
    public async Task<MailpitMessage> WaitForMessageAsync(string address, Func<MailpitMessage, bool>? match = null)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var search = await Api.GetFromJsonAsync<MailpitSearch>(SearchPath(address), cancellationToken);
                foreach (var summary in search?.Messages ?? [])
                {
                    var message = await Api.GetFromJsonAsync<MailpitMessage>($"/api/v1/message/{summary.Id}", cancellationToken);
                    if (message is not null && (match is null || match(message)))
                    {
                        return message;
                    }
                }
            }
            catch (HttpRequestException)
            {
                // Transient: retry until the deadline.
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException($"No matching email to {address} arrived in Mailpit.");
    }

    /// <summary>Number of messages sent to <paramref name="address"/> so far.</summary>
    public async Task<int> CountMessagesAsync(string address)
    {
        var search = await Api.GetFromJsonAsync<MailpitSearch>(SearchPath(address), TestContext.Current.CancellationToken);
        return search?.Messages.Count ?? 0;
    }

    private static string SearchPath(string address) => $"/api/v1/search?query={Uri.EscapeDataString($"to:\"{address}\"")}";

    private HttpClient Api => _api ?? throw new InvalidOperationException("Mailpit is not started.");

    private sealed record MailpitSearch(
        [property: JsonPropertyName("messages")] IReadOnlyList<MailpitSummary> Messages);

    private sealed record MailpitSummary([property: JsonPropertyName("ID")] string Id);
}

public sealed record MailpitMessage(
    [property: JsonPropertyName("Subject")] string Subject,
    [property: JsonPropertyName("Text")] string Text,
    [property: JsonPropertyName("HTML")] string Html);

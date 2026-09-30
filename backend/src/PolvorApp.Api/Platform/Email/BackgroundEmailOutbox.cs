using System.Threading.Channels;
using PolvorApp.SharedKernel.Email;

namespace PolvorApp.Api.Platform.Email;

/// <summary>
/// In-memory outbox drained by a background service (design D9). Bounded: when full, the oldest
/// message is dropped with a warning rather than blocking requests. On shutdown, messages still
/// waiting are attempted for a few seconds.
/// </summary>
internal sealed partial class BackgroundEmailOutbox : BackgroundService, IEmailOutbox
{
    private const int Capacity = 500;
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(5);

    private readonly IEmailSender _sender;
    private readonly ILogger<BackgroundEmailOutbox> _logger;
    private readonly Channel<EmailMessage> _messages;
    private int _pending;

    public BackgroundEmailOutbox(IEmailSender sender, ILogger<BackgroundEmailOutbox> logger)
    {
        _sender = sender;
        _logger = logger;
        _messages = Channel.CreateBounded<EmailMessage>(
            new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
            dropped =>
            {
                Interlocked.Decrement(ref _pending);
                LogDropped(_logger, dropped.Template);
            });
    }

    public void Enqueue(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Interlocked.Increment(ref _pending);
        if (!_messages.Writer.TryWrite(message))
        {
            Interlocked.Decrement(ref _pending);
            LogDropped(_logger, message.Template);
        }
    }

    /// <summary>Waits until every queued message has been sent or has failed (tests, shutdown).</summary>
    public async Task DrainAsync(CancellationToken cancellationToken)
    {
        while (Volatile.Read(ref _pending) > 0)
        {
            await Task.Delay(20, cancellationToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _messages.Writer.TryComplete();
        using var grace = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        grace.CancelAfter(ShutdownGrace);
        try
        {
            await DrainAsync(grace.Token);
        }
        catch (OperationCanceledException)
        {
            LogUnsentAtShutdown(_logger, Volatile.Read(ref _pending));
        }

        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in _messages.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await _sender.SendAsync(message, CancellationToken.None);
            }
            catch (EmailDeliveryException)
            {
                // The sender already logged the template, phase and error code.
            }
            catch (Exception exception)
            {
                LogFailed(_logger, message.Template, exception.GetType().Name);
            }
            finally
            {
                Interlocked.Decrement(ref _pending);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Queued email {Template} was dropped because the outbox is full")]
    private static partial void LogDropped(ILogger logger, string template);

    [LoggerMessage(Level = LogLevel.Error, Message = "Queued email {Template} failed unexpectedly ({ErrorType})")]
    private static partial void LogFailed(ILogger logger, string template, string errorType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Count} queued emails were not sent before shutdown")]
    private static partial void LogUnsentAtShutdown(ILogger logger, int count);
}

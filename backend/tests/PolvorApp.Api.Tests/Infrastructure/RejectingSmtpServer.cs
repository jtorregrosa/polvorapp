using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PolvorApp.Api.Tests.Infrastructure;

/// <summary>
/// A minimal SMTP server on a free loopback port that accepts the session but rejects every
/// recipient with 550, quoting the address the way real servers do.
/// </summary>
public sealed class RejectingSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _serving;

    public RejectingSmtpServer()
    {
        _listener.Start();
        _serving = ServeAsync(_stop.Token);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>A loopback port with nothing listening on it.</summary>
    public static int ClosedPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _serving;
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // Stopping the listener ends the accept loop.
        }

        _stop.Dispose();
    }

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 fake.example ESMTP");
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                var command = line.Split(' ', 2)[0].ToUpperInvariant();
                if (command == "QUIT")
                {
                    await writer.WriteLineAsync("221 bye");
                    break;
                }

                await writer.WriteLineAsync(command switch
                {
                    "EHLO" or "HELO" => "250 fake.example",
                    "MAIL" or "RSET" or "NOOP" => "250 ok",
                    "RCPT" => $"550 5.1.1 <{line.Split(':', 2)[^1].Trim(' ', '<', '>')}> mailbox unavailable",
                    _ => "502 not implemented",
                });
            }
        }
    }
}

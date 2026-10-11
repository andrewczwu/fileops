using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FileOps.Integration.Tests.Support;

/// <summary>One OTLP/HTTP request as the receiver saw it.</summary>
internal sealed record ReceivedRequest(string Path, string? ContentType, byte[] Body)
{
    /// <summary>
    /// Whether <paramref name="text"/> appears in the body. Protobuf stores strings as raw UTF-8, so
    /// a span name, attribute key or attribute value can be found without decoding the payload.
    /// </summary>
    public bool Contains(string text) => Body.AsSpan().IndexOf(Encoding.UTF8.GetBytes(text)) >= 0;
}

/// <summary>
/// A minimal OTLP/HTTP endpoint for integration tests. It listens on a free loopback port, records
/// each request's path, content type and body, and answers an empty 200. That is enough to prove the
/// real executable exports spans, metrics and log records to the endpoint it was given, without a
/// collector or protobuf dependencies. Plain sockets rather than <see cref="HttpListener"/>: binding
/// port 0 has no race, and teardown cannot fail.
/// </summary>
internal sealed class OtlpReceiver : IDisposable
{
    private static readonly byte[] HeaderEnd = "\r\n\r\n"u8.ToArray();
    private static readonly byte[] Response = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray();

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _serving;
    private readonly Lock _lock = new();
    private readonly List<ReceivedRequest> _requests = [];

    public OtlpReceiver()
    {
        _listener.Start();
        Endpoint = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
        _serving = Task.Run(ServeAsync);
    }

    /// <summary><c>http://127.0.0.1:&lt;port&gt;</c>, for <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>.</summary>
    public string Endpoint { get; }

    /// <summary>
    /// Environment that sends a run's telemetry here over OTLP/HTTP. The protocol must be set because
    /// the .NET exporter defaults to gRPC.
    /// </summary>
    public Dictionary<string, string?> Environment() => new()
    {
        ["OTEL_EXPORTER_OTLP_ENDPOINT"] = Endpoint,
        ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
        ["OTEL_EXPORTER_OTLP_TIMEOUT"] = "5000",
    };

    /// <summary>
    /// Everything received so far. The exporter waits for each response before the process exits, and
    /// a request is recorded before it is answered, so after the child exits this is complete.
    /// </summary>
    public IReadOnlyList<ReceivedRequest> Received
    {
        get
        {
            lock (_lock)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>Requests to one signal's path, for example <c>/v1/traces</c>.</summary>
    public IReadOnlyList<ReceivedRequest> For(string signalPath) =>
        [.. Received.Where(r => r.Path.EndsWith(signalPath, StringComparison.Ordinal))];

    public void Dispose()
    {
        _stopping.Cancel();
        _listener.Stop();
        try
        {
            _serving.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Cancellation surfaces here; nothing is left to clean up.
        }

        _stopping.Dispose();
    }

    private async Task ServeAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stopping.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return; // stopped by Dispose
            }

            using (client)
            {
                await HandleAsync(client.GetStream()).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Reads one request with a Content-Length body, records it, and answers with Connection: close so
    /// the exporter opens a fresh connection for its next request.
    /// </summary>
    private async Task HandleAsync(NetworkStream stream)
    {
        var buffer = new byte[64 * 1024];
        using var raw = new MemoryStream();
        int headerEnd;
        do
        {
            var read = await stream.ReadAsync(buffer, _stopping.Token).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            raw.Write(buffer, 0, read);
            headerEnd = raw.GetBuffer().AsSpan(0, (int)raw.Length).IndexOf(HeaderEnd);
        }
        while (headerEnd < 0);

        var headerLines = Encoding.ASCII.GetString(raw.GetBuffer(), 0, headerEnd).Split("\r\n");
        var path = headerLines[0].Split(' ')[1];
        var headers = headerLines.Skip(1)
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
        var length = headers.TryGetValue("Content-Length", out var value) ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : 0;

        var bodyStart = headerEnd + HeaderEnd.Length;
        while (raw.Length - bodyStart < length)
        {
            var read = await stream.ReadAsync(buffer, _stopping.Token).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            raw.Write(buffer, 0, read);
        }

        var body = raw.GetBuffer().AsSpan(bodyStart, length).ToArray();
        lock (_lock)
        {
            _requests.Add(new ReceivedRequest(path, headers.GetValueOrDefault("Content-Type"), body));
        }

        await stream.WriteAsync(Response, _stopping.Token).ConfigureAwait(false);
    }
}

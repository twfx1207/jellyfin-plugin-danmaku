using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Jellyfin.Plugin.Danmaku.Models;
using Jellyfin.Plugin.Danmaku.Services;
using Microsoft.Extensions.Logging.Abstractions;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Working IPv6 does not open IPv4", Ipv6Wins),
    ("Stalled IPv6 falls back to IPv4 and cancels the loser", StalledIpv6),
    ("An immediate IPv6 failure falls back without waiting", FailedIpv6),
    ("A failed IPv4 attempt does not cancel working IPv6", FailedIpv4),
    ("A late successful loser is disposed", LateLoser),
    ("Both failed families propagate a connection error", BothFail),
    ("Caller cancellation cancels both connection attempts", CancelConnection),
    ("Real IPv4 socket carries a POST exactly once", RealIpv4Post),
    ("Request timeout becomes a source error", HeaderTimeout),
    ("Response body uses the same request deadline", BodyTimeout),
    ("Request cancellation remains caller cancellation", CancelRequest),
    ("Authentication errors preserve the upstream message", AuthenticationFailure)
};

foreach (var (name, run) in tests)
{
    await run().WaitAsync(TimeSpan.FromSeconds(10));
    Console.WriteLine("PASS " + name);
}

Console.WriteLine($"Passed {tests.Length} network tests.");

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static async Task<TException> Throws<TException>(Func<Task> action) where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException exception)
    {
        return exception;
    }

    throw new InvalidOperationException("Expected " + typeof(TException).Name);
}

static async Task Ipv6Wins()
{
    var ipv4Calls = 0;
    using var expected = new TrackedStream();
    using var actual = await HappyEyeballsConnector.ConnectWithFallbackAsync((family, _) =>
    {
        if (family == AddressFamily.InterNetwork)
        {
            ipv4Calls++;
        }

        return Task.FromResult<Stream>(expected);
    }, CancellationToken.None);
    Assert(ReferenceEquals(actual, expected) && ipv4Calls == 0, "Fast IPv6 should win alone.");
    Assert(!expected.Disposed, "The winning stream must remain open.");
}

static async Task StalledIpv6()
{
    var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var clock = Stopwatch.StartNew();
    using var result = await HappyEyeballsConnector.ConnectWithFallbackAsync(async (family, token) =>
    {
        if (family == AddressFamily.InterNetworkV6)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                throw;
            }
        }

        return new TrackedStream();
    }, CancellationToken.None);
    await cancelled.Task;
    Assert(clock.Elapsed < TimeSpan.FromSeconds(2), "IPv4 waited for the IPv6 timeout.");
}

static async Task FailedIpv6()
{
    var clock = Stopwatch.StartNew();
    using var result = await HappyEyeballsConnector.ConnectWithFallbackAsync((family, _) =>
        family == AddressFamily.InterNetworkV6
            ? throw new SocketException((int)SocketError.NetworkUnreachable)
            : Task.FromResult<Stream>(new TrackedStream()), CancellationToken.None);
    Assert(clock.Elapsed < TimeSpan.FromMilliseconds(200), "A failed IPv6 attempt waited unnecessarily.");
}

static async Task FailedIpv4()
{
    using var expected = new TrackedStream();
    using var result = await HappyEyeballsConnector.ConnectWithFallbackAsync(async (family, token) =>
    {
        if (family == AddressFamily.InterNetwork)
        {
            throw new SocketException((int)SocketError.ConnectionRefused);
        }

        await Task.Delay(400, token);
        return expected;
    }, CancellationToken.None);
    Assert(ReferenceEquals(expected, result), "IPv4 failure cancelled a valid IPv6 connection.");
}

static async Task LateLoser()
{
    using var loser = new TrackedStream();
    var release = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var result = await HappyEyeballsConnector.ConnectWithFallbackAsync((family, _) =>
        family == AddressFamily.InterNetworkV6
            ? release.Task
            : Task.FromResult<Stream>(new TrackedStream()), CancellationToken.None);
    release.SetResult(loser);
    await loser.WhenDisposed.Task;
    Assert(loser.Disposed, "An attempt finishing after cancellation leaked a stream.");
}

static async Task BothFail()
{
    await Throws<SocketException>(async () =>
        await HappyEyeballsConnector.ConnectWithFallbackAsync((_, _) =>
            Task.FromException<Stream>(new SocketException((int)SocketError.ConnectionRefused)), CancellationToken.None));
}

static async Task CancelConnection()
{
    var started = 0;
    var cancelled = 0;
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(450));
    await Throws<OperationCanceledException>(async () =>
        await HappyEyeballsConnector.ConnectWithFallbackAsync(async (_, token) =>
        {
            Interlocked.Increment(ref started);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new TrackedStream();
            }
            finally
            {
                Interlocked.Increment(ref cancelled);
            }
        }, cancellation.Token));
    for (var i = 0; i < 50 && cancelled != 2; i++)
    {
        await Task.Delay(10);
    }

    Assert(started == 2 && cancelled == 2, "Cancellation did not stop both families.");
}

static async Task RealIpv4Post()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var requests = 0;
    var server = Task.Run(async () =>
    {
        using var socket = await listener.AcceptTcpClientAsync();
        await using var stream = socket.GetStream();
        using var reader = new StreamReader(stream, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync();
        Assert(requestLine == "POST /api/v2/match HTTP/1.1", "The HTTP request changed.");
        var length = 0;
        while (await reader.ReadLineAsync() is { Length: > 0 } header)
        {
            if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                length = int.Parse(header[15..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        var body = new char[length];
        await reader.ReadBlockAsync(body);
        Assert(new string(body) == "test-body", "The POST body was not preserved.");
        Interlocked.Increment(ref requests);
        var response = System.Text.Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}");
        await stream.WriteAsync(response);
    });
    using var handler = DanmakuHttpTransport.CreateHandler();
    handler.UseProxy = false;
    using var client = new HttpClient(handler);
    using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/api/v2/match")
    {
        Content = new StringContent("test-body")
    };
    var responseBody = await DanmakuHttpTransport.SendAsync(
        client, request, "弹弹play", NullLogger.Instance, CancellationToken.None);
    await server;
    Assert(requests == 1 && responseBody.ValueKind == System.Text.Json.JsonValueKind.Object,
        "Fallback submitted the POST more than once or lost its response.");
}

static async Task HeaderTimeout()
{
    using var client = new HttpClient(new FakeHandler(async token =>
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        return new HttpResponseMessage(HttpStatusCode.OK);
    }));
    using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/api/v2/search/episodes?secret=hidden");
    var error = await Throws<DanmakuSourceException>(async () => await DanmakuHttpTransport.SendAsync(
        client, request, "弹弹play", NullLogger.Instance, CancellationToken.None, TimeSpan.FromMilliseconds(100)));
    Assert(error.Message.Contains("请求超时", StringComparison.Ordinal), "Timeout lost its useful error message.");
    Assert(!error.Message.Contains("secret", StringComparison.Ordinal), "Timeout leaked query values.");
}

static async Task BodyTimeout()
{
    using var client = new HttpClient(new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new SlowContent()
    })));
    using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/api/v2/match");
    await Throws<DanmakuSourceException>(async () => await DanmakuHttpTransport.SendAsync(
        client, request, "弹弹play", NullLogger.Instance, CancellationToken.None, TimeSpan.FromMilliseconds(100)));
}

static async Task CancelRequest()
{
    using var client = new HttpClient(new FakeHandler(async token =>
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        return new HttpResponseMessage(HttpStatusCode.OK);
    }));
    using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
    await Throws<OperationCanceledException>(async () => await DanmakuHttpTransport.SendAsync(
        client, request, "弹弹play", NullLogger.Instance, cancellation.Token));
}

static async Task AuthenticationFailure()
{
    using var client = new HttpClient(new FakeHandler(_ =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.Add("X-Error-Message", "Invalid Signature");
        return Task.FromResult(response);
    }));
    using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
    var error = await Throws<DanmakuSourceException>(async () => await DanmakuHttpTransport.SendAsync(
        client, request, "弹弹play", NullLogger.Instance, CancellationToken.None));
    Assert(error.Message.Contains("403", StringComparison.Ordinal)
        && error.Message.Contains("Invalid Signature", StringComparison.Ordinal), "Authentication error was obscured.");
}

sealed class TrackedStream : MemoryStream
{
    public bool Disposed { get; private set; }

    public TaskCompletionSource WhenDisposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
        WhenDisposed.TrySetResult();
    }
}

sealed class FakeHandler(Func<CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => send(cancellationToken);
}

sealed class SlowContent : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}

using System.Net;
using System.Net.Sockets;

namespace Jellyfin.Plugin.Danmaku.Services;

internal static class HappyEyeballsConnector
{
    private static readonly TimeSpan FallbackDelay = TimeSpan.FromMilliseconds(250);

    public static ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        return new ValueTask<Stream>(ConnectWithFallbackAsync(
            (family, token) => ConnectSocketAsync(context.DnsEndPoint, family, token),
            cancellationToken));
    }

    internal static async Task<Stream> ConnectWithFallbackAsync(
        Func<AddressFamily, CancellationToken, Task<Stream>> connect,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var attempts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ipv6 = StartConnectionAsync(connect, AddressFamily.InterNetworkV6, attempts.Token);
        Task<Stream>? ipv4 = null;
        Task<Stream>? winner = null;
        try
        {
            var delay = Task.Delay(FallbackDelay, attempts.Token);
            if (await Task.WhenAny(ipv6, delay).ConfigureAwait(false) == ipv6 && ipv6.IsCompletedSuccessfully)
            {
                cancellationToken.ThrowIfCancellationRequested();
                winner = ipv6;
                return await ipv6.ConfigureAwait(false);
            }

            // A failed IPv6 attempt starts IPv4 immediately; a stalled one gets a 250 ms head start.
            cancellationToken.ThrowIfCancellationRequested();
            ipv4 = StartConnectionAsync(connect, AddressFamily.InterNetwork, attempts.Token);
            var first = await Task.WhenAny(ipv6, ipv4).ConfigureAwait(false);
            var selected = first.IsCompletedSuccessfully ? first : (first == ipv6 ? ipv4 : ipv6);
            if (!first.IsCompletedSuccessfully)
            {
                // Observe the failed attempt while the other family is still allowed to succeed.
                _ = first.Exception;
            }

            var stream = await selected.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            winner = selected;
            return stream;
        }
        finally
        {
            await attempts.CancelAsync().ConfigureAwait(false);
            if (ipv6 != winner)
            {
                _ = DisposeUnusedConnectionAsync(ipv6);
            }

            if (ipv4 is not null && ipv4 != winner)
            {
                _ = DisposeUnusedConnectionAsync(ipv4);
            }
        }
    }

    private static async Task<Stream> StartConnectionAsync(
        Func<AddressFamily, CancellationToken, Task<Stream>> connect,
        AddressFamily family,
        CancellationToken cancellationToken)
    {
        // Also capture synchronous failures from a connection attempt in its task.
        return await connect(family, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Stream> ConnectSocketAsync(
        DnsEndPoint endpoint,
        AddressFamily family,
        CancellationToken cancellationToken)
    {
        var socket = new Socket(family, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task DisposeUnusedConnectionAsync(Task<Stream> connection)
    {
        try
        {
            var stream = await connection.ConfigureAwait(false);
            await stream.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Losing attempts can fail or be cancelled after the winning stream has been returned.
        }
    }
}

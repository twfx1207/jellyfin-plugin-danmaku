using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Jellyfin.Plugin.Danmaku.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Danmaku.Services;

internal static partial class DanmakuHttpTransport
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    public static SocketsHttpHandler CreateHandler()
    {
        return new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = HappyEyeballsConnector.ConnectAsync
        };
    }

    public static async Task<JsonElement> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        string sourceName,
        ILogger logger,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        var deadline = timeout ?? RequestTimeout;
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(deadline);
        var host = request.RequestUri?.Host ?? string.Empty;
        // Custom URL paths can contain private access tokens. Never log them or any query/header values.
        var path = string.Equals(sourceName, "弹弹play", StringComparison.Ordinal)
            ? request.RequestUri?.AbsolutePath ?? string.Empty
            : "/<custom>";
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token).ConfigureAwait(false);
            if (logger.IsEnabled(LogLevel.Debug))
            {
                LogResponse(logger, sourceName, request.Method.Method, host, path,
                    (int)response.StatusCode, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            if (!response.IsSuccessStatusCode)
            {
                var upstreamMessage = response.Headers.TryGetValues("X-Error-Message", out var messages)
                    ? messages.FirstOrDefault()
                    : null;
                throw new DanmakuSourceException(
                    $"{sourceName}请求失败 ({(int)response.StatusCode}): {upstreamMessage ?? response.ReasonPhrase}");
            }

            // HttpClient.Timeout stops at response headers in this mode, so retain our deadline for the body.
            var payload = await response.Content.ReadAsStringAsync(timeoutSource.Token).ConfigureAwait(false);
            try
            {
                using var document = JsonDocument.Parse(payload);
                return document.RootElement.Clone();
            }
            catch (JsonException exception)
            {
                throw new DanmakuSourceException($"{sourceName}返回了无法解析的数据。", exception);
            }
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogTimeout(logger, sourceName, request.Method.Method, host, path,
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw new DanmakuSourceException(
                $"{sourceName}请求超时：{host}{path}。请检查服务端网络或稍后重试。", exception);
        }
        catch (HttpRequestException exception)
        {
            LogNetworkFailure(logger, sourceName, request.Method.Method, host, path, exception.HttpRequestError);
            throw new DanmakuSourceException(
                $"{sourceName}连接失败：{host}{path}（{exception.HttpRequestError}）。请检查服务端网络。", exception);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "{Source} {Method} {Host}{Path} returned HTTP {StatusCode} after {ElapsedMs} ms")]
    private static partial void LogResponse(
        ILogger logger, string source, string method, string host, string path, int statusCode, long elapsedMs);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "{Source} {Method} {Host}{Path} timed out after {ElapsedMs} ms (IPv4/IPv6 fallback enabled)")]
    private static partial void LogTimeout(
        ILogger logger, string source, string method, string host, string path, long elapsedMs);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
        Message = "{Source} {Method} {Host}{Path} failed: {Error} (IPv4/IPv6 fallback enabled)")]
    private static partial void LogNetworkFailure(
        ILogger logger, string source, string method, string host, string path, HttpRequestError error);
}

using System.Text;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Danmaku.Services;

internal sealed partial class WebClientInjector : IHostedService
{
    private const string StartMarker = "<!-- Jellyfin.Plugin.Danmaku:start -->";
    private const string EndMarker = "<!-- Jellyfin.Plugin.Danmaku:end -->";
    private static readonly string ClientVersion = typeof(WebClientInjector).Assembly.GetName().Version?.ToString() ?? "unknown";
    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<WebClientInjector> _logger;

    public WebClientInjector(IApplicationPaths applicationPaths, ILogger<WebClientInjector> logger)
    {
        _applicationPaths = applicationPaths;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var indexPath = Path.Combine(_applicationPaths.WebPath, "index.html");
        try
        {
            if (!File.Exists(indexPath))
            {
                LogIndexMissing(indexPath);
                return;
            }

            var original = await File.ReadAllTextAsync(indexPath, cancellationToken).ConfigureAwait(false);
            var withoutOldBlock = RemoveBlock(original);
            var bodyEnd = withoutOldBlock.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (bodyEnd < 0)
            {
                LogBodyMissing(indexPath);
                return;
            }

            var newline = withoutOldBlock.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var scriptTag = "<script data-plugin=\"Jellyfin.Plugin.Danmaku\" data-version=\""
                + ClientVersion
                + "\" src=\"../Danmaku/client.js?v="
                + ClientVersion
                + "\" defer></script>";
            var block = StartMarker + newline + scriptTag + newline + EndMarker + newline;
            var updated = withoutOldBlock.Insert(bodyEnd, block);
            if (!string.Equals(original, updated, StringComparison.Ordinal))
            {
                await WriteAtomicallyAsync(indexPath, updated, cancellationToken).ConfigureAwait(false);
                LogInjected(ClientVersion, indexPath);
            }
            else
            {
                LogInjectionCurrent(ClientVersion, indexPath);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogInjectionFailure(exception, indexPath);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public static bool Remove(string webPath)
    {
        var indexPath = Path.Combine(webPath, "index.html");
        try
        {
            if (!File.Exists(indexPath))
            {
                return true;
            }

            var original = File.ReadAllText(indexPath);
            var updated = RemoveBlock(original);
            if (!string.Equals(original, updated, StringComparison.Ordinal))
            {
                WriteAtomically(indexPath, updated);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string RemoveBlock(string html)
    {
        while (true)
        {
            var start = html.IndexOf(StartMarker, StringComparison.Ordinal);
            if (start < 0)
            {
                return html;
            }

            var end = html.IndexOf(EndMarker, start, StringComparison.Ordinal);
            if (end < 0)
            {
                return html;
            }

            end += EndMarker.Length;
            while (end < html.Length && (html[end] == '\r' || html[end] == '\n'))
            {
                end++;
            }

            html = html.Remove(start, end - start);
        }
    }

    private static void WriteAtomically(string path, string contents)
    {
        var temporaryPath = path + ".danmaku.tmp";
        try
        {
            File.WriteAllText(temporaryPath, contents, new UTF8Encoding(false));
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task WriteAtomicallyAsync(
        string path,
        string contents,
        CancellationToken cancellationToken)
    {
        var temporaryPath = path + ".danmaku.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                contents,
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Danmaku web injection skipped because {Path} does not exist")]
    private partial void LogIndexMissing(string path);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Danmaku web injection skipped because {Path} has no closing body element")]
    private partial void LogBodyMissing(string path);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "Injected Danmaku web client {Version} into {Path}")]
    private partial void LogInjected(string version, string path);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "Unable to inject the Danmaku web client into {Path}. See the plugin README for manual installation")]
    private partial void LogInjectionFailure(Exception exception, string path);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Information,
        Message = "Danmaku web client {Version} is already injected into {Path}")]
    private partial void LogInjectionCurrent(string version, string path);
}

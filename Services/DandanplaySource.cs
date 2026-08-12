using System.Buffers;
using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.Danmaku.Configuration;
using Jellyfin.Plugin.Danmaku.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Danmaku.Services;

internal sealed class DandanplaySource : IDanmakuSource
{
    private const int HashLength = 16 * 1024 * 1024;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DandanplaySource> _logger;

    public DandanplaySource(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<DandanplaySource> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
    }

    public string Id => "dandanplay";

    public string GetDisplayName(PluginConfiguration configuration) => "弹弹play";

    public bool IsConfigured(PluginConfiguration configuration)
    {
        return !string.IsNullOrWhiteSpace(configuration.DandanplayBaseUrl)
            && !string.IsNullOrWhiteSpace(configuration.DandanplayAppId)
            && !string.IsNullOrWhiteSpace(configuration.DandanplayAppSecret);
    }

    public async Task<SourceMatchResult> FindMatchesAsync(
        PluginConfiguration configuration,
        ItemDescriptor item,
        string? query,
        bool tryFileMatch,
        CancellationToken cancellationToken)
    {
        EnsureConfigured(configuration);

        if (tryFileMatch && string.IsNullOrWhiteSpace(query))
        {
            try
            {
                var fileMatches = await MatchFileAsync(configuration, item, cancellationToken).ConfigureAwait(false);
                if (fileMatches.Matches.Count > 0
                    && (fileMatches.Matches.Any(match => match.IsExact) || fileMatches.Matches.Count == 1))
                {
                    return fileMatches;
                }
            }
            catch (IOException exception)
            {
                _logger.LogDebug(exception, "Unable to hash {Path}; falling back to metadata matching", item.FilePath);
            }
            catch (UnauthorizedAccessException exception)
            {
                _logger.LogDebug(exception, "Unable to read {Path}; falling back to metadata matching", item.FilePath);
            }
        }

        return await SearchEpisodesAsync(configuration, item, query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DanmakuCommentDto>> GetCommentsAsync(
        PluginConfiguration configuration,
        string episodeId,
        CancellationToken cancellationToken)
    {
        EnsureConfigured(configuration);
        if (!long.TryParse(episodeId, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            throw new DanmakuSourceException("弹弹play 节目编号格式无效。");
        }

        var cacheKey = string.Create(
            CultureInfo.InvariantCulture,
            $"danmaku:dandanplay:{configuration.DandanplayBaseUrl}:{episodeId}:{configuration.DandanplayWithRelated}:{configuration.DandanplayChConvert}");
        if (_cache.TryGetValue<IReadOnlyList<DanmakuCommentDto>>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var path = "/api/v2/comment/" + Uri.EscapeDataString(episodeId);
        var uri = HttpSourceHelpers.CombineUrl(configuration.DandanplayBaseUrl, path);
        uri = HttpSourceHelpers.AddQuery(
            uri,
            [
                new("withRelated", configuration.DandanplayWithRelated ? "true" : "false"),
                new("chConvert", Math.Clamp(configuration.DandanplayChConvert, 0, 2).ToString(CultureInfo.InvariantCulture))
            ]);
        var root = await SendAsync(configuration, HttpMethod.Get, uri, null, cancellationToken).ConfigureAwait(false);
        HttpSourceHelpers.EnsureSuccessfulPayload(root);
        var comments = HttpSourceHelpers.ParseComments(root);

        _cache.Set(
            cacheKey,
            comments,
            TimeSpan.FromMinutes(Math.Clamp(configuration.CacheMinutes, 1, 10080)));
        return comments;
    }

    private async Task<SourceMatchResult> MatchFileAsync(
        PluginConfiguration configuration,
        ItemDescriptor item,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.FileName))
        {
            return new SourceMatchResult();
        }

        var fileHash = await GetFileHashAsync(item, cancellationToken).ConfigureAwait(false);
        var body = new
        {
            fileName = item.FileName,
            fileHash,
            fileSize = item.FileSize,
            videoDuration = item.DurationSeconds,
            matchMode = string.IsNullOrEmpty(fileHash) ? "fileNameOnly" : "hashAndFileName"
        };
        var uri = HttpSourceHelpers.CombineUrl(configuration.DandanplayBaseUrl, "/api/v2/match");
        var root = await SendAsync(configuration, HttpMethod.Post, uri, body, cancellationToken).ConfigureAwait(false);
        HttpSourceHelpers.EnsureSuccessfulPayload(root);

        var isMatched = root.GetBooleanValue("isMatched");
        if (!root.TryGetPropertyIgnoreCase("matches", out var matchesElement)
            || matchesElement.ValueKind != JsonValueKind.Array)
        {
            return new SourceMatchResult();
        }

        var matches = new List<MatchCandidateDto>();
        foreach (var match in matchesElement.EnumerateArray())
        {
            var episodeId = match.GetStringValue("episodeId");
            if (string.IsNullOrWhiteSpace(episodeId))
            {
                continue;
            }

            matches.Add(new MatchCandidateDto
            {
                Source = Id,
                EpisodeId = episodeId,
                AnimeTitle = match.GetStringValue("animeTitle"),
                EpisodeTitle = match.GetStringValue("episodeTitle"),
                Shift = match.GetDoubleValue("shift"),
                IsExact = isMatched
            });
        }

        return new SourceMatchResult { Matches = matches };
    }

    private async Task<SourceMatchResult> SearchEpisodesAsync(
        PluginConfiguration configuration,
        ItemDescriptor item,
        string? query,
        CancellationToken cancellationToken)
    {
        var parameters = new List<KeyValuePair<string, string?>>();
        if (!string.IsNullOrWhiteSpace(query))
        {
            if (query.Trim().Length < 2)
            {
                return new SourceMatchResult();
            }

            parameters.Add(new("anime", query.Trim()));
        }
        else if (!string.IsNullOrWhiteSpace(item.TmdbId)
                 && int.TryParse(item.TmdbId, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            parameters.Add(new("tmdbId", item.TmdbId));
            parameters.Add(new("tmdbIdType", item.TmdbIdType.ToString(CultureInfo.InvariantCulture)));
        }
        else if (!string.IsNullOrWhiteSpace(item.SearchTitle))
        {
            parameters.Add(new("anime", item.SearchTitle));
        }
        else
        {
            return new SourceMatchResult();
        }

        if (item.EpisodeNumber.HasValue)
        {
            parameters.Add(new("episode", item.EpisodeNumber.Value.ToString(CultureInfo.InvariantCulture)));
        }

        parameters.Add(new("v2", "true"));

        var uri = HttpSourceHelpers.CombineUrl(configuration.DandanplayBaseUrl, "/api/v2/search/episodes");
        uri = HttpSourceHelpers.AddQuery(uri, parameters);
        var root = await SendAsync(configuration, HttpMethod.Get, uri, null, cancellationToken).ConfigureAwait(false);
        HttpSourceHelpers.EnsureSuccessfulPayload(root);

        var matches = new List<MatchCandidateDto>();
        if (root.TryGetPropertyIgnoreCase("animes", out var animes) && animes.ValueKind == JsonValueKind.Array)
        {
            foreach (var anime in animes.EnumerateArray())
            {
                var animeTitle = anime.GetStringValue("animeTitle");
                if (!anime.TryGetPropertyIgnoreCase("episodes", out var episodes)
                    || episodes.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var episode in episodes.EnumerateArray())
                {
                    var episodeId = episode.GetStringValue("episodeId");
                    if (string.IsNullOrWhiteSpace(episodeId))
                    {
                        continue;
                    }

                    matches.Add(new MatchCandidateDto
                    {
                        Source = Id,
                        EpisodeId = episodeId,
                        AnimeTitle = animeTitle,
                        EpisodeTitle = episode.GetStringValue("episodeTitle")
                    });
                }
            }
        }

        return new SourceMatchResult
        {
            HasMore = root.GetBooleanValue("hasMore"),
            Matches = matches
                .DistinctBy(match => match.EpisodeId, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private async Task<JsonElement> SendAsync(
        PluginConfiguration configuration,
        HttpMethod method,
        Uri uri,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        AddAuthenticationHeaders(configuration, request);
        var client = _httpClientFactory.CreateClient(DanmakuService.HttpClientName);
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var upstreamMessage = response.Headers.TryGetValues("X-Error-Message", out var messages)
                ? messages.FirstOrDefault()
                : null;
            throw new DanmakuSourceException(
                $"弹弹play 请求失败 ({(int)response.StatusCode}): {upstreamMessage ?? response.ReasonPhrase}");
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new DanmakuSourceException("弹弹play 返回了无法解析的数据。", exception);
        }
    }

    private static void AddAuthenticationHeaders(
        PluginConfiguration configuration,
        HttpRequestMessage request)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var path = request.RequestUri?.AbsolutePath.ToLowerInvariant() ?? string.Empty;
        var signatureInput = configuration.DandanplayAppId
            + timestamp
            + path
            + configuration.DandanplayAppSecret;
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(signatureInput));
        var signature = Convert.ToBase64String(digest);

        request.Headers.TryAddWithoutValidation("X-AppId", configuration.DandanplayAppId);
        request.Headers.TryAddWithoutValidation("X-Timestamp", timestamp);
        request.Headers.TryAddWithoutValidation("X-Signature", signature);
    }

    private async Task<string?> GetFileHashAsync(ItemDescriptor item, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.FilePath) || !File.Exists(item.FilePath))
        {
            return null;
        }

        var fileInfo = new FileInfo(item.FilePath);
        var cacheKey = string.Create(
            CultureInfo.InvariantCulture,
            $"danmaku:filehash:{item.FilePath}:{fileInfo.Length}:{fileInfo.LastWriteTimeUtc.Ticks}");
        if (_cache.TryGetValue<string>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var stream = new FileStream(
            item.FilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            131072,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var streamScope = stream.ConfigureAwait(false);
#pragma warning disable CA5351 // MD5 is required by the dandanplay file identification protocol.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
#pragma warning restore CA5351
        var buffer = ArrayPool<byte>.Shared.Rent(131072);
        try
        {
            var remaining = Math.Min(HashLength, stream.Length);
            while (remaining > 0)
            {
                var read = await stream.ReadAsync(
                    buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, read);
                remaining -= read;
            }

            var result = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            _cache.Set(cacheKey, result, TimeSpan.FromHours(24));
            return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void EnsureConfigured(PluginConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.DandanplayAppId)
            || string.IsNullOrWhiteSpace(configuration.DandanplayAppSecret))
        {
            throw new DanmakuSourceException("请先在插件设置中填写弹弹play AppId 和 AppSecret。");
        }
    }
}

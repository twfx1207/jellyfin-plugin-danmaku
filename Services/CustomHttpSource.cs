using System.Globalization;
using System.Text.Json;
using Jellyfin.Plugin.Danmaku.Configuration;
using Jellyfin.Plugin.Danmaku.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Danmaku.Services;

internal sealed class CustomHttpSource : IDanmakuSource
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CustomHttpSource> _logger;

    public CustomHttpSource(IHttpClientFactory httpClientFactory, IMemoryCache cache, ILogger<CustomHttpSource> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
    }

    public string Id => "custom";

    public string GetDisplayName(PluginConfiguration configuration)
    {
        return string.IsNullOrWhiteSpace(configuration.CustomSourceName)
            ? "自定义源"
            : configuration.CustomSourceName.Trim();
    }

    public bool IsConfigured(PluginConfiguration configuration)
    {
        return configuration.CustomSourceEnabled
            && Uri.TryCreate(configuration.CustomSourceBaseUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    public async Task<SourceMatchResult> FindMatchesAsync(
        PluginConfiguration configuration,
        ItemDescriptor item,
        string? query,
        bool tryFileMatch,
        CancellationToken cancellationToken)
    {
        EnsureConfigured(configuration);
        var uri = HttpSourceHelpers.CombineUrl(
            configuration.CustomSourceBaseUrl,
            configuration.CustomMatchPath);
        uri = HttpSourceHelpers.AddQuery(
            uri,
            [
                new("itemId", item.ItemId.ToString("N")),
                new("title", item.Title),
                new("seriesName", item.SeriesName),
                new("season", item.SeasonNumber?.ToString(CultureInfo.InvariantCulture)),
                new("episode", item.EpisodeNumber?.ToString(CultureInfo.InvariantCulture)),
                new("tmdbId", item.TmdbId),
                new("tmdbIdType", item.TmdbIdType.ToString(CultureInfo.InvariantCulture)),
                new("fileName", item.FileName),
                new("fileSize", item.FileSize.ToString(CultureInfo.InvariantCulture)),
                new("duration", item.DurationSeconds.ToString(CultureInfo.InvariantCulture)),
                new("query", query),
                new("automatic", tryFileMatch ? "true" : "false")
            ]);

        var root = await SendAsync(configuration, uri, cancellationToken).ConfigureAwait(false);
        HttpSourceHelpers.EnsureSuccessfulPayload(root);
        var exact = root.GetBooleanValue("isMatched");
        var matches = ParseFlatMatches(root, exact);
        if (matches.Count == 0)
        {
            matches = ParseDandanplayStyleMatches(root, exact);
        }

        return new SourceMatchResult
        {
            HasMore = root.GetBooleanValue("hasMore"),
            Matches = matches
                .DistinctBy(match => match.EpisodeId, StringComparer.Ordinal)
                .ToArray()
        };
    }

    public async Task<IReadOnlyList<DanmakuCommentDto>> GetCommentsAsync(
        PluginConfiguration configuration,
        string episodeId,
        CancellationToken cancellationToken)
    {
        EnsureConfigured(configuration);
        var resolvedPath = configuration.CustomCommentsPath.Replace(
            "{episodeId}",
            Uri.EscapeDataString(episodeId),
            StringComparison.OrdinalIgnoreCase);
        var cacheKey = string.Create(
            CultureInfo.InvariantCulture,
            $"danmaku:custom:{configuration.CustomSourceBaseUrl}:{resolvedPath}");
        if (_cache.TryGetValue<IReadOnlyList<DanmakuCommentDto>>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var uri = HttpSourceHelpers.CombineUrl(configuration.CustomSourceBaseUrl, resolvedPath);
        var root = await SendAsync(configuration, uri, cancellationToken).ConfigureAwait(false);
        HttpSourceHelpers.EnsureSuccessfulPayload(root);
        var comments = HttpSourceHelpers.ParseComments(root);
        _cache.Set(
            cacheKey,
            comments,
            TimeSpan.FromMinutes(Math.Clamp(configuration.CacheMinutes, 1, 10080)));
        return comments;
    }

    private static IReadOnlyList<MatchCandidateDto> ParseFlatMatches(JsonElement root, bool exact)
    {
        if (!root.TryGetPropertyIgnoreCase("matches", out var matchesElement)
            || matchesElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<MatchCandidateDto>();
        }

        var matches = new List<MatchCandidateDto>();
        foreach (var match in matchesElement.EnumerateArray())
        {
            var episodeId = match.GetStringValue("episodeId", match.GetStringValue("id"));
            if (string.IsNullOrWhiteSpace(episodeId))
            {
                continue;
            }

            matches.Add(new MatchCandidateDto
            {
                Source = "custom",
                EpisodeId = episodeId,
                AnimeTitle = match.GetStringValue("animeTitle", match.GetStringValue("title")),
                EpisodeTitle = match.GetStringValue("episodeTitle", match.GetStringValue("subtitle")),
                Shift = match.GetDoubleValue("shift"),
                IsExact = exact || match.GetBooleanValue("isExact")
            });
        }

        return matches;
    }

    private static IReadOnlyList<MatchCandidateDto> ParseDandanplayStyleMatches(JsonElement root, bool exact)
    {
        if (!root.TryGetPropertyIgnoreCase("animes", out var animes)
            || animes.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<MatchCandidateDto>();
        }

        var matches = new List<MatchCandidateDto>();
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
                if (!string.IsNullOrWhiteSpace(episodeId))
                {
                    matches.Add(new MatchCandidateDto
                    {
                        Source = "custom",
                        EpisodeId = episodeId,
                        AnimeTitle = animeTitle,
                        EpisodeTitle = episode.GetStringValue("episodeTitle"),
                        IsExact = exact
                    });
                }
            }
        }

        return matches;
    }

    private async Task<JsonElement> SendAsync(
        PluginConfiguration configuration,
        Uri uri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (!string.IsNullOrWhiteSpace(configuration.CustomApiKey)
            && !string.IsNullOrWhiteSpace(configuration.CustomApiKeyHeader)
            && configuration.CustomApiKeyHeader.IndexOf('\r', StringComparison.Ordinal) < 0
            && configuration.CustomApiKeyHeader.IndexOf('\n', StringComparison.Ordinal) < 0
            && configuration.CustomApiKey.IndexOf('\r', StringComparison.Ordinal) < 0
            && configuration.CustomApiKey.IndexOf('\n', StringComparison.Ordinal) < 0)
        {
            request.Headers.TryAddWithoutValidation(
                configuration.CustomApiKeyHeader.Trim(),
                configuration.CustomApiKey);
        }

        var client = _httpClientFactory.CreateClient(DanmakuService.HttpClientName);
        return await DanmakuHttpTransport.SendAsync(
            client, request, "自定义弹幕源", _logger, cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureConfigured(PluginConfiguration configuration)
    {
        if (!configuration.CustomSourceEnabled
            || string.IsNullOrWhiteSpace(configuration.CustomSourceBaseUrl))
        {
            throw new DanmakuSourceException("请先在插件设置中启用并配置自定义弹幕源。");
        }
    }
}

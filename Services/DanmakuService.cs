using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.Danmaku.Configuration;
using Jellyfin.Plugin.Danmaku.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Danmaku.Services;

/// <summary>
/// Coordinates item access checks, source matching, persisted associations, and comment loading.
/// </summary>
public sealed class DanmakuService
{
    /// <summary>
    /// The name of the HTTP client shared by danmaku sources.
    /// </summary>
    public const string HttpClientName = "Jellyfin.Plugin.Danmaku";

    private readonly ILibraryManager _libraryManager;
    private readonly AssociationStore _associationStore;
    private readonly IReadOnlyList<IDanmakuSource> _sources;

    internal DanmakuService(
        ILibraryManager libraryManager,
        AssociationStore associationStore,
        DandanplaySource dandanplaySource,
        CustomHttpSource customHttpSource)
    {
        _libraryManager = libraryManager;
        _associationStore = associationStore;
        _sources = [dandanplaySource, customHttpSource];
    }

    /// <summary>
    /// Gets the subset of plugin options that is safe to expose to an authenticated client.
    /// </summary>
    /// <returns>The client options.</returns>
    public ClientOptionsDto GetClientOptions()
    {
        var configuration = GetConfiguration();
        var defaultSource = FindSource(configuration.DefaultSource) is null
            ? "dandanplay"
            : configuration.DefaultSource.ToLowerInvariant();
        return new ClientOptionsDto
        {
            Enabled = configuration.Enabled,
            AutoMatch = configuration.AutoMatch,
            DefaultSource = defaultSource,
            Sources = _sources
                .Where(source => source.Id != "custom" || configuration.CustomSourceEnabled)
                .Select(source => new SourceOptionDto
                {
                    Id = source.Id,
                    Name = source.GetDisplayName(configuration),
                    Configured = source.IsConfigured(configuration)
                })
                .ToArray(),
            Opacity = Math.Clamp(configuration.Opacity, 0.1, 1),
            FontScale = Math.Clamp(configuration.FontScale, 0.5, 2.5),
            SpeedSeconds = Math.Clamp(configuration.SpeedSeconds, 3, 30),
            MaxOnScreen = Math.Clamp(configuration.MaxOnScreen, 10, 300)
        };
    }

    /// <summary>
    /// Finds manual match candidates for a library item.
    /// </summary>
    /// <param name="userId">The requesting Jellyfin user.</param>
    /// <param name="itemId">The library item identifier.</param>
    /// <param name="sourceId">The requested source, or the configured default.</param>
    /// <param name="query">An optional title query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The available match candidates.</returns>
    public async Task<MatchResponseDto> FindMatchesAsync(
        Guid userId,
        Guid itemId,
        string? sourceId,
        string? query,
        CancellationToken cancellationToken)
    {
        var configuration = GetConfiguration();
        var source = GetSource(configuration, sourceId);
        var item = GetItemDescriptor(userId, itemId);
        var matches = await source.FindMatchesAsync(
            configuration,
            item,
            query,
            string.IsNullOrWhiteSpace(query),
            cancellationToken).ConfigureAwait(false);
        return new MatchResponseDto
        {
            HasMore = matches.HasMore,
            Matches = matches.Matches
        };
    }

    /// <summary>
    /// Loads comments for an item and optionally performs an automatic match.
    /// </summary>
    /// <param name="userId">The requesting Jellyfin user.</param>
    /// <param name="itemId">The library item identifier.</param>
    /// <param name="sourceId">The requested source, or the configured default.</param>
    /// <param name="allowAutoMatch">Whether this request allows automatic matching.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The active match, comments, and any ambiguous candidates.</returns>
    public async Task<DanmakuLoadResponseDto> LoadAsync(
        Guid userId,
        Guid itemId,
        string? sourceId,
        bool allowAutoMatch,
        CancellationToken cancellationToken)
    {
        var configuration = GetConfiguration();
        if (!configuration.Enabled)
        {
            return new DanmakuLoadResponseDto { Message = "弹幕插件已禁用。" };
        }

        var item = GetItemDescriptor(userId, itemId);
        Models.AssociationRecord? association = null;
        SourceMatchResult? automaticMatches = null;

        if (!_associationStore.TryGet(itemId, out association) || association is null)
        {
            if (!allowAutoMatch || !configuration.AutoMatch)
            {
                return new DanmakuLoadResponseDto { Message = "尚未匹配弹幕。" };
            }

            var source = GetSource(configuration, sourceId);
            automaticMatches = await source.FindMatchesAsync(
                configuration,
                item,
                null,
                true,
                cancellationToken).ConfigureAwait(false);
            var selected = SelectAutomaticMatch(automaticMatches.Matches);
            if (selected is null)
            {
                return new DanmakuLoadResponseDto
                {
                    Candidates = automaticMatches.Matches,
                    Message = automaticMatches.Matches.Count == 0
                        ? "没有找到可用弹幕，请尝试手动匹配。"
                        : "自动匹配结果不唯一，请手动选择。"
                };
            }

            association = ToAssociation(selected);
            await _associationStore.SetAsync(itemId, association, cancellationToken).ConfigureAwait(false);
        }

        return await LoadAssociationAsync(configuration, association, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Persists a selected match and loads its comments.
    /// </summary>
    /// <param name="userId">The requesting Jellyfin user.</param>
    /// <param name="itemId">The library item identifier.</param>
    /// <param name="request">The selected source episode.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The selected match and its comments.</returns>
    public async Task<DanmakuLoadResponseDto> SetMatchAsync(
        Guid userId,
        Guid itemId,
        SetMatchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = GetItemDescriptor(userId, itemId);
        if (string.IsNullOrWhiteSpace(request.EpisodeId))
        {
            throw new ArgumentException("节目编号不能为空。", nameof(request));
        }

        var configuration = GetConfiguration();
        var source = GetSource(configuration, request.Source);
        if (!source.IsConfigured(configuration))
        {
            throw new DanmakuSourceException($"弹幕源 {source.GetDisplayName(configuration)} 尚未配置完成。");
        }

        var association = new Models.AssociationRecord
        {
            Source = source.Id,
            EpisodeId = request.EpisodeId.Trim(),
            AnimeTitle = request.AnimeTitle.Trim(),
            EpisodeTitle = request.EpisodeTitle.Trim(),
            Shift = double.IsFinite(request.Shift) ? Math.Clamp(request.Shift, -3600, 3600) : 0
        };
        await _associationStore.SetAsync(itemId, association, cancellationToken).ConfigureAwait(false);
        return await LoadAssociationAsync(configuration, association, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes the persisted match for an item.
    /// </summary>
    /// <param name="userId">The requesting Jellyfin user.</param>
    /// <param name="itemId">The library item identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when an association was removed.</returns>
    public Task<bool> ClearMatchAsync(Guid userId, Guid itemId, CancellationToken cancellationToken)
    {
        _ = GetItemDescriptor(userId, itemId);
        return _associationStore.RemoveAsync(itemId, cancellationToken);
    }

    private async Task<DanmakuLoadResponseDto> LoadAssociationAsync(
        PluginConfiguration configuration,
        Models.AssociationRecord association,
        CancellationToken cancellationToken)
    {
        var source = GetSource(configuration, association.Source);
        var sourceComments = await source.GetCommentsAsync(
            configuration,
            association.EpisodeId,
            cancellationToken).ConfigureAwait(false);
        var shifted = sourceComments
            .Select(comment => new DanmakuCommentDto
            {
                Time = Math.Max(0, comment.Time + association.Shift),
                Mode = comment.Mode,
                Color = comment.Color,
                Text = comment.Text
            })
            .OrderBy(comment => comment.Time)
            .ToArray();
        var comments = LimitComments(shifted, Math.Clamp(configuration.MaxComments, 100, 30000));
        return new DanmakuLoadResponseDto
        {
            Matched = true,
            Match = association.ToDto(),
            Comments = comments,
            Message = comments.Length == 0 ? "已匹配，但弹幕源没有返回弹幕。" : null
        };
    }

    [SuppressMessage(
        "Security",
        "CA3003:Review code for file path injection vulnerabilities",
        Justification = "The media path comes from an access-checked Jellyfin library item, not from request input.")]
    private ItemDescriptor GetItemDescriptor(Guid userId, Guid itemId)
    {
        var item = userId.Equals(Guid.Empty)
            ? null
            : _libraryManager.GetItemById<BaseItem>(itemId, userId);
        if (item is null)
        {
            throw new KeyNotFoundException("找不到媒体项目，或当前用户没有访问权限。");
        }

        var episode = item as Episode;
        var series = episode?.Series;
        var filePath = item.Path ?? string.Empty;
        var fileSize = item.Size ?? 0;
        if (fileSize <= 0 && File.Exists(filePath))
        {
            try
            {
                fileSize = new FileInfo(filePath).Length;
            }
            catch (IOException)
            {
                fileSize = 0;
            }
            catch (UnauthorizedAccessException)
            {
                fileSize = 0;
            }
        }

        var tmdbId = string.Empty;
        if (series?.ProviderIds.TryGetValue("Tmdb", out var seriesTmdbId) == true)
        {
            tmdbId = seriesTmdbId;
        }
        else if (item.ProviderIds.TryGetValue("Tmdb", out var itemTmdbId))
        {
            tmdbId = itemTmdbId;
        }

        var duration = (item.RunTimeTicks ?? 0) / TimeSpan.TicksPerSecond;
        return new ItemDescriptor
        {
            ItemId = item.Id,
            Title = item.Name ?? string.Empty,
            SeriesName = episode?.SeriesName ?? series?.Name ?? string.Empty,
            SeasonNumber = episode?.ParentIndexNumber,
            EpisodeNumber = episode?.IndexNumber,
            TmdbId = tmdbId,
            TmdbIdType = item is Movie ? 1 : 0,
            FileName = string.IsNullOrWhiteSpace(filePath)
                ? item.Name ?? string.Empty
                : Path.GetFileName(filePath),
            FilePath = filePath,
            FileSize = Math.Max(0, fileSize),
            DurationSeconds = (int)Math.Clamp(duration, 0, int.MaxValue)
        };
    }

    private IDanmakuSource GetSource(PluginConfiguration configuration, string? requestedSource)
    {
        var sourceId = string.IsNullOrWhiteSpace(requestedSource)
            ? configuration.DefaultSource
            : requestedSource;
        var source = FindSource(sourceId)
            ?? throw new DanmakuSourceException($"未知弹幕源: {sourceId}");
        if (!source.IsConfigured(configuration))
        {
            throw new DanmakuSourceException($"弹幕源 {source.GetDisplayName(configuration)} 尚未配置完成。");
        }

        return source;
    }

    private IDanmakuSource? FindSource(string? sourceId)
    {
        return _sources.FirstOrDefault(source => string.Equals(
            source.Id,
            sourceId,
            StringComparison.OrdinalIgnoreCase));
    }

    private static MatchCandidateDto? SelectAutomaticMatch(IReadOnlyList<MatchCandidateDto> matches)
    {
        var exactMatches = matches.Where(match => match.IsExact).ToArray();
        if (exactMatches.Length == 1)
        {
            return exactMatches[0];
        }

        return matches.Count == 1 ? matches[0] : null;
    }

    private static Models.AssociationRecord ToAssociation(MatchCandidateDto match)
    {
        return new Models.AssociationRecord
        {
            Source = match.Source,
            EpisodeId = match.EpisodeId,
            AnimeTitle = match.AnimeTitle,
            EpisodeTitle = match.EpisodeTitle,
            Shift = match.Shift
        };
    }

    private static DanmakuCommentDto[] LimitComments(
        DanmakuCommentDto[] comments,
        int maximum)
    {
        if (comments.Length <= maximum)
        {
            return comments;
        }

        var result = new DanmakuCommentDto[maximum];
        var step = comments.Length / (double)maximum;
        for (var index = 0; index < maximum; index++)
        {
            result[index] = comments[(int)Math.Floor(index * step)];
        }

        return result;
    }

    private static PluginConfiguration GetConfiguration()
    {
        return Plugin.Instance?.Configuration
            ?? throw new InvalidOperationException("The danmaku plugin has not been initialized.");
    }
}

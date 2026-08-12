using Jellyfin.Plugin.Danmaku.Configuration;
using Jellyfin.Plugin.Danmaku.Models;

namespace Jellyfin.Plugin.Danmaku.Services;

internal interface IDanmakuSource
{
    string Id { get; }

    string GetDisplayName(PluginConfiguration configuration);

    bool IsConfigured(PluginConfiguration configuration);

    Task<SourceMatchResult> FindMatchesAsync(
        PluginConfiguration configuration,
        ItemDescriptor item,
        string? query,
        bool tryFileMatch,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DanmakuCommentDto>> GetCommentsAsync(
        PluginConfiguration configuration,
        string episodeId,
        CancellationToken cancellationToken);
}

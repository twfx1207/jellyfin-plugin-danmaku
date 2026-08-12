using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Danmaku.Models;

/// <summary>
/// A normalized danmaku comment.
/// </summary>
public sealed class DanmakuCommentDto
{
    /// <summary>
    /// Gets or sets the appearance time in seconds.
    /// </summary>
    [JsonPropertyName("time")]
    public double Time { get; set; }

    /// <summary>
    /// Gets or sets the display mode: 1 scrolling, 4 bottom, or 5 top.
    /// </summary>
    [JsonPropertyName("mode")]
    public int Mode { get; set; } = 1;

    /// <summary>
    /// Gets or sets the RGB color encoded as an integer.
    /// </summary>
    [JsonPropertyName("color")]
    public int Color { get; set; } = 0xFFFFFF;

    /// <summary>
    /// Gets or sets the comment text.
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// A match candidate returned by a danmaku source.
/// </summary>
public sealed class MatchCandidateDto
{
    /// <summary>
    /// Gets or sets the source identifier.
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source episode identifier.
    /// </summary>
    [JsonPropertyName("episodeId")]
    public string EpisodeId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the work title.
    /// </summary>
    [JsonPropertyName("animeTitle")]
    public string AnimeTitle { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the episode title.
    /// </summary>
    [JsonPropertyName("episodeTitle")]
    public string EpisodeTitle { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source-recommended time shift in seconds.
    /// </summary>
    [JsonPropertyName("shift")]
    public double Shift { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is an exact match.
    /// </summary>
    [JsonPropertyName("isExact")]
    public bool IsExact { get; set; }
}

/// <summary>
/// A source matching response.
/// </summary>
public sealed class MatchResponseDto
{
    /// <summary>
    /// Gets or sets a value indicating whether the operation succeeded.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether additional results were omitted by the source.
    /// </summary>
    [JsonPropertyName("hasMore")]
    public bool HasMore { get; set; }

    /// <summary>
    /// Gets or sets the candidates.
    /// </summary>
    [JsonPropertyName("matches")]
    public IReadOnlyList<MatchCandidateDto> Matches { get; set; } = Array.Empty<MatchCandidateDto>();

    /// <summary>
    /// Gets or sets an error message.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// A response containing the active match and its comments.
/// </summary>
public sealed class DanmakuLoadResponseDto
{
    /// <summary>
    /// Gets or sets a value indicating whether a match is active.
    /// </summary>
    [JsonPropertyName("matched")]
    public bool Matched { get; set; }

    /// <summary>
    /// Gets or sets the active match.
    /// </summary>
    [JsonPropertyName("match")]
    public MatchCandidateDto? Match { get; set; }

    /// <summary>
    /// Gets or sets the comments.
    /// </summary>
    [JsonPropertyName("comments")]
    public IReadOnlyList<DanmakuCommentDto> Comments { get; set; } = Array.Empty<DanmakuCommentDto>();

    /// <summary>
    /// Gets or sets candidates when automatic matching was ambiguous.
    /// </summary>
    [JsonPropertyName("candidates")]
    public IReadOnlyList<MatchCandidateDto> Candidates { get; set; } = Array.Empty<MatchCandidateDto>();

    /// <summary>
    /// Gets or sets an informational or error message.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>
/// A request to persist a manual match.
/// </summary>
public sealed class SetMatchRequest
{
    /// <summary>
    /// Gets or sets the source identifier.
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source episode identifier.
    /// </summary>
    [JsonPropertyName("episodeId")]
    public string EpisodeId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the work title.
    /// </summary>
    [JsonPropertyName("animeTitle")]
    public string AnimeTitle { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the episode title.
    /// </summary>
    [JsonPropertyName("episodeTitle")]
    public string EpisodeTitle { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the time shift in seconds.
    /// </summary>
    [JsonPropertyName("shift")]
    public double Shift { get; set; }
}

/// <summary>
/// A source option safe to expose to regular web clients.
/// </summary>
public sealed class SourceOptionDto
{
    /// <summary>
    /// Gets or sets the source identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source display name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the source has enough configuration to be used.
    /// </summary>
    [JsonPropertyName("configured")]
    public bool Configured { get; set; }
}

/// <summary>
/// Client-safe plugin options.
/// </summary>
public sealed class ClientOptionsDto
{
    /// <summary>
    /// Gets or sets a value indicating whether the plugin is enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether automatic matching is enabled.
    /// </summary>
    [JsonPropertyName("autoMatch")]
    public bool AutoMatch { get; set; }

    /// <summary>
    /// Gets or sets the default source identifier.
    /// </summary>
    [JsonPropertyName("defaultSource")]
    public string DefaultSource { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the available sources.
    /// </summary>
    [JsonPropertyName("sources")]
    public IReadOnlyList<SourceOptionDto> Sources { get; set; } = Array.Empty<SourceOptionDto>();

    /// <summary>
    /// Gets or sets the overlay opacity.
    /// </summary>
    [JsonPropertyName("opacity")]
    public double Opacity { get; set; }

    /// <summary>
    /// Gets or sets the font scale.
    /// </summary>
    [JsonPropertyName("fontScale")]
    public double FontScale { get; set; }

    /// <summary>
    /// Gets or sets the scrolling duration in seconds.
    /// </summary>
    [JsonPropertyName("speedSeconds")]
    public double SpeedSeconds { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of simultaneous comments.
    /// </summary>
    [JsonPropertyName("maxOnScreen")]
    public int MaxOnScreen { get; set; }
}

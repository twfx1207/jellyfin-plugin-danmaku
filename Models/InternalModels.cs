namespace Jellyfin.Plugin.Danmaku.Models;

internal sealed class ItemDescriptor
{
    public Guid ItemId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string SeriesName { get; init; } = string.Empty;

    public int? SeasonNumber { get; init; }

    public int? EpisodeNumber { get; init; }

    public string TmdbId { get; init; } = string.Empty;

    public int TmdbIdType { get; init; }

    public string FileName { get; init; } = string.Empty;

    public string FilePath { get; init; } = string.Empty;

    public long FileSize { get; init; }

    public int DurationSeconds { get; init; }

    public string SearchTitle => string.IsNullOrWhiteSpace(SeriesName) ? Title : SeriesName;
}

internal sealed class SourceMatchResult
{
    public bool HasMore { get; init; }

    public IReadOnlyList<MatchCandidateDto> Matches { get; init; } = Array.Empty<MatchCandidateDto>();
}

internal sealed class AssociationRecord
{
    public string Source { get; set; } = string.Empty;

    public string EpisodeId { get; set; } = string.Empty;

    public string AnimeTitle { get; set; } = string.Empty;

    public string EpisodeTitle { get; set; } = string.Empty;

    public double Shift { get; set; }

    public MatchCandidateDto ToDto()
    {
        return new MatchCandidateDto
        {
            Source = Source,
            EpisodeId = EpisodeId,
            AnimeTitle = AnimeTitle,
            EpisodeTitle = EpisodeTitle,
            Shift = Shift,
            IsExact = true
        };
    }
}

/// <summary>
/// Represents an error returned by, or encountered while calling, a danmaku source.
/// </summary>
public sealed class DanmakuSourceException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DanmakuSourceException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public DanmakuSourceException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DanmakuSourceException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public DanmakuSourceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

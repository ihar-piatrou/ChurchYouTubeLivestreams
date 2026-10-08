namespace ChurchYouTubeAssistant.Models;

/// <summary>API-facing projection of one uploaded video.</summary>
public sealed record YouTubeVideoDto
{
    public required string VideoId { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }

    /// <summary>When the video itself was published (not when it was added to the uploads playlist).</summary>
    public DateTimeOffset? PublishedAt { get; init; }

    public string? ThumbnailUrl { get; init; }

    public string WatchUrl => $"https://www.youtube.com/watch?v={VideoId}";

    /// <summary>
    /// Populated only when the caller opts in, because it costs an extra videos.list call.
    /// </summary>
    public YouTubeVideoDetailsDto? Details { get; init; }
}

/// <summary>
/// Extra per-video facts that playlistItems.list does not return. Fetched with a single batched
/// videos.list call. These fields are what later pipeline stages key off: whether a video is a
/// finished livestream, and whether captions exist to transcribe.
/// </summary>
public sealed record YouTubeVideoDetailsDto
{
    public TimeSpan? Duration { get; init; }
    public ulong? ViewCount { get; init; }
    public ulong? LikeCount { get; init; }
    public string? PrivacyStatus { get; init; }

    /// <summary>"none" for a normal video, "live" or "upcoming" for a broadcast.</summary>
    public string? LiveBroadcastContent { get; init; }

    /// <summary>True when the video has livestream metadata, i.e. it was (or is) a broadcast.</summary>
    public bool IsLivestream { get; init; }

    public DateTimeOffset? LiveActualStartTime { get; init; }
    public DateTimeOffset? LiveActualEndTime { get; init; }

    /// <summary>Whether YouTube reports captions for this video (the future transcript source).</summary>
    public bool? HasCaptions { get; init; }
}

/// <summary>Envelope for a page of videos, including the provenance of the list.</summary>
public sealed record YouTubeVideoListDto
{
    public required string ChannelId { get; init; }
    public required string UploadsPlaylistId { get; init; }
    public required int Count { get; init; }
    public required IReadOnlyList<YouTubeVideoDto> Videos { get; init; }

    /// <summary>Pass back as pageToken to fetch the next page; null when this is the last page.</summary>
    public string? NextPageToken { get; init; }

    /// <summary>Pass back as pageToken to fetch the previous page; null on the first page.</summary>
    public string? PrevPageToken { get; init; }
}

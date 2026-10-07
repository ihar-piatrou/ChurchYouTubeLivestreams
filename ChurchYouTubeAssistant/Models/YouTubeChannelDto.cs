namespace ChurchYouTubeAssistant.Models;

/// <summary>
/// API-facing projection of a YouTube channel. Deliberately our own shape rather than Google's
/// SDK model, so the SDK can be upgraded or swapped without changing our public contract.
/// </summary>
public sealed record YouTubeChannelDto
{
    public required string ChannelId { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }

    /// <summary>The @handle style URL, when the channel has claimed one.</summary>
    public string? CustomUrl { get; init; }

    public string? ThumbnailUrl { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }

    public ulong? SubscriberCount { get; init; }
    public bool SubscriberCountHidden { get; init; }
    public ulong? VideoCount { get; init; }
    public ulong? ViewCount { get; init; }

    /// <summary>
    /// The channel's "all uploads" playlist. Listing this playlist is how we enumerate videos:
    /// 1 quota unit per page, versus 100 units for a search.list call.
    /// </summary>
    public string? UploadsPlaylistId { get; init; }
}

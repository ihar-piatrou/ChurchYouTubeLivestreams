namespace ChurchYouTubeAssistant.Models;

/// <summary>What the admin is asking to publish to YouTube for one analysis.</summary>
public sealed record PublishRequest
{
    public required string Title { get; init; }
    public required string Description { get; init; }

    /// <summary>
    /// Set true to proceed despite a detected conflict (the live video's title/description has
    /// changed since this analysis was run). The first call without this flag only reports the
    /// conflict; it never publishes.
    /// </summary>
    public bool AcknowledgeConflict { get; init; }
}

/// <summary>The outcome of a publish attempt.</summary>
public sealed record PublishResult
{
    public required bool Published { get; init; }
    public required bool ConflictDetected { get; init; }
    public string? ConflictDetail { get; init; }
    public string? CurrentLiveTitle { get; init; }
    public string? CurrentLiveDescription { get; init; }
    public required VideoAnalysis Analysis { get; init; }

    /// <summary>True when a generated thumbnail existed and an upload to YouTube was attempted.</summary>
    public bool ThumbnailAttempted { get; init; }

    /// <summary>True when the thumbnail upload succeeded. False (with <see cref="ThumbnailError"/> set) on failure.</summary>
    public bool ThumbnailPublished { get; init; }

    /// <summary>
    /// Set when <see cref="ThumbnailAttempted"/> is true but <see cref="ThumbnailPublished"/> is
    /// false - the title/description still published successfully; only the thumbnail failed.
    /// </summary>
    public string? ThumbnailError { get; init; }
}

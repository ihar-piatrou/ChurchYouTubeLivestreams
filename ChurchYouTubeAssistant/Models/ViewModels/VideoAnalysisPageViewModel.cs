namespace ChurchYouTubeAssistant.Models.ViewModels;

/// <summary>View model for the AI analysis review page (Home/Analysis).</summary>
public sealed record VideoAnalysisPageViewModel
{
    public required string VideoId { get; init; }

    public IReadOnlyList<VideoAnalysis> History { get; init; } = [];

    /// <summary>The analysis currently being displayed (newest by default, or a chosen past one).</summary>
    public VideoAnalysis? Selected { get; init; }

    /// <summary>Description textarea's initial content: optimized description + included chapters.</summary>
    public string PrefilledDescription { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable active thumbnail provider + model, e.g. "OpenAI (gpt-image-2.5-sunburst)" -
    /// reflects whichever provider Thumbnail:Provider currently selects, so the page never shows a
    /// hardcoded provider name that could drift from what's actually configured.
    /// </summary>
    public required string ThumbnailProviderLabel { get; init; }

    public string? FlashError { get; init; }
    public string? FlashSuccess { get; init; }

    public bool ConflictDetected { get; init; }
    public string? ConflictDetail { get; init; }
    public string? ConflictLiveTitle { get; init; }
    public string? ConflictLiveDescription { get; init; }

    /// <summary>What the admin had typed into the title/description fields when a conflict interrupted publishing.</summary>
    public string? PendingPublishTitle { get; init; }
    public string? PendingPublishDescription { get; init; }
}

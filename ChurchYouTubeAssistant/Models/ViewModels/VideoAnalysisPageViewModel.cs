namespace ChurchYouTubeAssistant.Models.ViewModels;

/// <summary>View model for the AI analysis review page (Home/Analysis).</summary>
public sealed record VideoAnalysisPageViewModel
{
    public required string VideoId { get; init; }

    /// <summary>The dashboard page token this video was opened from, if any; carried through every
    /// action on this page and used to build the "Back to dashboard" link so it returns to the same
    /// page instead of resetting to the first one.</summary>
    public string? PageToken { get; init; }

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

    /// <summary>
    /// Set while an "Analyze" or "Generate Thumbnail" background job for this video is still
    /// pending/running - the view renders a waiting bar and polls <c>/jobs/{id}/status</c> instead
    /// of the normal success/error flash. Null once the job is done (the controller redirects away
    /// from the job URL at that point, so the view never actually has to render a finished job).
    /// </summary>
    public Guid? PendingJobId { get; init; }

    /// <summary>"analyze" or "thumbnail" - which action's waiting message/copy to show.</summary>
    public string? PendingJobKind { get; init; }

    public bool ConflictDetected { get; init; }
    public string? ConflictDetail { get; init; }
    public string? ConflictLiveTitle { get; init; }
    public string? ConflictLiveDescription { get; init; }

    /// <summary>What the admin had typed into the title/description fields when a conflict interrupted publishing.</summary>
    public string? PendingPublishTitle { get; init; }
    public string? PendingPublishDescription { get; init; }
}

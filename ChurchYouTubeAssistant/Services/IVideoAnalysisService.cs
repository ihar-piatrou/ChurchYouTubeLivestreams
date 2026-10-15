using ChurchYouTubeAssistant.Models;

namespace ChurchYouTubeAssistant.Services;

/// <summary>
/// Orchestrates one full AI video analysis: retrieve metadata and transcript, call OpenAI, validate
/// the response, persist it - and, separately, publishing an approved result back to YouTube.
/// </summary>
public interface IVideoAnalysisService
{
    /// <summary>
    /// A coarse per-video status for the dashboard: whether a video has ever been analyzed, and
    /// whether the latest analysis has been published back to YouTube.
    /// </summary>
    Task<IReadOnlyDictionary<string, VideoDashboardStatus>> GetDashboardStatusesAsync(
        IReadOnlyCollection<string> videoIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// The id of the latest analysis for each video, but only for videos whose latest analysis has
    /// a generated thumbnail image - the lookup behind the dashboard's "Review Thumbnail" button, so
    /// it only shows up where there is actually something to review.
    /// </summary>
    Task<IReadOnlyDictionary<string, Guid>> GetLatestThumbnailAnalysisIdsAsync(
        IReadOnlyCollection<string> videoIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a complete analysis for a video and persists the result (whether it succeeded,
    /// partially succeeded, or failed - a failed attempt is still recorded, then the triggering
    /// exception is rethrown so the caller gets an appropriate error response).
    /// </summary>
    Task<VideoAnalysis> AnalyzeAsync(string videoId, CancellationToken cancellationToken = default);

    /// <summary>All analyses ever run for a video, newest first.</summary>
    Task<IReadOnlyList<VideoAnalysis>> GetHistoryAsync(string videoId, CancellationToken cancellationToken = default);

    Task<VideoAnalysis?> GetByIdAsync(string videoId, Guid analysisId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes an approved title/description to YouTube, after checking whether the live video's
    /// metadata has changed since the analysis was run. The first call (without
    /// <see cref="PublishRequest.AcknowledgeConflict"/>) only reports a detected conflict; nothing
    /// is published until the caller explicitly re-confirms.
    /// </summary>
    Task<PublishResult> PublishAsync(
        string videoId, Guid analysisId, PublishRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves an admin-edited title/description against an analysis, as a draft. Makes no YouTube
    /// API call at all - purely a database update, kept separate from the AI-generated
    /// OptimizedTitle/OptimizedDescription so the original suggestion is never lost.
    /// </summary>
    /// <exception cref="Exceptions.VideoAnalysisNotFoundException">No such analysis exists for this video.</exception>
    Task<VideoAnalysis> SaveEditsAsync(
        string videoId, Guid analysisId, EditAnalysisRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves an admin-edited thumbnail image prompt against an analysis, as a draft. Makes no
    /// image-generation API call at all - purely a database update, so edits can be saved without
    /// spending a generation on every change.
    /// </summary>
    /// <exception cref="Exceptions.VideoAnalysisNotFoundException">No such analysis exists for this video.</exception>
    Task<VideoAnalysis> SaveThumbnailPromptAsync(
        string videoId, Guid analysisId, string imagePrompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a thumbnail image via the configured provider (see
    /// <see cref="Configuration.ThumbnailOptions"/>) from <paramref name="imagePrompt"/> and saves
    /// it to the analysis. The prompt (which may have been hand-edited) is persisted first, via the
    /// same write <see cref="SaveThumbnailPromptAsync"/> performs, before the image call runs - so
    /// an edit is never lost even if generation itself fails.
    /// </summary>
    /// <exception cref="Exceptions.VideoAnalysisNotFoundException">No such analysis exists for this video.</exception>
    /// <exception cref="Exceptions.ThumbnailGenerationException">The provider is not configured or the call failed.</exception>
    Task<VideoAnalysis> GenerateThumbnailImageAsync(
        string videoId, Guid analysisId, string imagePrompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-uploads the already-generated thumbnail image to the live YouTube video via
    /// thumbnails.set, without touching title/description and without the conflict check
    /// <see cref="PublishAsync"/> does. Meant for pushing a regenerated thumbnail again after a
    /// first publish, as many times as needed, without re-running the full publish flow each time.
    /// </summary>
    /// <exception cref="Exceptions.VideoAnalysisNotFoundException">No such analysis exists for this video.</exception>
    /// <exception cref="Exceptions.ThumbnailGenerationException">No thumbnail has been generated yet for this analysis.</exception>
    /// <exception cref="Exceptions.YouTubeIntegrationException">The thumbnail upload itself failed.</exception>
    Task<VideoAnalysis> PublishThumbnailOnlyAsync(
        string videoId, Guid analysisId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Classifies (or re-classifies) one analysis's theology category via
    /// <see cref="ICategorizationService"/> and saves it in place - the backfill path for analyses
    /// created before PrimaryCategory existed, or whose classification should be redone.
    /// </summary>
    /// <exception cref="Exceptions.VideoAnalysisNotFoundException">No such analysis exists for this video.</exception>
    Task<VideoAnalysis> CategorizeAsync(
        string videoId, Guid analysisId, CancellationToken cancellationToken = default);

    /// <summary>
    /// (VideoId, AnalysisId) for every video whose latest succeeded/partially-valid analysis has no
    /// PrimaryCategory yet - the backfill target set for the dashboard's "Categorize uncategorized" action.
    /// </summary>
    Task<IReadOnlyList<(string VideoId, Guid AnalysisId)>> GetUncategorizedAsync(CancellationToken cancellationToken = default);
}

/// <summary>Coarse per-video status shown on the admin dashboard.</summary>
public enum VideoDashboardStatus
{
    /// <summary>No analysis has ever been run for this video.</summary>
    NotAnalyzed,

    /// <summary>
    /// An analysis job is currently queued or running for this video. Computed from
    /// <see cref="IVideoAnalysisProgressTracker"/>, not from stored data - it overrides whatever
    /// the video's underlying status would otherwise be, since the dashboard should never show a
    /// stale pre-job status while a job is actually in flight.
    /// </summary>
    Analyzing,

    /// <summary>The latest analysis fully succeeded and has not been published to YouTube yet.</summary>
    Analyzed,

    /// <summary>
    /// The latest analysis was usable but one or more sections were dropped/corrected during
    /// validation (<see cref="AnalysisStatus.PartiallyValid"/>) - worth a closer look before publishing.
    /// </summary>
    AnalyzedPartial,

    /// <summary>
    /// The latest analysis attempt failed outright (<see cref="AnalysisStatus.Failed"/>) - nothing
    /// usable was produced, so this video needs re-analyzing.
    /// </summary>
    AnalyzedFailed,

    /// <summary>The latest analysis has been published (title/description, and thumbnail if generated) to YouTube.</summary>
    Published
}

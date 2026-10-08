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
}

/// <summary>Coarse per-video status shown on the admin dashboard.</summary>
public enum VideoDashboardStatus
{
    /// <summary>No analysis has ever been run for this video.</summary>
    NotAnalyzed,

    /// <summary>At least one analysis exists, but its result has not been published to YouTube.</summary>
    Analyzed,

    /// <summary>The latest analysis has been published (title/description, and thumbnail if generated) to YouTube.</summary>
    Published
}

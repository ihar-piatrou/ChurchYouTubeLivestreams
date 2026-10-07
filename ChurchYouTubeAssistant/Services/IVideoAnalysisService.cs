using ChurchYouTubeAssistant.Models;

namespace ChurchYouTubeAssistant.Services;

/// <summary>
/// Orchestrates one full AI video analysis: retrieve metadata and transcript, call OpenAI, validate
/// the response, persist it - and, separately, publishing an approved result back to YouTube.
/// </summary>
public interface IVideoAnalysisService
{
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
}

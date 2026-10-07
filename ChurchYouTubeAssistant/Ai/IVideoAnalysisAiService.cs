namespace ChurchYouTubeAssistant.Ai;

/// <summary>
/// Calls OpenAI to produce a complete structured video analysis from a transcript and existing
/// metadata, in one request by default, falling back to a multi-stage pipeline for transcripts too
/// long to fit comfortably in a single request/response.
/// </summary>
public interface IVideoAnalysisAiService
{
    /// <exception cref="Exceptions.AiAnalysisException">The OpenAI call failed or its response was unusable.</exception>
    Task<VideoAnalysisAiCallResult> AnalyzeAsync(
        VideoAnalysisInput input, CancellationToken cancellationToken = default);
}

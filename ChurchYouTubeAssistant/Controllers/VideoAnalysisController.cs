using ChurchYouTubeAssistant.Models;
using ChurchYouTubeAssistant.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChurchYouTubeAssistant.Controllers;

/// <summary>
/// AI video analysis: generate, review history, and (separately, with explicit approval) publish
/// optimized metadata back to YouTube.
/// </summary>
/// <remarks>
/// Routed under the same <c>/youtube/videos/{videoId}/...</c> prefix as the existing transcript
/// endpoint, rather than a generic <c>/api/videos/...</c> prefix, to match this project's existing
/// convention of keeping everything YouTube-video-related under one namespace.
/// </remarks>
[ApiController]
[Route("youtube/videos/{videoId}/analyses")]
[Produces("application/json")]
public sealed class VideoAnalysisController(IVideoAnalysisService analysisService) : ControllerBase
{
    /// <summary>
    /// Runs a complete AI analysis for the video: title/description optimization, sermon analysis,
    /// Bible references, chapters, thumbnail concept and Shorts candidates, in one OpenAI request
    /// (or a multi-stage fallback for unusually long transcripts). This is the single "Analyze
    /// Video with AI" operation from the frontend's point of view.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(VideoAnalysis), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<VideoAnalysis>> Analyze(
        [FromRoute] string videoId, CancellationToken cancellationToken) =>
        Ok(await analysisService.AnalyzeAsync(videoId, cancellationToken));

    /// <summary>Every analysis ever run for this video, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<VideoAnalysis>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<VideoAnalysis>>> GetHistory(
        [FromRoute] string videoId, CancellationToken cancellationToken) =>
        Ok(await analysisService.GetHistoryAsync(videoId, cancellationToken));

    /// <summary>One specific analysis, in full.</summary>
    [HttpGet("{analysisId:guid}")]
    [ProducesResponseType(typeof(VideoAnalysis), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VideoAnalysis>> GetById(
        [FromRoute] string videoId, [FromRoute] Guid analysisId, CancellationToken cancellationToken)
    {
        var analysis = await analysisService.GetByIdAsync(videoId, analysisId, cancellationToken);
        return analysis is null ? NotFound() : Ok(analysis);
    }

    /// <summary>
    /// Publishes the approved title/description to the real YouTube video. Never happens
    /// automatically - this endpoint is only ever reached by an explicit admin action, and the
    /// first call (without <see cref="PublishRequest.AcknowledgeConflict"/>) only reports whether
    /// the live video has changed since the analysis, without publishing anything.
    /// </summary>
    [HttpPost("{analysisId:guid}/publish")]
    [ProducesResponseType(typeof(PublishResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<PublishResult>> Publish(
        [FromRoute] string videoId,
        [FromRoute] Guid analysisId,
        [FromBody] PublishRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 100)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(request.Title)] = ["Title must be non-empty and at most 100 characters."]
            }));
        }

        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > 5000)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(request.Description)] = ["Description must be non-empty and at most 5000 characters."]
            }));
        }

        return Ok(await analysisService.PublishAsync(videoId, analysisId, request, cancellationToken));
    }

    /// <summary>
    /// Saves an admin-edited title/description as a draft against this analysis. Makes no YouTube
    /// API call at all - purely a database update, distinct from <see cref="Publish"/>.
    /// </summary>
    [HttpPost("{analysisId:guid}/edits")]
    [ProducesResponseType(typeof(VideoAnalysis), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VideoAnalysis>> SaveEdits(
        [FromRoute] string videoId,
        [FromRoute] Guid analysisId,
        [FromBody] EditAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 100)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(request.Title)] = ["Title must be non-empty and at most 100 characters."]
            }));
        }

        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > 5000)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(request.Description)] = ["Description must be non-empty and at most 5000 characters."]
            }));
        }

        return Ok(await analysisService.SaveEditsAsync(videoId, analysisId, request, cancellationToken));
    }
}

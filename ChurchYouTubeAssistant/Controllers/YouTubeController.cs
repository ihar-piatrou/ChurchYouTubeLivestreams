using ChurchYouTubeAssistant.Models;
using ChurchYouTubeAssistant.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChurchYouTubeAssistant.Controllers;

/// <summary>
/// Read-only endpoints over the connected church channel, plus the entry point of the Google
/// authorization flow.
/// </summary>
/// <remarks>
/// Controllers stay thin on purpose: they translate HTTP to a service call and back. Failures are
/// thrown as domain exceptions and turned into ProblemDetails centrally by
/// <see cref="Infrastructure.ApiExceptionHandler"/>, so no endpoint repeats that mapping.
/// </remarks>
[ApiController]
[Route("youtube")]
[Produces("application/json")]
public sealed class YouTubeController(
    IGoogleOAuthService oauthService,
    IYouTubeReadService youTubeReadService) : ControllerBase
{
    /// <summary>
    /// Starts Google OAuth: redirects the browser to Google's consent screen.
    /// Open this in a browser, not from JavaScript or Swagger.
    /// </summary>
    [HttpGet("connect")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public IActionResult Connect()
    {
        var authorizationUrl = oauthService.BuildAuthorizationUrl();

        // 302 so the browser follows it; the state value is already recorded server-side.
        return Redirect(authorizationUrl);
    }

    /// <summary>Reports whether YouTube is connected and whether it can refresh unattended.</summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(YouTubeConnectionStatusDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<YouTubeConnectionStatusDto>> GetStatus(CancellationToken cancellationToken) =>
        Ok(await oauthService.GetConnectionStatusAsync(cancellationToken));

    /// <summary>Returns the YouTube channel owned by the authorized Google account.</summary>
    [HttpGet("channel")]
    [ProducesResponseType(typeof(YouTubeChannelDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<YouTubeChannelDto>> GetChannel(CancellationToken cancellationToken) =>
        Ok(await youTubeReadService.GetAuthenticatedChannelAsync(cancellationToken));

    /// <summary>Returns the most recent uploads, newest first.</summary>
    /// <param name="maxResults">1-50, default 10.</param>
    /// <param name="includeDetails">
    /// Include duration, statistics, caption availability and livestream timings. Costs one extra
    /// YouTube quota unit.
    /// </param>
    [HttpGet("videos")]
    [ProducesResponseType(typeof(YouTubeVideoListDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<YouTubeVideoListDto>> GetVideos(
        [FromQuery] int maxResults = 10,
        [FromQuery] bool includeDetails = false,
        CancellationToken cancellationToken = default)
    {
        if (maxResults is < 1 or > 50)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(maxResults)] = ["maxResults must be between 1 and 50."]
            }));
        }

        return Ok(await youTubeReadService.GetLatestVideosAsync(maxResults, includeDetails, cancellationToken));
    }

    /// <summary>
    /// Returns the transcript of one video: picks a caption track and downloads its content.
    /// </summary>
    /// <param name="videoId">The video to fetch a transcript for.</param>
    /// <param name="languageCode">
    /// BCP-47 language to prefer (e.g. "en"). Omit to use any available track, preferring a
    /// manually created one over YouTube's auto-generated (ASR) guess.
    /// </param>
    /// <param name="format">Subtitle format: "srt" (default, keeps timing), "vtt" or "sbv".</param>
    /// <remarks>
    /// Costs 50 quota units for captions.list plus 200 for captions.download - by far the most
    /// expensive call in this API. Fetch once per video and let the caller cache the result rather
    /// than re-requesting it.
    /// </remarks>
    [HttpGet("videos/{videoId}/transcript")]
    [ProducesResponseType(typeof(YouTubeTranscriptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<YouTubeTranscriptDto>> GetTranscript(
        [FromRoute] string videoId,
        [FromQuery] string? languageCode = null,
        [FromQuery] string format = "srt",
        CancellationToken cancellationToken = default)
    {
        if (!IYouTubeReadService.SupportedTranscriptFormats.Contains(format, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(format)] =
                [
                    $"format must be one of: {string.Join(", ", IYouTubeReadService.SupportedTranscriptFormats)}."
                ]
            }));
        }

        return Ok(await youTubeReadService.GetVideoTranscriptAsync(
            videoId, languageCode, format, cancellationToken));
    }

    /// <summary>
    /// Revokes the authorization at Google and forgets the stored tokens. Useful in development to
    /// re-run the consent flow from a clean state.
    /// </summary>
    [HttpDelete("connection")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Disconnect(CancellationToken cancellationToken)
    {
        await oauthService.DisconnectAsync(cancellationToken);
        return NoContent();
    }
}

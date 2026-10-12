using System.Text;
using ChurchYouTubeAssistant.Configuration;
using ChurchYouTubeAssistant.Exceptions;
using ChurchYouTubeAssistant.Models;
using ChurchYouTubeAssistant.Models.ViewModels;
using ChurchYouTubeAssistant.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ChurchYouTubeAssistant.Controllers;

/// <summary>
/// The human-facing admin UI: a small dashboard over the same services the JSON API uses.
/// </summary>
/// <remarks>
/// This calls <see cref="IGoogleOAuthService"/> and <see cref="IYouTubeReadService"/> directly -
/// the same services <see cref="YouTubeController"/> and <see cref="OAuthController"/> call - rather
/// than making HTTP requests to this app's own JSON API. The JSON endpoints under <c>/youtube</c>
/// are unaffected and still work exactly as before for scripted/API use.
/// <para>
/// Unlike the API controllers, failures here are caught and turned into a friendly message on the
/// page instead of a ProblemDetails JSON body: a human looking at a browser should see "no
/// transcript available for this video", not a JSON blob.
/// </para>
/// </remarks>
public sealed class HomeController(
    IGoogleOAuthService oauthService,
    IYouTubeReadService youTubeReadService,
    IVideoAnalysisService analysisService,
    IBackgroundJobTracker jobTracker,
    IBackgroundTaskQueue taskQueue,
    IOptions<ThumbnailOptions> thumbnailOptions,
    IOptions<IdeogramOptions> ideogramOptions,
    IOptions<OpenAiImageOptions> openAiImageOptions,
    ILogger<HomeController> logger) : Controller
{
    /// <summary>The dashboard: connection status, channel summary, latest uploads.</summary>
    [HttpGet("/")]
    public async Task<IActionResult> Index([FromQuery] string? pageToken, CancellationToken cancellationToken)
    {
        var status = await oauthService.GetConnectionStatusAsync(cancellationToken);

        YouTubeChannelDto? channel = null;
        IReadOnlyList<YouTubeVideoDto> videos = [];
        IReadOnlyDictionary<string, VideoDashboardStatus> videoStatuses = new Dictionary<string, VideoDashboardStatus>();
        string? nextPageToken = null;
        string? prevPageToken = null;
        string? loadError = null;

        if (status.Connected)
        {
            try
            {
                channel = await youTubeReadService.GetAuthenticatedChannelAsync(cancellationToken);
                var list = await youTubeReadService.GetLatestVideosAsync(
                    maxResults: 10, includeDetails: true, pageToken, cancellationToken);

                // Livestreams that are still running (or otherwise unfinished) report a 0:00
                // duration until YouTube finishes processing them - not useful to analyze yet, so
                // hide them rather than showing a confusing "00:00" row. This can leave a page with
                // fewer than 10 rows; acceptable for this admin-only dashboard.
                videos = list.Videos.Where(v => v.Details?.Duration != TimeSpan.Zero).ToList();
                nextPageToken = list.NextPageToken;
                prevPageToken = list.PrevPageToken;

                videoStatuses = await analysisService.GetDashboardStatusesAsync(
                    videos.Select(v => v.VideoId).ToList(), cancellationToken);
            }
            catch (NoYouTubeChannelException ex)
            {
                loadError = ex.Message;
            }
            catch (YouTubeIntegrationException ex)
            {
                logger.LogWarning(ex, "Dashboard could not load the channel or its videos.");
                loadError = ex.Message;
            }
        }

        var model = new DashboardViewModel
        {
            Connected = status.Connected,
            CanRefreshUnattended = status.CanRefreshUnattended,
            StatusMessage = status.Message,
            FlashSuccess = Request.Query["success"] is { Count: > 0 } s ? s.ToString() : null,
            FlashError = Request.Query["error"] is { Count: > 0 } e ? e.ToString() : null,
            FlashWarnings = Request.Query["warnings"] is { Count: > 0 } w
                ? w.ToString().Split('|', StringSplitOptions.RemoveEmptyEntries)
                : [],
            Channel = channel,
            Videos = videos,
            VideoStatuses = videoStatuses,
            NextPageToken = nextPageToken,
            PrevPageToken = prevPageToken,
            LoadError = loadError
        };

        return View(model);
    }

    /// <summary>Revokes the authorization at Google and forgets the stored tokens.</summary>
    [HttpPost("/disconnect")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Disconnect(CancellationToken cancellationToken)
    {
        await oauthService.DisconnectAsync(cancellationToken);

        return Redirect(QueryHelpers.AddQueryString("/", "success", "Disconnected from YouTube."));
    }

    /// <summary>The transcript of one video.</summary>
    [HttpGet("/transcript/{videoId}")]
    public async Task<IActionResult> Transcript(
        string videoId,
        [FromQuery] string? languageCode,
        [FromQuery] string format = "srt",
        CancellationToken cancellationToken = default)
    {
        YouTubeTranscriptDto? transcript = null;
        string? errorMessage = null;

        try
        {
            transcript = await youTubeReadService.GetVideoTranscriptAsync(
                videoId, languageCode, format, cancellationToken);
        }
        catch (TranscriptNotAvailableException ex)
        {
            errorMessage = ex.Message;
        }
        catch (YouTubeNotConnectedException ex)
        {
            errorMessage = ex.Message;
        }
        catch (YouTubeIntegrationException ex)
        {
            logger.LogWarning(ex, "Could not load the transcript for video {VideoId}.", videoId);
            errorMessage = ex.Message;
        }

        return View(new TranscriptViewModel
        {
            VideoId = videoId,
            LanguageCode = languageCode,
            Format = format,
            Transcript = transcript,
            ErrorMessage = errorMessage
        });
    }

    /// <summary>
    /// The AI analysis review page: optimized metadata, sermon analysis, Bible references,
    /// chapters, thumbnail concept, and Shorts candidates for one video.
    /// </summary>
    [HttpGet("/videos/{videoId}/analysis")]
    public async Task<IActionResult> Analysis(
        string videoId, [FromQuery] Guid? analysisId, [FromQuery] Guid? jobId, CancellationToken cancellationToken)
    {
        // A finished job's own redirect URL already encodes the success/error flash - follow it
        // rather than rendering a "waiting" page for a job that is no longer actually running
        // (e.g. the browser reopened this exact URL after the job had already completed).
        if (jobId is { } id && jobTracker.Get(id) is { State: BackgroundJobState.Done, RedirectUrl: { } url })
        {
            return Redirect(url);
        }

        var history = await analysisService.GetHistoryAsync(videoId, cancellationToken);
        var selected = analysisId is { } aid
            ? history.FirstOrDefault(a => a.Id == aid)
            : history.FirstOrDefault();

        return View(BuildAnalysisViewModel(videoId, history, selected));
    }

    /// <summary>
    /// Starts a new AI analysis as a background job and redirects immediately to the analysis
    /// page with a job id - that page polls <see cref="JobStatus"/> and shows a waiting bar until
    /// the job finishes, rather than this request blocking for however long the OpenAI call takes.
    /// </summary>
    [HttpPost("/videos/{videoId}/analysis/analyze")]
    [ValidateAntiForgeryToken]
    public IActionResult AnalyzeVideo(string videoId)
    {
        var jobId = jobTracker.Create();

        taskQueue.Enqueue(async (services, ct) =>
        {
            jobTracker.MarkRunning(jobId);
            var scopedAnalysisService = services.GetRequiredService<IVideoAnalysisService>();

            string redirectUrl;
            try
            {
                var analysis = await scopedAnalysisService.AnalyzeAsync(videoId, ct);
                redirectUrl = QueryHelpers.AddQueryString(
                    $"/videos/{videoId}/analysis", new Dictionary<string, string?>
                    {
                        ["analysisId"] = analysis.Id.ToString(),
                        ["success"] = analysis.Status == AnalysisStatus.Succeeded
                            ? "Analysis complete."
                            : $"Analysis completed with some issues (status: {analysis.Status})."
                    });
            }
            catch (Exception ex) when (ex is Exceptions.AiAnalysisException or YouTubeNotConnectedException
                or TranscriptNotAvailableException or YouTubeIntegrationException)
            {
                logger.LogWarning(ex, "AI analysis failed for video {VideoId}.", videoId);
                redirectUrl = QueryHelpers.AddQueryString(
                    $"/videos/{videoId}/analysis", "error", $"Analysis failed: {ex.Message}");
            }

            jobTracker.Complete(jobId, redirectUrl);
        });

        return Redirect(QueryHelpers.AddQueryString(
            $"/videos/{videoId}/analysis", new Dictionary<string, string?>
            {
                ["jobId"] = jobId.ToString(),
                ["job"] = "analyze"
            }));
    }

    /// <summary>
    /// Publishes approved title/description to YouTube. The first submission (no acknowledged
    /// conflict) only reports a detected conflict and asks for confirmation; nothing is published
    /// until the admin explicitly resubmits with the conflict acknowledged.
    /// </summary>
    [HttpPost("/videos/{videoId}/analysis/{analysisId:guid}/publish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PublishAnalysis(
        string videoId,
        Guid analysisId,
        [FromForm] string title,
        [FromForm] string description,
        [FromForm] bool acknowledgeConflict,
        CancellationToken cancellationToken)
    {
        var history = await analysisService.GetHistoryAsync(videoId, cancellationToken);
        var selected = history.FirstOrDefault(a => a.Id == analysisId);

        if (string.IsNullOrWhiteSpace(title) || title.Length > 100 ||
            string.IsNullOrWhiteSpace(description) || description.Length > 5000)
        {
            return View("Analysis", BuildAnalysisViewModel(videoId, history, selected) with
            {
                FlashError = "Title must be 1-100 characters and description 1-5000 characters."
            });
        }

        var result = await analysisService.PublishAsync(
            videoId, analysisId,
            new PublishRequest { Title = title, Description = description, AcknowledgeConflict = acknowledgeConflict },
            cancellationToken);

        if (result.ConflictDetected && !result.Published)
        {
            return View("Analysis", BuildAnalysisViewModel(videoId, history, result.Analysis) with
            {
                ConflictDetected = true,
                ConflictDetail = result.ConflictDetail,
                ConflictLiveTitle = result.CurrentLiveTitle,
                ConflictLiveDescription = result.CurrentLiveDescription,
                PendingPublishTitle = title,
                PendingPublishDescription = description
            });
        }

        var successMessage = result switch
        {
            { ThumbnailAttempted: true, ThumbnailPublished: true } =>
                "Published title, description and thumbnail to YouTube.",
            { ThumbnailAttempted: true, ThumbnailPublished: false } =>
                $"Published title and description, but the thumbnail upload failed: {result.ThumbnailError}",
            _ => "Published to YouTube."
        };

        return Redirect(QueryHelpers.AddQueryString(
            $"/videos/{videoId}/analysis",
            new Dictionary<string, string?>
            {
                ["analysisId"] = analysisId.ToString(),
                [result.ThumbnailAttempted && !result.ThumbnailPublished ? "error" : "success"] = successMessage
            }));
    }

    /// <summary>
    /// Saves an edited thumbnail prompt as a draft. Makes no image-generation API call - distinct
    /// from <see cref="GenerateThumbnail"/>, so edits can be saved without spending a generation.
    /// </summary>
    [HttpPost("/videos/{videoId}/analysis/{analysisId:guid}/thumbnail/prompt")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveThumbnailPrompt(
        string videoId, Guid analysisId, [FromForm] string imagePrompt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imagePrompt))
        {
            return Redirect(QueryHelpers.AddQueryString(
                $"/videos/{videoId}/analysis", new Dictionary<string, string?>
                {
                    ["analysisId"] = analysisId.ToString(),
                    ["error"] = "Enter an image prompt before saving."
                }));
        }

        await analysisService.SaveThumbnailPromptAsync(videoId, analysisId, imagePrompt, cancellationToken);

        return Redirect(QueryHelpers.AddQueryString(
            $"/videos/{videoId}/analysis", new Dictionary<string, string?>
            {
                ["analysisId"] = analysisId.ToString(),
                ["success"] = "Thumbnail prompt saved (no image generated)."
            }));
    }

    /// <summary>
    /// Generates a thumbnail image via the configured provider (see <see cref="ThumbnailOptions"/>)
    /// as a background job, redirecting immediately to the analysis page with a job id - that page
    /// polls <see cref="JobStatus"/> and shows a waiting bar until the job finishes. The prompt is
    /// saved first, before the image call runs, so an edit is never lost even if generation fails
    /// (see <see cref="IVideoAnalysisService.GenerateThumbnailImageAsync"/>).
    /// </summary>
    [HttpPost("/videos/{videoId}/analysis/{analysisId:guid}/thumbnail")]
    [ValidateAntiForgeryToken]
    public IActionResult GenerateThumbnail(string videoId, Guid analysisId, [FromForm] string imagePrompt)
    {
        if (string.IsNullOrWhiteSpace(imagePrompt))
        {
            return Redirect(QueryHelpers.AddQueryString(
                $"/videos/{videoId}/analysis", new Dictionary<string, string?>
                {
                    ["analysisId"] = analysisId.ToString(),
                    ["error"] = "Enter an image prompt before generating a thumbnail."
                }));
        }

        var jobId = jobTracker.Create();

        taskQueue.Enqueue(async (services, ct) =>
        {
            jobTracker.MarkRunning(jobId);
            var scopedAnalysisService = services.GetRequiredService<IVideoAnalysisService>();

            string redirectUrl;
            try
            {
                await scopedAnalysisService.GenerateThumbnailImageAsync(videoId, analysisId, imagePrompt, ct);
                redirectUrl = QueryHelpers.AddQueryString(
                    $"/videos/{videoId}/analysis", new Dictionary<string, string?>
                    {
                        ["analysisId"] = analysisId.ToString(),
                        ["success"] = "Thumbnail image generated."
                    });
            }
            catch (ThumbnailGenerationException ex)
            {
                logger.LogWarning(ex, "Thumbnail generation failed for video {VideoId}.", videoId);
                redirectUrl = QueryHelpers.AddQueryString(
                    $"/videos/{videoId}/analysis", new Dictionary<string, string?>
                    {
                        ["analysisId"] = analysisId.ToString(),
                        ["error"] = $"Thumbnail generation failed: {ex.Message}"
                    });
            }

            jobTracker.Complete(jobId, redirectUrl);
        });

        return Redirect(QueryHelpers.AddQueryString(
            $"/videos/{videoId}/analysis", new Dictionary<string, string?>
            {
                ["analysisId"] = analysisId.ToString(),
                ["jobId"] = jobId.ToString(),
                ["job"] = "thumbnail"
            }));
    }

    /// <summary>Polling endpoint the Analysis page's waiting bar JavaScript calls every few seconds.</summary>
    [HttpGet("/jobs/{jobId:guid}/status")]
    public IActionResult JobStatus(Guid jobId)
    {
        var status = jobTracker.Get(jobId);
        if (status is null)
        {
            return NotFound();
        }

        return Json(new JobStatusResponse(status.State.ToString(), status.RedirectUrl));
    }

    /// <summary>
    /// Saves an edited title/description as a draft against this analysis. Does not call YouTube
    /// at all - purely a database update, distinct from <see cref="PublishAnalysis"/>.
    /// </summary>
    [HttpPost("/videos/{videoId}/analysis/{analysisId:guid}/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDraft(
        string videoId,
        Guid analysisId,
        [FromForm] string title,
        [FromForm] string description,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 100 ||
            string.IsNullOrWhiteSpace(description) || description.Length > 5000)
        {
            var history = await analysisService.GetHistoryAsync(videoId, cancellationToken);
            var selected = history.FirstOrDefault(a => a.Id == analysisId);

            return View("Analysis", BuildAnalysisViewModel(videoId, history, selected) with
            {
                FlashError = "Title must be 1-100 characters and description 1-5000 characters.",
                PendingPublishTitle = title,
                PendingPublishDescription = description
            });
        }

        await analysisService.SaveEditsAsync(
            videoId, analysisId, new EditAnalysisRequest { Title = title, Description = description }, cancellationToken);

        return Redirect(QueryHelpers.AddQueryString(
            $"/videos/{videoId}/analysis",
            new Dictionary<string, string?>
            {
                ["analysisId"] = analysisId.ToString(),
                ["success"] = "Draft saved (not published to YouTube)."
            }));
    }

    private VideoAnalysisPageViewModel BuildAnalysisViewModel(
        string videoId, IReadOnlyList<VideoAnalysis> history, VideoAnalysis? selected)
    {
        var prefilled = selected is null
            ? string.Empty
            : BuildPrefilledDescription(selected);

        return new VideoAnalysisPageViewModel
        {
            VideoId = videoId,
            History = history,
            Selected = selected,
            PrefilledDescription = prefilled,
            ThumbnailProviderLabel = BuildThumbnailProviderLabel(),
            PendingJobId = Guid.TryParse(Request.Query["jobId"], out var jobId) ? jobId : null,
            PendingJobKind = Request.Query["job"] is { Count: > 0 } jk ? jk.ToString() : null,
            FlashSuccess = Request.Query["success"] is { Count: > 0 } s ? s.ToString() : null,
            FlashError = Request.Query["error"] is { Count: > 0 } e ? e.ToString() : null
        };
    }

    private string BuildThumbnailProviderLabel() => thumbnailOptions.Value.Provider switch
    {
        ThumbnailProvider.OpenAi => $"OpenAI ({openAiImageOptions.Value.Model})",
        _ => $"Ideogram ({ideogramOptions.Value.Model})"
    };

    private static string BuildPrefilledDescription(VideoAnalysis analysis)
    {
        if (analysis.EditedDescription is { } edited)
        {
            // Once a draft has been saved, it is authoritative and may already include a
            // "Chapters:" block the client-side checkboxes built before submit - return it
            // verbatim. Re-appending a fresh chapters block here on every page load was the
            // cause of chapters appearing duplicated after Save Draft.
            return edited;
        }

        var description = analysis.OptimizedDescription ?? string.Empty;
        var includedChapters = analysis.Chapters.Where(c => c.Included).ToList();

        if (includedChapters.Count == 0)
        {
            return description;
        }

        var builder = new StringBuilder(description.TrimEnd());
        builder.AppendLine().AppendLine().AppendLine("Chapters:");
        foreach (var chapter in includedChapters.OrderBy(c => c.StartSeconds))
        {
            builder.AppendLine($"{chapter.StartTime} {chapter.Title}");
        }

        return builder.ToString();
    }
}

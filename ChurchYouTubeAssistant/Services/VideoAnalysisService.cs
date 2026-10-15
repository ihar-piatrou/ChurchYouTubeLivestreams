using ChurchYouTubeAssistant.Ai;
using ChurchYouTubeAssistant.Configuration;
using ChurchYouTubeAssistant.Data;
using ChurchYouTubeAssistant.Exceptions;
using ChurchYouTubeAssistant.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChurchYouTubeAssistant.Services;

/// <inheritdoc cref="IVideoAnalysisService"/>
public sealed class VideoAnalysisService(
    ChurchYouTubeAssistantDbContext db,
    IYouTubeReadService youTubeReadService,
    IYouTubeWriteService youTubeWriteService,
    IVideoAnalysisAiService aiService,
    IThumbnailImageService thumbnailService,
    ICategorizationService categorizationService,
    IVideoAnalysisPromptProvider promptProvider,
    IOptions<OpenAiOptions> openAiOptions,
    VideoAnalysisValidator validator,
    TimeProvider timeProvider,
    ILogger<VideoAnalysisService> logger) : IVideoAnalysisService
{
    public async Task<IReadOnlyDictionary<string, VideoDashboardStatus>> GetDashboardStatusesAsync(
        IReadOnlyCollection<string> videoIds, CancellationToken cancellationToken = default)
    {
        var statuses = videoIds.ToDictionary(id => id, _ => VideoDashboardStatus.NotAnalyzed);
        if (videoIds.Count == 0)
        {
            return statuses;
        }

        // Fetches a flat, trivially-translatable projection and does the "latest row per video"
        // grouping client-side instead of inside the EF query: neither selecting the full
        // VideoAnalysis entity (it owns several collection navigations EF can't materialize inside
        // a GroupBy subquery) nor a nested anonymous "latest" projection translates reliably here.
        // The dashboard only ever asks for one page of videos (<= 50), so this stays small.
        var rows = await db.VideoAnalyses
            .Where(a => videoIds.Contains(a.VideoId))
            .Select(a => new { a.VideoId, a.CreatedAtUtc, a.IsPublished, a.Status })
            .ToListAsync(cancellationToken);

        var latestPerVideo = rows
            .GroupBy(a => a.VideoId)
            .Select(g => g.OrderByDescending(a => a.CreatedAtUtc).First());

        foreach (var entry in latestPerVideo)
        {
            statuses[entry.VideoId] = entry.IsPublished
                ? VideoDashboardStatus.Published
                : entry.Status switch
                {
                    AnalysisStatus.Succeeded => VideoDashboardStatus.Analyzed,
                    AnalysisStatus.PartiallyValid => VideoDashboardStatus.AnalyzedPartial,
                    AnalysisStatus.Failed => VideoDashboardStatus.AnalyzedFailed,
                    // Pending is only ever meant to be transient (see AnalysisStatus.Pending), but a
                    // crashed job could in principle leave one stuck - fall back to "Analyzing" so the
                    // dashboard doesn't claim a result exists when none actually does.
                    _ => VideoDashboardStatus.Analyzing
                };
        }

        return statuses;
    }

    public async Task<IReadOnlyDictionary<string, Guid>> GetLatestThumbnailAnalysisIdsAsync(
        IReadOnlyCollection<string> videoIds, CancellationToken cancellationToken = default)
    {
        if (videoIds.Count == 0)
        {
            return new Dictionary<string, Guid>();
        }

        // See the comment in GetDashboardStatusesAsync: fetch a flat projection and group "latest
        // per video" client-side - grouping inside the EF query itself does not translate reliably.
        var rows = await db.VideoAnalyses
            .Where(a => videoIds.Contains(a.VideoId))
            .Select(a => new { a.VideoId, a.Id, a.CreatedAtUtc, a.ThumbnailImageGeneratedAtUtc })
            .ToListAsync(cancellationToken);

        var latestWithThumbnail = rows
            .GroupBy(a => a.VideoId)
            .Select(g => g.OrderByDescending(a => a.CreatedAtUtc).First())
            .Where(a => a.ThumbnailImageGeneratedAtUtc != null);

        return latestWithThumbnail.ToDictionary(a => a.VideoId, a => a.Id);
    }

    public async Task<IReadOnlyList<(string VideoId, Guid AnalysisId)>> GetUncategorizedAsync(
        CancellationToken cancellationToken = default)
    {
        var uncategorized = await db.VideoAnalyses
            .Where(a => a.Status == AnalysisStatus.Succeeded || a.Status == AnalysisStatus.PartiallyValid)
            .GroupBy(a => a.VideoId)
            .Select(g => new
            {
                VideoId = g.Key,
                AnalysisId = g.OrderByDescending(a => a.CreatedAtUtc).First().Id,
                PrimaryCategory = g.OrderByDescending(a => a.CreatedAtUtc).First().PrimaryCategory
            })
            .Where(x => x.PrimaryCategory == null)
            .ToListAsync(cancellationToken);

        return uncategorized.Select(x => (x.VideoId, x.AnalysisId)).ToList();
    }

    public async Task<VideoAnalysis> AnalyzeAsync(string videoId, CancellationToken cancellationToken = default)
    {
        // Snapshot the *current* live metadata, not whatever our own cache happens to hold - the
        // analysis's OriginalTitle/OriginalDescription must reflect reality at analysis time, since
        // the publish workflow later compares against this snapshot to detect external changes.
        var video = await youTubeReadService.GetVideoAsync(videoId, cancellationToken);
        var transcript = await youTubeReadService.GetVideoTranscriptAsync(videoId, cancellationToken: cancellationToken);

        var input = new Ai.VideoAnalysisInput
        {
            VideoId = videoId,
            OriginalTitle = video.Title,
            OriginalDescription = video.Description ?? string.Empty,
            DurationSeconds = video.Details?.Duration is { } duration ? (int)duration.TotalSeconds : null,
            PublishedAt = ToServiceSunday(video.PublishedAt),
            Transcript = transcript.Content
        };

        try
        {
            var aiResult = await aiService.AnalyzeAsync(input, cancellationToken);
            var validation = validator.Validate(aiResult.Result, input.DurationSeconds);

            var entity = BuildEntity(input, aiResult, validation);
            db.VideoAnalyses.Add(entity);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Saved {Status} analysis {AnalysisId} for video {VideoId} ({Warnings} warning(s), {Errors} error(s)).",
                entity.Status, entity.Id, videoId, validation.Warnings.Count, validation.Errors.Count);

            return entity;
        }
        catch (AiAnalysisException ex)
        {
            // The OpenAI call itself failed (or returned something unusable). Record the attempt -
            // per the spec's "record failed attempts separately" - then rethrow so the controller
            // still reports an error to the caller.
            var failed = new VideoAnalysis
            {
                Id = Guid.NewGuid(),
                VideoId = videoId,
                OriginalTitle = input.OriginalTitle,
                OriginalDescription = input.OriginalDescription,
                VideoDurationSeconds = input.DurationSeconds,
                Model = openAiOptions.Value.Model,
                PromptVersion = promptProvider.Version,
                Status = AnalysisStatus.Failed,
                Notes = [$"{ex.Error}: {ex.Message}"],
                CreatedAtUtc = timeProvider.GetUtcNow()
            };

            db.VideoAnalyses.Add(failed);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogError(
                ex, "AI analysis failed for video {VideoId}; recorded as failed analysis {AnalysisId}.",
                videoId, failed.Id);

            throw;
        }
    }

    public async Task<IReadOnlyList<VideoAnalysis>> GetHistoryAsync(
        string videoId, CancellationToken cancellationToken = default) =>
        await db.VideoAnalyses
            .Where(a => a.VideoId == videoId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<VideoAnalysis?> GetByIdAsync(
        string videoId, Guid analysisId, CancellationToken cancellationToken = default) =>
        db.VideoAnalyses.FirstOrDefaultAsync(a => a.VideoId == videoId && a.Id == analysisId, cancellationToken);

    public async Task<VideoAnalysis> SaveEditsAsync(
        string videoId, Guid analysisId, EditAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        var analysis = await db.VideoAnalyses.FirstOrDefaultAsync(
                a => a.VideoId == videoId && a.Id == analysisId, cancellationToken)
            ?? throw new VideoAnalysisNotFoundException($"Analysis {analysisId} for video {videoId} was not found.");

        analysis.EditedTitle = request.Title;
        analysis.EditedDescription = request.Description;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Saved edited title/description for analysis {AnalysisId} (video {VideoId}); not published to YouTube.",
            analysisId, videoId);

        return analysis;
    }

    public async Task<VideoAnalysis> SaveThumbnailPromptAsync(
        string videoId, Guid analysisId, string imagePrompt, CancellationToken cancellationToken = default)
    {
        var analysis = await db.VideoAnalyses.FirstOrDefaultAsync(
                a => a.VideoId == videoId && a.Id == analysisId, cancellationToken)
            ?? throw new VideoAnalysisNotFoundException($"Analysis {analysisId} for video {videoId} was not found.");

        analysis.EditedThumbnailPrompt = imagePrompt;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Saved edited thumbnail prompt for analysis {AnalysisId} (video {VideoId}); no image generated.",
            analysisId, videoId);

        return analysis;
    }

    public async Task<VideoAnalysis> GenerateThumbnailImageAsync(
        string videoId, Guid analysisId, string imagePrompt, CancellationToken cancellationToken = default)
    {
        // Saves the (possibly hand-edited) prompt as its own write, before the image call runs - an
        // edit the admin made must not be lost just because generation itself then fails.
        var analysis = await SaveThumbnailPromptAsync(videoId, analysisId, imagePrompt, cancellationToken);

        var image = await thumbnailService.GenerateAsync(imagePrompt, cancellationToken);

        analysis.ThumbnailImageData = image.ImageData;
        analysis.ThumbnailImageContentType = image.ContentType;
        analysis.ThumbnailImageGeneratedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Generated thumbnail image for analysis {AnalysisId} (video {VideoId}): {Bytes} bytes, {ContentType}.",
            analysisId, videoId, image.ImageData.Length, image.ContentType);

        return analysis;
    }

    public async Task<VideoAnalysis> PublishThumbnailOnlyAsync(
        string videoId, Guid analysisId, CancellationToken cancellationToken = default)
    {
        var analysis = await db.VideoAnalyses.FirstOrDefaultAsync(
                a => a.VideoId == videoId && a.Id == analysisId, cancellationToken)
            ?? throw new VideoAnalysisNotFoundException($"Analysis {analysisId} for video {videoId} was not found.");

        if (analysis.ThumbnailImageData is not { Length: > 0 } imageData)
        {
            throw new ThumbnailGenerationException(
                ThumbnailGenerationError.TransientFailure,
                "No thumbnail image has been generated yet for this analysis.");
        }

        await youTubeWriteService.SetThumbnailAsync(
            videoId, imageData, analysis.ThumbnailImageContentType ?? "image/png", cancellationToken);

        analysis.ThumbnailPublished = true;
        analysis.ThumbnailPublishedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Published just the thumbnail for analysis {AnalysisId} (video {VideoId}).", analysisId, videoId);

        return analysis;
    }

    public async Task<VideoAnalysis> CategorizeAsync(
        string videoId, Guid analysisId, CancellationToken cancellationToken = default)
    {
        var analysis = await db.VideoAnalyses.FirstOrDefaultAsync(
                a => a.VideoId == videoId && a.Id == analysisId, cancellationToken)
            ?? throw new VideoAnalysisNotFoundException($"Analysis {analysisId} for video {videoId} was not found.");

        var result = await categorizationService.CategorizeAsync(analysis, cancellationToken);

        analysis.PrimaryCategory = result.Primary;
        analysis.SecondaryCategories = result.Secondary.ToList();
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Categorized analysis {AnalysisId} (video {VideoId}) as {Primary} (+{SecondaryCount} secondary).",
            analysisId, videoId, result.Primary, result.Secondary.Count);

        return analysis;
    }

    public async Task<PublishResult> PublishAsync(
        string videoId, Guid analysisId, PublishRequest request, CancellationToken cancellationToken = default)
    {
        var analysis = await db.VideoAnalyses.FirstOrDefaultAsync(
                a => a.VideoId == videoId && a.Id == analysisId, cancellationToken)
            ?? throw new VideoAnalysisNotFoundException($"Analysis {analysisId} for video {videoId} was not found.");

        // Check the *live* video against the snapshot this analysis was run against - never
        // against our own cache, which could itself be stale.
        var live = await youTubeReadService.GetVideoAsync(videoId, cancellationToken);

        // Once this analysis has been published at least once, the correct baseline for "did
        // something else change this on YouTube" is what we ourselves last published, not the
        // pre-analysis OriginalTitle/OriginalDescription - otherwise a routine re-publish of the
        // same analysis (e.g. fixing a typo and clicking Update again) would always look like an
        // external conflict, since the live value now equals our own last publish, not the original.
        var expectedLiveTitle = analysis.IsPublished ? analysis.PublishedTitle ?? analysis.OriginalTitle : analysis.OriginalTitle;
        var expectedLiveDescription = analysis.IsPublished
            ? analysis.PublishedDescription ?? analysis.OriginalDescription
            : analysis.OriginalDescription;

        var conflict =
            !string.Equals(live.Title, expectedLiveTitle, StringComparison.Ordinal)
            || !string.Equals(live.Description ?? string.Empty, expectedLiveDescription, StringComparison.Ordinal);

        if (conflict && !request.AcknowledgeConflict)
        {
            logger.LogWarning(
                "Publish for analysis {AnalysisId} (video {VideoId}) blocked: live metadata has changed since analysis.",
                analysisId, videoId);

            return new PublishResult
            {
                Published = false,
                ConflictDetected = true,
                ConflictDetail =
                    "This video's title or description on YouTube has changed since this analysis was run. " +
                    "Review the current values and confirm to publish anyway.",
                CurrentLiveTitle = live.Title,
                CurrentLiveDescription = live.Description,
                Analysis = analysis
            };
        }

        await youTubeWriteService.UpdateVideoMetadataAsync(videoId, request.Title, request.Description, cancellationToken);

        analysis.IsPublished = true;
        analysis.PublishedAtUtc = timeProvider.GetUtcNow();
        analysis.PublishedTitle = request.Title;
        analysis.PublishedDescription = request.Description;

        var thumbnailAttempted = false;
        var thumbnailPublished = false;
        string? thumbnailError = null;

        if (request.IncludeThumbnail && analysis.ThumbnailImageData is { Length: > 0 } imageData)
        {
            thumbnailAttempted = true;
            try
            {
                await youTubeWriteService.SetThumbnailAsync(
                    videoId, imageData, analysis.ThumbnailImageContentType ?? "image/png", cancellationToken);

                analysis.ThumbnailPublished = true;
                analysis.ThumbnailPublishedAtUtc = timeProvider.GetUtcNow();
                thumbnailPublished = true;
            }
            catch (YouTubeIntegrationException ex)
            {
                // Title/description already succeeded and YouTube has no "undo" for that - a
                // thumbnail-specific failure is reported separately rather than masking the part
                // that worked or being treated as if the whole publish failed.
                logger.LogWarning(
                    ex, "Title/description published, but setting the thumbnail failed for video {VideoId}.", videoId);
                thumbnailError = ex.Message;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Published analysis {AnalysisId} for video {VideoId} to YouTube (thumbnail: {ThumbnailOutcome}).",
            analysisId, videoId,
            !thumbnailAttempted ? "none generated" : thumbnailPublished ? "published" : "failed");

        return new PublishResult
        {
            Published = true,
            ConflictDetected = conflict,
            Analysis = analysis,
            ThumbnailAttempted = thumbnailAttempted,
            ThumbnailPublished = thumbnailPublished,
            ThumbnailError = thumbnailError
        };
    }

    /// <summary>
    /// Services are always Sunday, but YouTube's publish timestamp can land a day or more late (e.g.
    /// a livestream finalized/published early Monday) - rolls a timestamp back to the most recent
    /// Sunday on or before it, in the server's local time (the same convention the dashboard already
    /// uses for display dates), so the AI gets the actual service date, not a late-publish artifact.
    /// </summary>
    private static DateTimeOffset? ToServiceSunday(DateTimeOffset? publishedAtUtc)
    {
        if (publishedAtUtc is not { } value)
        {
            return null;
        }

        var local = value.ToLocalTime();
        return local.AddDays(-(int)local.DayOfWeek);
    }

    private VideoAnalysis BuildEntity(
        Ai.VideoAnalysisInput input, Ai.VideoAnalysisAiCallResult aiResult, Ai.VideoAnalysisValidationResult validation)
    {
        var result = validation.Cleaned;

        var status = !validation.IsValid
            ? AnalysisStatus.Failed
            : validation.Warnings.Count > 0
                ? AnalysisStatus.PartiallyValid
                : AnalysisStatus.Succeeded;

        return new VideoAnalysis
        {
            Id = Guid.NewGuid(),
            VideoId = input.VideoId,
            OriginalTitle = input.OriginalTitle,
            OriginalDescription = input.OriginalDescription,
            Language = result.Language,
            VideoDurationSeconds = input.DurationSeconds,

            OptimizedTitle = result.Metadata.Optimized.Title,
            OptimizedDescription = result.Metadata.Optimized.Description,
            AlternativeTitles = result.Metadata.AlternativeTitles,
            TitleReasoning = result.Metadata.Reasoning,

            MainMessage = result.SermonAnalysis.MainMessage,
            MainQuestion = result.SermonAnalysis.MainQuestion,
            KeyThemes = result.SermonAnalysis.KeyThemes,
            Keywords = result.SermonAnalysis.Keywords,
            PrimaryCategory = TheologyCategoryCatalog.TryParse(result.SermonAnalysis.PrimaryCategory),
            SecondaryCategories = result.SermonAnalysis.SecondaryCategories
                .Select(TheologyCategoryCatalog.TryParse)
                .Where(c => c is not null)
                .Select(c => c!.Value)
                .ToList(),

            BibleReferences = result.BibleReferences,
            Chapters = result.Chapters,
            Thumbnail = result.Thumbnail,
            Shorts = result.Shorts,

            RawOpenAiResponse = aiResult.RawResponseJson,
            Model = aiResult.Model,
            PromptVersion = aiResult.PromptVersion,
            PromptTokens = aiResult.PromptTokens,
            CompletionTokens = aiResult.CompletionTokens,
            TotalTokens = aiResult.TotalTokens,
            UsedMultiStageFallback = aiResult.UsedMultiStageFallback,

            Status = status,
            Notes = [.. validation.Warnings, .. validation.Errors],
            CreatedAtUtc = timeProvider.GetUtcNow()
        };
    }
}

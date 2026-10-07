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
    IVideoAnalysisPromptProvider promptProvider,
    IOptions<OpenAiOptions> openAiOptions,
    VideoAnalysisValidator validator,
    TimeProvider timeProvider,
    ILogger<VideoAnalysisService> logger) : IVideoAnalysisService
{
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

    public async Task<PublishResult> PublishAsync(
        string videoId, Guid analysisId, PublishRequest request, CancellationToken cancellationToken = default)
    {
        var analysis = await db.VideoAnalyses.FirstOrDefaultAsync(
                a => a.VideoId == videoId && a.Id == analysisId, cancellationToken)
            ?? throw new VideoAnalysisNotFoundException($"Analysis {analysisId} for video {videoId} was not found.");

        // Check the *live* video against the snapshot this analysis was run against - never
        // against our own cache, which could itself be stale.
        var live = await youTubeReadService.GetVideoAsync(videoId, cancellationToken);

        var conflict =
            !string.Equals(live.Title, analysis.OriginalTitle, StringComparison.Ordinal)
            || !string.Equals(live.Description ?? string.Empty, analysis.OriginalDescription, StringComparison.Ordinal);

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
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Published analysis {AnalysisId} for video {VideoId} to YouTube.", analysisId, videoId);

        return new PublishResult
        {
            Published = true,
            ConflictDetected = conflict,
            Analysis = analysis
        };
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

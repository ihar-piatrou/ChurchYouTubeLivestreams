using ChurchYouTubeAssistant.Models.Ai;

namespace ChurchYouTubeAssistant.Ai;

/// <summary>
/// Enforces YouTube's hard platform rules (chapter minimums, Shorts duration caps, timestamp
/// bounds) and basic sanity limits on a model-produced analysis, before it is stored or shown.
/// </summary>
/// <remarks>
/// Philosophy: drop or mechanically correct individual items that fail a rule (a too-short
/// chapter, an over-long Short) rather than failing the whole analysis, matching the spec's own
/// instruction to omit unreliable chapters/Shorts rather than publish them. The analysis as a
/// whole is only marked invalid when its core content (title and description) is unusable.
/// </remarks>
public sealed class VideoAnalysisValidator
{
    private const int MaxTitleLength = 100;
    private const int MaxDescriptionLength = 5000;
    private const int MinChapterDurationSeconds = 10;
    private const int MinRequiredChapters = 3;
    private const int MaxShortDurationSeconds = 60;
    private const int MinShortDurationSeconds = 5;

    public VideoAnalysisValidationResult Validate(VideoAnalysisResult result, int? videoDurationSeconds)
    {
        var warnings = new List<string>();
        var errors = new List<string>();

        ValidateLanguage(result, warnings);
        ValidateTitleAndDescription(result, warnings, errors);
        ValidateAlternativeTitles(result, warnings);
        ValidateBibleReferences(result, videoDurationSeconds, warnings);
        ValidateChapters(result, videoDurationSeconds, warnings);
        ValidateShorts(result, videoDurationSeconds, warnings);
        ValidateThumbnail(result, warnings);

        return new VideoAnalysisValidationResult(result, warnings, errors);
    }

    private static void ValidateLanguage(VideoAnalysisResult result, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(result.Language))
        {
            result.Language = "unknown";
            warnings.Add("Model did not report a language; defaulted to 'unknown'.");
        }
    }

    private static void ValidateTitleAndDescription(
        VideoAnalysisResult result, List<string> warnings, List<string> errors)
    {
        var title = result.Metadata.Optimized.Title;
        if (string.IsNullOrWhiteSpace(title))
        {
            errors.Add("The model returned an empty optimized title.");
        }
        else if (title.Length > MaxTitleLength)
        {
            result.Metadata.Optimized.Title = TruncateAtWordBoundary(title, MaxTitleLength);
            warnings.Add($"Optimized title exceeded {MaxTitleLength} characters and was truncated.");
        }

        var description = result.Metadata.Optimized.Description;
        if (string.IsNullOrWhiteSpace(description))
        {
            errors.Add("The model returned an empty optimized description.");
        }
        else if (description.Length > MaxDescriptionLength)
        {
            result.Metadata.Optimized.Description = TruncateAtWordBoundary(description, MaxDescriptionLength);
            warnings.Add($"Optimized description exceeded {MaxDescriptionLength} characters and was truncated.");
        }
    }

    private static void ValidateAlternativeTitles(VideoAnalysisResult result, List<string> warnings)
    {
        result.Metadata.AlternativeTitles = result.Metadata.AlternativeTitles
            .Where(t => !string.IsNullOrWhiteSpace(t) && t.Length <= MaxTitleLength)
            .Take(5) // generous upper bound; 3 is the target, more is tolerated rather than discarded
            .ToList();

        if (result.Metadata.AlternativeTitles.Count == 0)
        {
            warnings.Add("Model returned no usable alternative titles.");
        }
    }

    private static void ValidateBibleReferences(
        VideoAnalysisResult result, int? videoDurationSeconds, List<string> warnings)
    {
        var before = result.BibleReferences.Count;

        result.BibleReferences = result.BibleReferences
            .Where(reference => !string.IsNullOrWhiteSpace(reference.Reference))
            .Select(reference =>
            {
                if (reference.StartSeconds is { } seconds
                    && (seconds < 0 || (videoDurationSeconds is { } duration && seconds > duration)))
                {
                    // An out-of-bounds timestamp is more likely a model slip than a real signal;
                    // keep the reference itself (it may still be accurate) but drop the timestamp.
                    return reference with { StartSeconds = null };
                }

                return reference;
            })
            .ToList();

        if (result.BibleReferences.Count < before)
        {
            warnings.Add($"Removed {before - result.BibleReferences.Count} Bible reference(s) with no reference text.");
        }
    }

    private void ValidateChapters(VideoAnalysisResult result, int? videoDurationSeconds, List<string> warnings)
    {
        var chapters = result.Chapters
            .Where(c => !string.IsNullOrWhiteSpace(c.Title) && c.StartSeconds >= 0)
            .Where(c => videoDurationSeconds is null || c.StartSeconds <= videoDurationSeconds)
            .OrderBy(c => c.StartSeconds)
            .ToList();

        // Drop exact-duplicate start times, keeping the first.
        chapters = chapters
            .GroupBy(c => c.StartSeconds)
            .Select(g => g.First())
            .OrderBy(c => c.StartSeconds)
            .ToList();

        // Drop chapters shorter than the platform minimum, measured against the next remaining
        // chapter's start (or the video's end for the last one).
        var filtered = new List<ChapterInfo>();
        for (var i = 0; i < chapters.Count; i++)
        {
            var next = i + 1 < chapters.Count ? chapters[i + 1].StartSeconds : videoDurationSeconds;
            var durationKnownAndTooShort = next is { } nextStart && nextStart - chapters[i].StartSeconds < MinChapterDurationSeconds;

            if (durationKnownAndTooShort)
            {
                continue;
            }

            filtered.Add(chapters[i]);
        }

        if (filtered.Count > 0 && filtered[0].StartSeconds != 0)
        {
            // YouTube requires the first chapter to start at 00:00. The content from the true
            // start up to the first identified transition belongs to this chapter anyway.
            filtered[0] = filtered[0] with { StartSeconds = 0, StartTime = SrtTranscript.FormatSeconds(0) };
            warnings.Add("First chapter did not start at 00:00; adjusted to 00:00 as YouTube requires.");
        }

        if (filtered.Count < MinRequiredChapters)
        {
            if (result.Chapters.Count > 0)
            {
                warnings.Add(
                    $"Fewer than {MinRequiredChapters} valid chapters after validation; chapters omitted " +
                    "entirely rather than publishing an incomplete list.");
            }

            result.Chapters = [];
        }
        else
        {
            if (filtered.Count < result.Chapters.Count)
            {
                warnings.Add($"Removed {result.Chapters.Count - filtered.Count} invalid chapter(s).");
            }

            result.Chapters = filtered;
        }
    }

    private void ValidateShorts(VideoAnalysisResult result, int? videoDurationSeconds, List<string> warnings)
    {
        var before = result.Shorts.Count;

        var cleaned = result.Shorts
            .Where(s => !string.IsNullOrWhiteSpace(s.TranscriptExcerpt))
            .Where(s => s.EndSeconds > s.StartSeconds)
            .Where(s => videoDurationSeconds is null || s.EndSeconds <= videoDurationSeconds)
            .Select(s => s with { DurationSeconds = s.EndSeconds - s.StartSeconds })
            .Where(s => s.DurationSeconds is >= MinShortDurationSeconds and <= MaxShortDurationSeconds)
            .Select(s => s with { Score = Math.Clamp(s.Score, 1, 10) })
            .OrderByDescending(s => s.Score)
            .ToList();

        if (cleaned.Count < before)
        {
            warnings.Add(
                $"Removed {before - cleaned.Count} Shorts candidate(s) with invalid timestamps or duration " +
                $"outside {MinShortDurationSeconds}-{MaxShortDurationSeconds} seconds.");
        }

        result.Shorts = cleaned;
    }

    private static void ValidateThumbnail(VideoAnalysisResult result, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(result.Thumbnail.Headline))
        {
            warnings.Add("Model returned no thumbnail headline.");
        }

        result.Thumbnail.AlternativeHeadlines = result.Thumbnail.AlternativeHeadlines
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .ToList();
    }

    private static string TruncateAtWordBoundary(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        var cut = text[..maxLength];
        var lastSpace = cut.LastIndexOf(' ');
        return (lastSpace > maxLength / 2 ? cut[..lastSpace] : cut).TrimEnd();
    }
}

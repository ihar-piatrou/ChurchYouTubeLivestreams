using ChurchYouTubeAssistant.Ai;
using ChurchYouTubeAssistant.Models.Ai;
using Xunit;

namespace ChurchYouTubeAssistant.Tests.Ai;

public sealed class VideoAnalysisValidatorTests
{
    private readonly VideoAnalysisValidator _validator = new();

    private static VideoAnalysisResult MinimalValidResult() => new()
    {
        Language = "ru",
        Metadata = new OptimizedMetadata
        {
            Optimized = new OptimizedTitleDescription { Title = "A sermon title", Description = "A description." },
            AlternativeTitles = ["Alt 1", "Alt 2", "Alt 3"],
            Reasoning = "Because it reflects the main theme."
        },
        SermonAnalysis = new SermonAnalysis { MainMessage = "msg", MainQuestion = "q?" }
    };

    [Fact]
    public void Validate_EmptyTitle_IsRecordedAsError()
    {
        var result = MinimalValidResult();
        result.Metadata.Optimized.Title = "";

        var outcome = _validator.Validate(result, videoDurationSeconds: null);

        Assert.False(outcome.IsValid);
        Assert.Contains(outcome.Errors, e => e.Contains("title", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_EmptyDescription_IsRecordedAsError()
    {
        var result = MinimalValidResult();
        result.Metadata.Optimized.Description = "   ";

        var outcome = _validator.Validate(result, videoDurationSeconds: null);

        Assert.False(outcome.IsValid);
        Assert.Contains(outcome.Errors, e => e.Contains("description", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_TitleOverPlatformLimit_IsTruncatedNotRejected()
    {
        var result = MinimalValidResult();
        result.Metadata.Optimized.Title = new string('x', 150);

        var outcome = _validator.Validate(result, videoDurationSeconds: null);

        Assert.True(outcome.IsValid);
        Assert.True(outcome.Cleaned.Metadata.Optimized.Title.Length <= 100);
        Assert.Contains(outcome.Warnings, w => w.Contains("truncated"));
    }

    [Fact]
    public void Validate_FirstChapterNotAtZero_IsAdjustedToZero()
    {
        var result = MinimalValidResult();
        result.Chapters =
        [
            new ChapterInfo { StartSeconds = 15, StartTime = "00:00:15", Title = "Intro" },
            new ChapterInfo { StartSeconds = 60, StartTime = "00:01:00", Title = "Point 1" },
            new ChapterInfo { StartSeconds = 200, StartTime = "00:03:20", Title = "Point 2" }
        ];

        var outcome = _validator.Validate(result, videoDurationSeconds: 400);

        Assert.Equal(0, outcome.Cleaned.Chapters[0].StartSeconds);
        Assert.Contains(outcome.Warnings, w => w.Contains("00:00"));
    }

    [Fact]
    public void Validate_FewerThanThreeValidChapters_AreOmittedEntirely()
    {
        var result = MinimalValidResult();
        result.Chapters =
        [
            new ChapterInfo { StartSeconds = 0, StartTime = "00:00:00", Title = "Intro" },
            new ChapterInfo { StartSeconds = 60, StartTime = "00:01:00", Title = "Point 1" }
        ];

        var outcome = _validator.Validate(result, videoDurationSeconds: 200);

        Assert.Empty(outcome.Cleaned.Chapters);
        Assert.Contains(outcome.Warnings, w => w.Contains("omitted"));
    }

    [Fact]
    public void Validate_ChapterShorterThanTenSeconds_IsDropped()
    {
        var result = MinimalValidResult();
        result.Chapters =
        [
            new ChapterInfo { StartSeconds = 0, StartTime = "00:00:00", Title = "Intro" },
            new ChapterInfo { StartSeconds = 5, StartTime = "00:00:05", Title = "Too short" }, // only 7s before the next
            new ChapterInfo { StartSeconds = 12, StartTime = "00:00:12", Title = "Still close" },
            new ChapterInfo { StartSeconds = 60, StartTime = "00:01:00", Title = "Point 1" },
            new ChapterInfo { StartSeconds = 200, StartTime = "00:03:20", Title = "Point 2" }
        ];

        var outcome = _validator.Validate(result, videoDurationSeconds: 400);

        Assert.DoesNotContain(outcome.Cleaned.Chapters, c => c.Title == "Too short");
    }

    [Fact]
    public void Validate_ChapterPastVideoDuration_IsDropped()
    {
        var result = MinimalValidResult();
        result.Chapters =
        [
            new ChapterInfo { StartSeconds = 0, StartTime = "00:00:00", Title = "Intro" },
            new ChapterInfo { StartSeconds = 60, StartTime = "00:01:00", Title = "Point 1" },
            new ChapterInfo { StartSeconds = 200, StartTime = "00:03:20", Title = "Point 2" },
            new ChapterInfo { StartSeconds = 9999, StartTime = "02:46:39", Title = "Beyond the video" }
        ];

        var outcome = _validator.Validate(result, videoDurationSeconds: 400);

        Assert.DoesNotContain(outcome.Cleaned.Chapters, c => c.Title == "Beyond the video");
    }

    [Theory]
    [InlineData(1000, 1000, false)] // end == start: invalid
    [InlineData(1000, 995, false)]  // end < start: invalid
    [InlineData(1000, 1030, true)]  // 30s: valid
    [InlineData(1000, 1065, false)] // 65s: exceeds the 60s cap
    [InlineData(1000, 1002, false)] // 2s: below the 5s floor
    public void Validate_ShortsDuration_EnforcesBoundaries(int start, int end, bool shouldSurvive)
    {
        var result = MinimalValidResult();
        result.Shorts =
        [
            new ShortCandidate
            {
                Title = "Clip", StartSeconds = start, EndSeconds = end, StartTime = "x", EndTime = "y",
                DurationSeconds = end - start, Hook = "h", MainMessage = "m", Conclusion = "c", Reason = "r",
                Score = 5, Category = "cat", TranscriptExcerpt = "some text", TimestampConfidence = "high"
            }
        ];

        var outcome = _validator.Validate(result, videoDurationSeconds: 2000);

        Assert.Equal(shouldSurvive, outcome.Cleaned.Shorts.Count == 1);
    }

    [Fact]
    public void Validate_ShortsScoreOutOfRange_IsClamped()
    {
        var result = MinimalValidResult();
        result.Shorts =
        [
            new ShortCandidate
            {
                Title = "Clip", StartSeconds = 100, EndSeconds = 130, StartTime = "x", EndTime = "y",
                DurationSeconds = 30, Hook = "h", MainMessage = "m", Conclusion = "c", Reason = "r",
                Score = 57, Category = "cat", TranscriptExcerpt = "text", TimestampConfidence = "high"
            }
        ];

        var outcome = _validator.Validate(result, videoDurationSeconds: 2000);

        Assert.Equal(10, outcome.Cleaned.Shorts[0].Score);
    }

    [Fact]
    public void Validate_ShortsAreSortedByScoreDescending()
    {
        var result = MinimalValidResult();
        ShortCandidate Make(int score) => new()
        {
            Title = $"Clip {score}", StartSeconds = 100, EndSeconds = 130, StartTime = "x", EndTime = "y",
            DurationSeconds = 30, Hook = "h", MainMessage = "m", Conclusion = "c", Reason = "r",
            Score = score, Category = "cat", TranscriptExcerpt = "text", TimestampConfidence = "high"
        };
        result.Shorts = [Make(3), Make(9), Make(5)];

        var outcome = _validator.Validate(result, videoDurationSeconds: 2000);

        Assert.Equal([9, 5, 3], outcome.Cleaned.Shorts.Select(s => s.Score));
    }

    [Fact]
    public void Validate_BibleReferenceWithOutOfBoundsTimestamp_KeepsReferenceButClearsTimestamp()
    {
        var result = MinimalValidResult();
        result.BibleReferences =
        [
            new BibleReferenceInfo { Reference = "John 3:16", IsPrimary = true, ReferenceType = "explicit", Context = "ctx", StartSeconds = 99999 }
        ];

        var outcome = _validator.Validate(result, videoDurationSeconds: 400);

        Assert.Single(outcome.Cleaned.BibleReferences);
        Assert.Null(outcome.Cleaned.BibleReferences[0].StartSeconds);
    }

    [Fact]
    public void Validate_BibleReferenceWithNoReferenceText_IsRemoved()
    {
        var result = MinimalValidResult();
        result.BibleReferences =
        [
            new BibleReferenceInfo { Reference = "", IsPrimary = false, ReferenceType = "inferred", Context = "ctx" }
        ];

        var outcome = _validator.Validate(result, videoDurationSeconds: null);

        Assert.Empty(outcome.Cleaned.BibleReferences);
    }
}

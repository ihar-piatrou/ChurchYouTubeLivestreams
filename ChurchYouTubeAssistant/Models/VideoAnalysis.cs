using ChurchYouTubeAssistant.Models.Ai;

namespace ChurchYouTubeAssistant.Models;

/// <summary>
/// One complete AI analysis of a video: the inputs it was run against, the model's structured
/// output, validation outcome, token usage, and publication state. A video can have many of these
/// over time (re-running analysis never overwrites a previous one).
/// </summary>
/// <remarks>
/// <see cref="OriginalTitle"/>/<see cref="OriginalDescription"/> are snapshotted at analysis time
/// and never touched afterwards - they are the authoritative record of what YouTube had before this
/// analysis, independent of whatever <see cref="VideoRecord"/> happens to hold later.
/// </remarks>
public sealed class VideoAnalysis
{
    public Guid Id { get; set; }
    public required string VideoId { get; set; }

    public required string OriginalTitle { get; set; }
    public required string OriginalDescription { get; set; }
    public string? Language { get; set; }
    public int? VideoDurationSeconds { get; set; }

    // --- AI-generated content, exactly as produced (after validation/cleanup) ---
    public string? OptimizedTitle { get; set; }
    public string? OptimizedDescription { get; set; }
    public List<string> AlternativeTitles { get; set; } = [];
    public string? TitleReasoning { get; set; }

    public string? MainMessage { get; set; }
    public string? MainQuestion { get; set; }
    public List<string> KeyThemes { get; set; } = [];
    public List<string> Keywords { get; set; } = [];

    public List<BibleReferenceInfo> BibleReferences { get; set; } = [];
    public List<ChapterInfo> Chapters { get; set; } = [];
    public ThumbnailConcept? Thumbnail { get; set; }
    public List<ShortCandidate> Shorts { get; set; } = [];

    // --- User-edited versions, kept separate from what the AI generated (spec requirement: never
    //     conflate an edited suggestion with the original model output). Null until the admin edits. ---
    public string? EditedTitle { get; set; }
    public string? EditedDescription { get; set; }

    /// <summary>
    /// Admin-edited thumbnail image prompt, saved before image generation runs - generation always
    /// uses this value once it exists, never silently falling back to Thumbnail.ImagePrompt.
    /// </summary>
    public string? EditedThumbnailPrompt { get; set; }

    // --- Audit / provenance ---
    public string? RawOpenAiResponse { get; set; }
    public required string Model { get; set; }
    public required string PromptVersion { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
    public bool UsedMultiStageFallback { get; set; }

    public required AnalysisStatus Status { get; set; }

    /// <summary>Validation warnings and/or the error that caused <see cref="Status"/> to not be Succeeded.</summary>
    public List<string> Notes { get; set; } = [];

    public required DateTimeOffset CreatedAtUtc { get; set; }

    // --- Publication (title/description) ---
    public bool IsPublished { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public string? PublishedTitle { get; set; }
    public string? PublishedDescription { get; set; }

    // --- Generated thumbnail image (via Ideogram, from Thumbnail.ImagePrompt) ---
    public byte[]? ThumbnailImageData { get; set; }
    public string? ThumbnailImageContentType { get; set; }
    public DateTimeOffset? ThumbnailImageGeneratedAtUtc { get; set; }

    /// <summary>Whether the generated image has been uploaded as the video's live YouTube thumbnail.</summary>
    public bool ThumbnailPublished { get; set; }
    public DateTimeOffset? ThumbnailPublishedAtUtc { get; set; }
}

using System.Text.Json.Serialization;

namespace ChurchYouTubeAssistant.Models.Ai;

/// <summary>
/// The complete structured output of one AI video analysis. This shape is deliberately shared by
/// three roles at once: it is what OpenAI's structured-output JSON deserializes into, what gets
/// stored (as JSON columns, see <see cref="VideoAnalysis"/>), and what the frontend receives - one
/// cohesive analysis document rather than three slightly different shapes to keep in sync.
/// </summary>
/// <remarks>
/// Plain mutable classes with parameterless constructors on purpose: both System.Text.Json
/// deserialization and EF Core's owned-type JSON column mapping work most reliably against this
/// shape, without extra converters or constructor-binding configuration.
/// </remarks>
public sealed record VideoAnalysisResult
{
    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    [JsonPropertyName("metadata")]
    public OptimizedMetadata Metadata { get; set; } = new();

    [JsonPropertyName("sermonAnalysis")]
    public SermonAnalysis SermonAnalysis { get; set; } = new();

    [JsonPropertyName("bibleReferences")]
    public List<BibleReferenceInfo> BibleReferences { get; set; } = [];

    [JsonPropertyName("chapters")]
    public List<ChapterInfo> Chapters { get; set; } = [];

    [JsonPropertyName("thumbnail")]
    public ThumbnailConcept Thumbnail { get; set; } = new();

    [JsonPropertyName("shorts")]
    public List<ShortCandidate> Shorts { get; set; } = [];
}

public sealed record OptimizedMetadata
{
    [JsonPropertyName("optimized")]
    public OptimizedTitleDescription Optimized { get; set; } = new();

    [JsonPropertyName("alternativeTitles")]
    public List<string> AlternativeTitles { get; set; } = [];

    /// <summary>Brief explanation of why the recommended title was chosen.</summary>
    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; } = string.Empty;
}

public sealed record OptimizedTitleDescription
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

public sealed record SermonAnalysis
{
    [JsonPropertyName("mainMessage")]
    public string MainMessage { get; set; } = string.Empty;

    [JsonPropertyName("mainQuestion")]
    public string MainQuestion { get; set; } = string.Empty;

    [JsonPropertyName("keyThemes")]
    public List<string> KeyThemes { get; set; } = [];

    [JsonPropertyName("keywords")]
    public List<string> Keywords { get; set; } = [];
}

public sealed record BibleReferenceInfo
{
    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;

    [JsonPropertyName("isPrimary")]
    public bool IsPrimary { get; set; }

    /// <summary>"explicit" (the preacher stated the reference) or "inferred".</summary>
    [JsonPropertyName("referenceType")]
    public string ReferenceType { get; set; } = string.Empty;

    [JsonPropertyName("context")]
    public string Context { get; set; } = string.Empty;

    [JsonPropertyName("startSeconds")]
    public int? StartSeconds { get; set; }
}

public sealed record ChapterInfo
{
    [JsonPropertyName("startSeconds")]
    public int StartSeconds { get; set; }

    /// <summary>Formatted as HH:MM:SS, derived from <see cref="StartSeconds"/> - never hand-authored by the model.</summary>
    [JsonPropertyName("startTime")]
    public string StartTime { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Whether this chapter is currently selected for inclusion in the published description.
    /// Defaults true; the UI lets the admin exclude individual chapters before publishing.
    /// </summary>
    [JsonPropertyName("included")]
    public bool Included { get; set; } = true;
}

public sealed record ThumbnailConcept
{
    [JsonPropertyName("headline")]
    public string Headline { get; set; } = string.Empty;

    [JsonPropertyName("alternativeHeadlines")]
    public List<string> AlternativeHeadlines { get; set; } = [];

    [JsonPropertyName("visualConcept")]
    public string VisualConcept { get; set; } = string.Empty;

    [JsonPropertyName("imagePrompt")]
    public string ImagePrompt { get; set; } = string.Empty;

    [JsonPropertyName("composition")]
    public string Composition { get; set; } = string.Empty;

    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; } = string.Empty;
}

public sealed record ShortCandidate
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("startSeconds")]
    public int StartSeconds { get; set; }

    [JsonPropertyName("endSeconds")]
    public int EndSeconds { get; set; }

    [JsonPropertyName("startTime")]
    public string StartTime { get; set; } = string.Empty;

    [JsonPropertyName("endTime")]
    public string EndTime { get; set; } = string.Empty;

    [JsonPropertyName("durationSeconds")]
    public int DurationSeconds { get; set; }

    [JsonPropertyName("hook")]
    public string Hook { get; set; } = string.Empty;

    [JsonPropertyName("mainMessage")]
    public string MainMessage { get; set; } = string.Empty;

    [JsonPropertyName("conclusion")]
    public string Conclusion { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    /// <summary>1-10, highest first.</summary>
    [JsonPropertyName("score")]
    public int Score { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("transcriptExcerpt")]
    public string TranscriptExcerpt { get; set; } = string.Empty;

    /// <summary>"high", "medium" or "low" - SRT timestamps are not word-accurate; see VideoAnalysisPrompt.</summary>
    [JsonPropertyName("timestampConfidence")]
    public string TimestampConfidence { get; set; } = string.Empty;

    [JsonPropertyName("requiresManualReview")]
    public bool RequiresManualReview { get; set; }
}

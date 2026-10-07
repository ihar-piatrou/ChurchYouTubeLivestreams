namespace ChurchYouTubeAssistant.Ai;

/// <summary>Everything the AI analysis service needs about one video to analyze it.</summary>
public sealed record VideoAnalysisInput
{
    public required string VideoId { get; init; }
    public required string OriginalTitle { get; init; }
    public required string OriginalDescription { get; init; }

    /// <summary>A hint such as "ru"; the model is still asked to detect/confirm the language itself.</summary>
    public string? LanguageHint { get; init; }

    public int? DurationSeconds { get; init; }

    /// <summary>The raw SRT transcript text (captions.download format), timestamped.</summary>
    public required string Transcript { get; init; }
}

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

    /// <summary>
    /// The actual Sunday service date, already rolled back from YouTube's raw publish timestamp if
    /// that timestamp landed on a later day (see VideoAnalysisService.ToServiceSunday - every
    /// service is Sunday, but a livestream can finish publishing a day or more late). The fallback
    /// date the model must use for the title's service-identifier segment when the original title
    /// has no date of its own (see TITLE RULES in the prompt). Never guessed/invented - this real
    /// value or nothing.
    /// </summary>
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>The raw SRT transcript text (captions.download format), timestamped.</summary>
    public required string Transcript { get; init; }
}

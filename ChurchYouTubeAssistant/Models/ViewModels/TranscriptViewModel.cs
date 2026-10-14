namespace ChurchYouTubeAssistant.Models.ViewModels;

/// <summary>View model for the transcript page (Home/Transcript).</summary>
public sealed class TranscriptViewModel
{
    public required string VideoId { get; init; }
    public string? LanguageCode { get; init; }
    public required string Format { get; init; }

    /// <summary>The dashboard page token this video was opened from, if any; used to build the
    /// "Back to dashboard" link so it returns to the same page.</summary>
    public string? PageToken { get; init; }

    public YouTubeTranscriptDto? Transcript { get; init; }

    /// <summary>Set when the video has no captions, or the API call otherwise failed.</summary>
    public string? ErrorMessage { get; init; }
}

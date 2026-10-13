namespace ChurchYouTubeAssistant.Services;

/// <summary>
/// Tracks which video ids currently have an AI analysis job in flight (queued or running), purely
/// so the dashboard can show "Analyzing..." instead of the stale pre-job status while a background
/// job for that video is outstanding. Separate from <see cref="IBackgroundJobTracker"/>, which is
/// keyed by job id for the single-job polling/waiting-bar flow, not by video id.
/// </summary>
public interface IVideoAnalysisProgressTracker
{
    /// <summary>Marks a video as having an analysis job in flight. Call before enqueuing the job.</summary>
    void MarkStarted(string videoId);

    /// <summary>Clears the in-flight marker. Call once the job finishes, whether it succeeded or not.</summary>
    void MarkFinished(string videoId);

    /// <summary>Which of the given video ids currently have an analysis job in flight.</summary>
    IReadOnlySet<string> GetInProgress(IReadOnlyCollection<string> videoIds);
}

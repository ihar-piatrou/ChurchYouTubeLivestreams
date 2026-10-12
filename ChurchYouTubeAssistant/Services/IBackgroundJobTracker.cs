using ChurchYouTubeAssistant.Models;

namespace ChurchYouTubeAssistant.Services;

/// <summary>
/// Tracks the lifecycle of a background job so the browser can poll for completion and follow its
/// result redirect, instead of a full-page request blocking for however long the AI call takes.
/// </summary>
public interface IBackgroundJobTracker
{
    /// <summary>Registers a new job in the Pending state and returns its id.</summary>
    Guid Create();

    /// <summary>Marks a job as actively running (purely informational for the UI).</summary>
    void MarkRunning(Guid jobId);

    /// <summary>Marks a job finished and records the URL the browser should follow next.</summary>
    void Complete(Guid jobId, string redirectUrl);

    /// <summary>Null for an unknown (or expired) job id.</summary>
    BackgroundJobStatus? Get(Guid jobId);
}

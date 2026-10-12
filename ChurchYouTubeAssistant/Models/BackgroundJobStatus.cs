namespace ChurchYouTubeAssistant.Models;

/// <summary>Coarse lifecycle of a background job tracked for the UI's polling progress bar.</summary>
public enum BackgroundJobState
{
    /// <summary>Queued, not yet picked up by the background worker.</summary>
    Pending,

    /// <summary>The background worker is actively running it.</summary>
    Running,

    /// <summary>
    /// Finished - successfully or not. The job's own redirect URL already encodes success/error
    /// (via "success"/"error" query parameters), so this type does not need a separate Failed
    /// state: the browser follows <see cref="BackgroundJobStatus.RedirectUrl"/> either way.
    /// </summary>
    Done
}

/// <summary>A background job's current state, as returned to the page's polling JavaScript.</summary>
public sealed record BackgroundJobStatus
{
    public required BackgroundJobState State { get; init; }

    /// <summary>Set only once <see cref="State"/> is <see cref="BackgroundJobState.Done"/>.</summary>
    public string? RedirectUrl { get; init; }
}

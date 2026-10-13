namespace ChurchYouTubeAssistant.Configuration;

/// <summary>Tuning for <see cref="Services.QueuedHostedService"/>, bound from the "BackgroundJobs" section.</summary>
public sealed class BackgroundJobOptions
{
    public const string SectionName = "BackgroundJobs";

    /// <summary>
    /// How many queued jobs (Analyze / Generate Thumbnail / batch-analyze, etc.) run at the same
    /// time. Higher values finish a bulk "Analyze selected" batch faster but issue more concurrent
    /// OpenAI/YouTube calls at once, risking rate limits - 3 is a middle ground, tune via
    /// appsettings without a code change.
    /// </summary>
    public int MaxConcurrency { get; init; } = 3;
}

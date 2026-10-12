using ChurchYouTubeAssistant.Models;
using Microsoft.Extensions.Caching.Memory;

namespace ChurchYouTubeAssistant.Services;

/// <summary>
/// Job status store backed by <see cref="IMemoryCache"/> - same trade-off as
/// <see cref="Auth.MemoryCacheOAuthStateStore"/>: in-process, so jobs do not survive a restart and
/// do not work across multiple instances. That is fine while this is a single admin-triggered
/// process; move to a distributed cache (Redis) if this is ever scaled out. The expiration also
/// doubles as cleanup - nothing else ever removes a finished job's entry.
/// </summary>
public sealed class MemoryCacheBackgroundJobTracker(IMemoryCache cache) : IBackgroundJobTracker
{
    private const string KeyPrefix = "background-job:";

    /// <summary>
    /// How long a job's status stays queryable after being created. Generous relative to the
    /// polling interval (a few seconds) and to how long a human leaves a browser tab open waiting.
    /// </summary>
    private static readonly TimeSpan JobLifetime = TimeSpan.FromMinutes(30);

    public Guid Create()
    {
        var jobId = Guid.NewGuid();
        Set(jobId, new BackgroundJobStatus { State = BackgroundJobState.Pending });
        return jobId;
    }

    public void MarkRunning(Guid jobId) =>
        Set(jobId, new BackgroundJobStatus { State = BackgroundJobState.Running });

    public void Complete(Guid jobId, string redirectUrl) =>
        Set(jobId, new BackgroundJobStatus { State = BackgroundJobState.Done, RedirectUrl = redirectUrl });

    public BackgroundJobStatus? Get(Guid jobId) =>
        cache.TryGetValue(KeyPrefix + jobId, out BackgroundJobStatus? status) ? status : null;

    private void Set(Guid jobId, BackgroundJobStatus status) =>
        cache.Set(KeyPrefix + jobId, status, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = JobLifetime
        });
}

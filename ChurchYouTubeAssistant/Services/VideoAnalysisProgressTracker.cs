using System.Collections.Concurrent;

namespace ChurchYouTubeAssistant.Services;

/// <inheritdoc cref="IVideoAnalysisProgressTracker"/>
/// <remarks>
/// In-process only, same trade-off as <see cref="MemoryCacheBackgroundJobTracker"/>: fine for a
/// single admin instance, would need a shared store to scale out. No expiration is needed here -
/// every caller that adds an entry is itself responsible for removing it once its job ends.
/// </remarks>
public sealed class VideoAnalysisProgressTracker : IVideoAnalysisProgressTracker
{
    private readonly ConcurrentDictionary<string, byte> _inProgress = new();

    public void MarkStarted(string videoId) => _inProgress[videoId] = 0;

    public void MarkFinished(string videoId) => _inProgress.TryRemove(videoId, out _);

    public IReadOnlySet<string> GetInProgress(IReadOnlyCollection<string> videoIds) =>
        videoIds.Where(id => _inProgress.ContainsKey(id)).ToHashSet();
}

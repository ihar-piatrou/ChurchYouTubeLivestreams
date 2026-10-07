namespace ChurchYouTubeAssistant.Services;

/// <summary>
/// Write access to the connected church channel. Deliberately separate from
/// <see cref="IYouTubeReadService"/> - see that interface's remarks - so the ability to publish to
/// the real channel is an explicit, reviewable dependency, not an accident of class membership.
/// Requires the <c>youtube.force-ssl</c> scope (already requested for captions.download).
/// </summary>
public interface IYouTubeWriteService
{
    /// <summary>
    /// Updates a video's title and description, preserving every other existing snippet field
    /// (category, tags, language, etc.) by fetching the current snippet first and only mutating
    /// the two fields being published - videos.update replaces the entire part it is given.
    /// </summary>
    /// <exception cref="Exceptions.YouTubeNotConnectedException">YouTube has not been connected.</exception>
    /// <exception cref="Exceptions.YouTubeIntegrationException">The video was not found, or the API call failed.</exception>
    Task UpdateVideoMetadataAsync(
        string videoId, string title, string description, CancellationToken cancellationToken = default);
}

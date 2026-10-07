using ChurchYouTubeAssistant.Models;

namespace ChurchYouTubeAssistant.Services;

/// <summary>
/// Read-only access to the connected church channel on the YouTube Data API.
/// </summary>
/// <remarks>
/// Named "read" on purpose. This service must be incapable of changing the channel: when
/// videos.update and thumbnails.set arrive they belong behind a separate write-side service, so
/// that the ability to publish is an explicit, reviewable dependency rather than an accident of
/// being in the same class. The required scope is now <c>youtube.readonly</c> +
/// <c>youtube.force-ssl</c> (the latter purely because captions.download demands it - see
/// <see cref="Configuration.GoogleOAuthOptions.Scopes"/>), which technically grants more than this
/// class uses; that gap is the contract this interface exists to hold.
/// </remarks>
public interface IYouTubeReadService
{
    /// <summary>
    /// The only formats captions.download accepts, per the YouTube Data API reference. Exposed here
    /// so the controller can validate the <c>format</c> query parameter without depending on the
    /// concrete <see cref="YouTubeReadService"/> implementation.
    /// </summary>
    static readonly string[] SupportedTranscriptFormats = ["srt", "vtt", "sbv"];

    /// <summary>Retrieves the channel owned by the authorized Google account (channels.list, mine=true).</summary>
    /// <exception cref="Exceptions.YouTubeNotConnectedException">YouTube has not been connected.</exception>
    /// <exception cref="Exceptions.NoYouTubeChannelException">The Google account has no channel.</exception>
    /// <exception cref="Exceptions.YouTubeIntegrationException">The YouTube Data API call failed.</exception>
    Task<YouTubeChannelDto> GetAuthenticatedChannelAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the most recent uploads from the channel's uploads playlist, newest first.
    /// </summary>
    /// <param name="maxResults">How many videos to return (1-50).</param>
    /// <param name="includeDetails">
    /// Also fetch duration, statistics, caption availability and livestream timings with one extra
    /// videos.list call.
    /// </param>
    Task<YouTubeVideoListDto> GetLatestVideosAsync(
        int maxResults = 10,
        bool includeDetails = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the transcript of one video: lists its caption tracks (captions.list), picks one,
    /// and downloads its content (captions.download).
    /// </summary>
    /// <param name="videoId">The video to fetch a transcript for.</param>
    /// <param name="languageCode">
    /// BCP-47 language to prefer (e.g. "en"). When null, any available track is used, preferring a
    /// manually created one over YouTube's auto-generated (ASR) guess.
    /// </param>
    /// <param name="format">Subtitle format to download: "srt" (default, keeps timing), "vtt" or "sbv".</param>
    /// <exception cref="Exceptions.YouTubeNotConnectedException">YouTube has not been connected.</exception>
    /// <exception cref="Exceptions.TranscriptNotAvailableException">
    /// The video has no captions at all, or none in the requested language.
    /// </exception>
    /// <exception cref="Exceptions.YouTubeIntegrationException">The YouTube Data API call failed.</exception>
    Task<YouTubeTranscriptDto> GetVideoTranscriptAsync(
        string videoId,
        string? languageCode = null,
        string format = "srt",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves one video's current metadata directly (videos.list, id={videoId}), independent of
    /// the uploads playlist. Used wherever the *current, live* title/description of a specific
    /// video matters - e.g. snapshotting it before an AI analysis, or checking for conflicting
    /// changes immediately before publishing.
    /// </summary>
    /// <exception cref="Exceptions.YouTubeNotConnectedException">YouTube has not been connected.</exception>
    /// <exception cref="Exceptions.YouTubeIntegrationException">The video was not found, or the API call failed.</exception>
    Task<YouTubeVideoDto> GetVideoAsync(string videoId, CancellationToken cancellationToken = default);
}
